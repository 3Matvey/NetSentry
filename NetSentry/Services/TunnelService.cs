using Microsoft.Win32.SafeHandles;
using NetSentry.Crypto;
using NetSentry.Drivers;
using NetSentry.Framing;
using NetSentry.Models;
using NetSentry.Network;
using NetSentry.ResultPattern;
using NetSentry.Routing;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using static NetSentry.Drivers.LinuxNative;

namespace NetSentry.Services
{
    /// <summary>
    /// Оркестратор VPN-туннеля: создаёт, хранит, удаляет туннели и запускает I/O-циклы.
    /// </summary>
    public class TunnelService : ITunnelService
    {
        private readonly ICryptoProvider _crypto;
        private readonly ITunAdapter _tun;
        private readonly IRouteManager _route;
        private readonly IFramer _framer;
        private readonly IUdpTransport _udp;

        // Текущие конфигурации и токены отмены
        private readonly ConcurrentDictionary<string, TunnelConfig> _configs = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cts = new();

        public TunnelService(
            ICryptoProvider crypto,
            ITunAdapter tun,
            IRouteManager route,
            IFramer framer,
            IUdpTransport udp)
        {
            _crypto = crypto;
            _tun = tun;
            _route = route;
            _framer = framer;
            _udp = udp;
        }

        public Task<Result<TunnelConfig>> CreateAsync(string peerName, int durationHours)
        {
            var config = _crypto.CreateConfig(peerName, durationHours);
            var secret = _crypto.GetSharedSecret(config.TunnelId);

            _tun.CreateInterface(config);
            _route.ApplyRouting(config);

            var cts = new CancellationTokenSource();
            _configs[config.TunnelId] = config;
            _cts[config.TunnelId] = cts;

            StartTunToUdpLoop(config, secret, cts.Token);
            StartUdpToTunLoop(config, secret, cts.Token);

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

        private void StartTunToUdpLoop(TunnelConfig cfg, byte[] secret, CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                var aead = new System.Security.Cryptography.ChaCha20Poly1305(secret);

                byte[] nonce = new byte[12];
                byte[] tag = new byte[16];
                byte[] rawBuf = ArrayPool<byte>.Shared.Rent(1500);

                try
                {
#if true
                    // открываем дескриптор и получаем его из TunAdapter (повторяя логику CreateInterface, но не закрывая fd)
                    int fd = open("/dev/net/tun", O_RDWR);
                    // … ioctl точно так же, как в TunAdapter …
                    var tunStream = new FileStream(new SafeFileHandle(fd, true),
                                                   FileAccess.ReadWrite,
                                                   bufferSize: 1500,
                                                   isAsync: true);
#elif WINDOWS
                    // аналогично: получить handle из WintunNative и обернуть его в Stream
                    var tunStream = new WintunStream(adapterHandle); 
#else
                    throw new PlatformNotSupportedException();
#endif
                    while (!token.IsCancellationRequested)
                    {
                        int ipLen = await tunStream.ReadAsync(rawBuf.AsMemory(0, rawBuf.Length), token);
                        if (ipLen <= 0) continue;

                        // Используем только byte[] для арендуемых буферов, чтобы не хранить Span<byte> за await
                        byte[] frameBufArr = default!;
                        byte[] cipherBufArr = default!;
                        byte[] packetBufArr = default!;
#pragma warning disable CA2014 // стек вряд-ли переполнится, но если что арендуем)
                        Span<byte> frameBuf = ipLen <= 512
                            ? stackalloc byte[1600]
                            : (frameBufArr = ArrayPool<byte>.Shared.Rent(1600)).AsSpan(0, 1600);
                        Span<byte> cipherBuf = ipLen <= 512
                            ? stackalloc byte[1600]
                            : (cipherBufArr = ArrayPool<byte>.Shared.Rent(1600)).AsSpan(0, 1600);
                        Span<byte> packetBuf = ipLen <= 512
                            ? stackalloc byte[12 + 1600 + 16]
                            : (packetBufArr = ArrayPool<byte>.Shared.Rent(12 + 1600 + 16)).AsSpan(0, 12 + 1600 + 16);
#pragma warning restore CA2014
                        try
                        {
                            int hdrLen = _framer.Frame(cfg.TunnelId, rawBuf.AsSpan(0, ipLen), frameBuf);

                            Random.Shared.NextBytes(nonce);
                            aead.Encrypt(nonce, frameBuf[..hdrLen], cipherBuf, tag);

                            packetBuf.Clear();
                            nonce.CopyTo(packetBuf);
                            cipherBuf[..hdrLen].CopyTo(packetBuf[12..]);
                            tag.CopyTo(packetBuf[(12 + hdrLen)..]);

                            await _udp.SendAsync(cfg, packetBuf[..(12 + hdrLen + 16)].ToArray());
                        }
                        finally
                        {
                            // Возвращаем буферы в пул, если они были арендованы
                            if (frameBufArr != null) ArrayPool<byte>.Shared.Return(frameBufArr);
                            if (cipherBufArr != null) ArrayPool<byte>.Shared.Return(cipherBufArr);
                            if (packetBufArr != null) ArrayPool<byte>.Shared.Return(packetBufArr);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rawBuf);
                }
            }, token);
        }

        private void StartUdpToTunLoop(TunnelConfig cfg, byte[] secret, CancellationToken token)
        {
            _ = Task.Run(async () =>
            {
                const int MaxFrameSize = 1600;
                const int HeaderSize = 12 /*nonce*/ + 16 /*tag*/;
                const int MinFrameSize = HeaderSize + 1;

                var aead = new ChaCha20Poly1305(secret);
                // используем арендуемые буферы, чтобы потом вернуть их в пул
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
                        {
                            //_logger.LogWarning("Too-short frame ({Length} bytes) on tunnel {TunnelId}", length, cfg.TunnelId);
                            continue;
                        }

                        // расширяем буфер, если нужно
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

                        // дешифруем
                        try
                        {
                            aead.Decrypt(nonce, ciphertext, tag, ipBuf);
                        }
                        catch (CryptographicException ex)
                        {
                            //_logger.LogWarning(ex, "Decrypt failed for tunnel {TunnelId}", cfg.TunnelId);
                            continue;
                        }

                        // дефреймим
                        int ipLen;
                        try
                        {
                            ipLen = _framer.Deframe(ciphertext, out var parsedId, ipBuf);
                        }
                        catch (Exception ex)
                        {
                            //_logger.LogWarning(ex, "Deframe failed for tunnel {TunnelId}", cfg.TunnelId);
                            continue;
                        }

                        // асинхронно пишем IP-пакет в TUN
                        try
                        {
                            await tunStream.WriteAsync(ipBuf.AsMemory(0, ipLen), token);
                        }
                        catch (Exception ex)
                        {
                            //_logger.LogError(ex, "WriteAsync to TUN failed for tunnel {TunnelId}", cfg.TunnelId);
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
