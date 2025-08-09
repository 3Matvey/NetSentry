using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NetSentry.Models;

namespace NetSentry.Crypto
{
    public class CryptoProvider : ICryptoProvider
    {
        const int LISTEN_PORT = 51888;

        private readonly ECDiffieHellman _serverDh;
        private readonly byte[] _serverPub;      // Base64-bytes of SPKI
        private readonly ConcurrentDictionary<string, byte[]> _sharedSecrets = [];
        // ↓ Сессии шифрования по tunnelId
        private readonly ConcurrentDictionary<string, TunnelCryptoSession> _sessions = [];

        public CryptoProvider()
        {
            _serverDh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            _serverPub = _serverDh.PublicKey.ExportSubjectPublicKeyInfo();
        }

        public TunnelConfig CreateConfig(string peerName, int durationHours)
        {
            using var clientDh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var clientPriv = clientDh.ExportPkcs8PrivateKey();
            var clientPub = clientDh.PublicKey.ExportSubjectPublicKeyInfo();

            var shared = _serverDh.DeriveKeyMaterial(clientDh.PublicKey);
            var id = Guid.NewGuid().ToString("N");
            _sharedSecrets[id] = shared;

            string localIp = $"10.0.0.{RandomNumberGenerator.GetInt32(2, 254)}";
            string remoteIp = $"10.0.0.{RandomNumberGenerator.GetInt32(2, 254)}";
            //int port = 50000 + RandomNumberGenerator.GetInt32(0, 1000);
            int port = LISTEN_PORT;
            var expiresAt = DateTimeOffset.UtcNow.AddHours(durationHours);

            return new TunnelConfig(
                TunnelId: id,
                PeerName: peerName,
                LocalPrivateKey: Convert.ToBase64String(clientPriv),
                LocalPublicKey: Convert.ToBase64String(clientPub),
                RemotePublicKey: Convert.ToBase64String(_serverPub),
                LocalIp: localIp,
                RemoteIp: remoteIp,
                ListenPort: port,
                ExpiresAt: expiresAt,
                RemoteHost: "127.0.0.1", // TODO: задать реальный адрес пира снаружи
                RemotePort: port
            );
        }

        public byte[] GetSharedSecret(string tunnelId)
            => _sharedSecrets.TryGetValue(tunnelId, out var s) ? s : throw new KeyNotFoundException();

        public void RemoveSecret(string tunnelId)
        {
            _sharedSecrets.TryRemove(tunnelId, out _);
            if (_sessions.TryRemove(tunnelId, out var s))
                s.Dispose();
        }

        public ITunnelCryptoSession GetSession(string tunnelId)
        {
            return _sessions.GetOrAdd(tunnelId, id =>
            {
                if (!_sharedSecrets.TryGetValue(id, out var shared))
                    throw new KeyNotFoundException($"Shared secret for {id} not found");

                // HKDF-SHA256: derive k_tx / k_rx (32 байта каждый)
                Span<byte> k_tx = stackalloc byte[32];
                Span<byte> k_rx = stackalloc byte[32];

                var infoTx = System.Text.Encoding.UTF8.GetBytes($"NetSentry|{id}|tx");
                var infoRx = System.Text.Encoding.UTF8.GetBytes($"NetSentry|{id}|rx");

                Hkdf(shared, ReadOnlySpan<byte>.Empty, infoTx, k_tx);
                Hkdf(shared, ReadOnlySpan<byte>.Empty, infoRx, k_rx);

                // 4-байтовый префикс nonce для направления "tx"
                Span<byte> prefix = stackalloc byte[4];
                RandomNumberGenerator.Fill(prefix);

                return new TunnelCryptoSession(k_tx.ToArray(), k_rx.ToArray(), prefix.ToArray());
            });
        }

        private static void Hkdf(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info, Span<byte> okm)
        {
            // Extract
            Span<byte> prk = stackalloc byte[32];
            using (var hmac = new HMACSHA256(salt.IsEmpty ? Array.Empty<byte>() : salt.ToArray()))
                prk = hmac.ComputeHash(ikm.ToArray());

            // Expand (достаточно одного блока для 32 байт)
            using var hmac2 = new HMACSHA256(prk.ToArray());
            hmac2.TransformBlock(info.ToArray(), 0, info.Length, null, 0);
            hmac2.TransformFinalBlock(new byte[] { 0x01 }, 0, 1);
            hmac2.Hash!.CopyTo(okm);
        }

        // Реализация крипто-сессии
        private sealed class TunnelCryptoSession : ITunnelCryptoSession
        {
            private readonly ChaCha20Poly1305 _aeadTx;
            private readonly ChaCha20Poly1305 _aeadRx;
            private readonly byte[] _noncePrefix; // 4 байта
            private long _counter;                // 8-байтовая часть nonce (big-endian от _counter)

            public TunnelCryptoSession(byte[] kTx, byte[] kRx, byte[] noncePrefix)
            {
                _aeadTx = new ChaCha20Poly1305(kTx);
                _aeadRx = new ChaCha20Poly1305(kRx);
                _noncePrefix = noncePrefix;
                _counter = 0;
            }

            public void Encrypt(ReadOnlySpan<byte> frame, Span<byte> nonceOut, Span<byte> cipherOut, Span<byte> tagOut)
            {
                if (nonceOut.Length != 12) throw new ArgumentException("nonceOut must be 12 bytes");

                // 12-байтовый nonce = 4 prefix + 8 counter (big-endian)
                _noncePrefix.AsSpan().CopyTo(nonceOut);
                WriteUInt64BE(nonceOut.Slice(4, 8), (ulong)Interlocked.Increment(ref _counter));

                _aeadTx.Encrypt(nonceOut, frame, cipherOut, tagOut);
            }

            public bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag, Span<byte> plainOut)
            {
                try
                {
                    _aeadRx.Decrypt(nonce, cipher, tag, plainOut);
                    return true;
                }
                catch (CryptographicException)
                {
                    return false;
                }
            }

            public void Dispose()
            {
                _aeadTx.Dispose();
                _aeadRx.Dispose();
            }

            private static void WriteUInt64BE(Span<byte> dst, ulong value)
            {
                dst[0] = (byte)(value >> 56);
                dst[1] = (byte)(value >> 48);
                dst[2] = (byte)(value >> 40);
                dst[3] = (byte)(value >> 32);
                dst[4] = (byte)(value >> 24);
                dst[5] = (byte)(value >> 16);
                dst[6] = (byte)(value >> 8);
                dst[7] = (byte)value;
            }
        }
    }
}
