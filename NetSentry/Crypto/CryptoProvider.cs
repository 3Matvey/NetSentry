// Crypto/CryptoProvider.cs
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using NetSentry.Models;

namespace NetSentry.Crypto
{
    public class CryptoProvider : ICryptoProvider
    {
        private readonly ECDiffieHellman _serverDh;
        private readonly byte[] _serverPub;      // Base64-bytes of SPKI
        private readonly ConcurrentDictionary<string, byte[]> _sharedSecrets = [];

        public CryptoProvider()
        {
            _serverDh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            _serverPub = _serverDh.PublicKey.ExportSubjectPublicKeyInfo();
        }

        public TunnelConfig CreateConfig(string peerName, int durationHours)
        {
            // 1) клиентский Ephemeral ECDH
            using var clientDh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var clientPriv = clientDh.ExportPkcs8PrivateKey();
            var clientPub = clientDh.PublicKey.ExportSubjectPublicKeyInfo();

            // 2) общий секрет: DeriveKeyMaterial(паблика клиента)
            var shared = _serverDh.DeriveKeyMaterial(clientDh.PublicKey);
            // запоминаем, чтобы потом брать его для шифрования/дешифра
            var id = Guid.NewGuid().ToString("N");
            _sharedSecrets[id] = shared;

            // 3) сеть и порт
            string localIp = $"10.0.0.{RandomNumberGenerator.GetInt32(2, 254)}";
            string remoteIp = $"10.0.0.{RandomNumberGenerator.GetInt32(2, 254)}";
            int port = 50000 + RandomNumberGenerator.GetInt32(0, 1000);

            // 4) Время истечения
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
                ExpiresAt: expiresAt
            );
        }

        public byte[] GetSharedSecret(string tunnelId)
            => _sharedSecrets.TryGetValue(tunnelId, out var s) ? s : throw new KeyNotFoundException();

        public void RemoveSecret(string tunnelId)
            => _sharedSecrets.TryRemove(tunnelId, out _);
    }
}
