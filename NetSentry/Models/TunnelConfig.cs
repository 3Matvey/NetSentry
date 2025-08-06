namespace NetSentry.Models
{
    public record TunnelConfig(
        string TunnelId,
        string PeerName,
        string LocalPrivateKey,   // клиентская приватка (Base64 PKCS8)
        string LocalPublicKey,    // клиентский паблик (Base64 SPKI)
        string RemotePublicKey,   // серверный паблик (Base64 SPKI)
        string LocalIp,
        string RemoteIp,
        int ListenPort,
        DateTimeOffset ExpiresAt
    );
}
