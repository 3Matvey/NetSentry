using Microsoft.Win32.SafeHandles;
using NetSentry.Crypto;
using NetSentry.Drivers;
using NetSentry.Drivers.Windows;
using NetSentry.Framing;
using NetSentry.Models;
using NetSentry.Network;
using NetSentry.Routing;
using NetSentry.Shared.Platform;
using NetSentry.Shared.ResultPattern;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using static NetSentry.Drivers.Linux.LinuxNative;

namespace NetSentry.Services
{
    /// <summary>
    /// Оркестратор VPN-туннеля: создаёт, хранит, удаляет туннели и запускает I/O-циклы.
    /// </summary>
    public class TunnelService : ITunnelService
    {
        private readonly ICryptoProvider _crypto;
        private readonly TunAdapter _tun;
        private readonly IRouteManager _route;
        private readonly IFramer _framer;
        private readonly IUdpTransport _udp;
        private readonly IPlatformInfo _platform;

        private readonly ConcurrentDictionary<string, TunnelConfig> _configs = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();

        public TunnelService(
            ICryptoProvider crypto,
            TunAdapter tun,
            IRouteManager route,
            IFramer framer,
            IUdpTransport udp,
            IPlatformInfo platform)
        {
            _crypto = crypto;
            _tun = tun;
            _route = route;
            _framer = framer;
            _udp = udp;
            _platform = platform;
        }

        public Task<Result<TunnelConfig>> CreateAsync(string peerName, int durationHours)
        {
            var config = _crypto.CreateConfig(peerName, durationHours);

            _tun.CreateInterface(config);
            _route.ApplyRouting(config);

            var cts = new CancellationTokenSource();
            _configs[config.TunnelId] = config;
            _cts[config.TunnelId] = cts;

            StartTunToUdpLoop(config, cts.Token);
            StartUdpToTunLoop(config, cts.Token);

            return Task.FromResult(Result<TunnelConfig>.Success(config));
        }

        public Task<Result<TunnelConfig>> GetAsync(string tunnelId)
        {
            if (_configs.TryGetValue(tunnelId, out var cfg))
                return Task.FromResult(Result<TunnelConfig>.Success(cfg));

            return Task.FromResult(Result<TunnelConfig>.Failure(
                Error.NotFound("TunnelNotFound", "Tunnel not found")));
        }

        public Task<Result> DeleteAsync(string tunnelId)
        {
            if (!_configs.ContainsKey(tunnelId))
                return Task.FromResult(Result.Failure(
                    Error.NotFound("TunnelNotFound", "Tunnel not found")));

            if (_cts.TryRemove(tunnelId, out var cts))
                cts.Cancel();

            _route.RemoveRouting(tunnelId);
            _tun.RemoveInterface(tunnelId);
            _crypto.RemoveSecret(tunnelId);

            _configs.TryRemove(tunnelId, out _);
            return Task.FromResult(Result.Success());
        }

        private void StartTunToUdpLoop(TunnelConfig cfg, CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                var session = _crypto.GetSession(cfg.TunnelId);

                byte[] rawBuf = ArrayPool<byte>.Shared.Rent(1500);

                try
                {
                    using var tunStream = _tun.OpenTunStream(cfg);

                    //Span<byte> frameStack = stackalloc byte[2048];

                    while (!token.IsCancellationRequested)
                    {
                        int ipLen = await tunStream.ReadAsync(rawBuf.AsMemory(0, rawBuf.Length), token);
                        if (ipLen <= 0) continue;

                        int framedLenGuess = ipLen + 64;

                        byte[]? frameArr = null;
                        Span<byte> frameBuf =
                            framedLenGuess <= 2048
                                ? stackalloc byte[framedLenGuess]
                                : (frameArr = ArrayPool<byte>.Shared.Rent(framedLenGuess)).AsSpan(0, framedLenGuess);

                        byte[]? packetArr = null;
                        Span<byte> nonce = stackalloc byte[12];
                        Span<byte> tag = stackalloc byte[16];

                        try
                        {
                            int hdrLen = _framer.Frame(cfg.TunnelId, rawBuf.AsSpan(0, ipLen), frameBuf);

                            int totalLen = 12 + hdrLen + 16;
                            packetArr = ArrayPool<byte>.Shared.Rent(totalLen);
                            var packetBuf = packetArr.AsSpan(0, totalLen);

                            session.Encrypt(frameBuf[..hdrLen],
                                            nonce,
                                            packetBuf.Slice(12, hdrLen),
                                            tag);

                            nonce.CopyTo(packetBuf);
                            tag.CopyTo(packetBuf.Slice(12 + hdrLen, 16));

                            await _udp.SendAsync(cfg, packetArr.AsMemory(0, totalLen));
                        }
                        finally
                        {
                            if (packetArr != null) ArrayPool<byte>.Shared.Return(packetArr);
                            if (frameArr != null) ArrayPool<byte>.Shared.Return(frameArr);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rawBuf);
                }
            }, token);
        }

        private void StartUdpToTunLoop(TunnelConfig cfg, CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                const int MaxFrameSize = 1600;
                const int HeaderSize = 12 + 16;
                const int MinFrameSize = HeaderSize + 1;

                var session = _crypto.GetSession(cfg.TunnelId);

                byte[] ipBuf = ArrayPool<byte>.Shared.Rent(1500);
                byte[] udpBuf = ArrayPool<byte>.Shared.Rent(MaxFrameSize);

                try
                {
                    using var tunStream = _tun.OpenTunStream(cfg);

                    await foreach (var (_, data) in _udp.ReceiveAsync(token))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        int length = data.Length;
                        if (length < MinFrameSize)
                            continue;

                        if (length > udpBuf.Length)
                        {
                            ArrayPool<byte>.Shared.Return(udpBuf);
                            udpBuf = ArrayPool<byte>.Shared.Rent(length);
                        }

                        data.CopyTo(udpBuf.AsMemory(0, length));
                        var span = udpBuf.AsSpan(0, length);

                        var nonce = span.Slice(0, 12);
                        var tag = span.Slice(length - 16, 16);
                        var ciphertext = span.Slice(12, length - HeaderSize);

                        if (!session.Decrypt(nonce, ciphertext, tag, ipBuf))
                            continue;

                        int ipLen;
                        try
                        {
                            ipLen = _framer.Deframe(ipBuf.AsSpan(0, ciphertext.Length), out var parsedId, ipBuf);
                        }
                        catch
                        {
                            continue;
                        }

                        try
                        {
                            await tunStream.WriteAsync(ipBuf.AsMemory(0, ipLen), token);
                        }
                        catch
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(udpBuf);
                    ArrayPool<byte>.Shared.Return(ipBuf);
                }
            }, token);
        }
    }
}
