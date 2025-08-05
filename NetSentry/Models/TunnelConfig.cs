namespace NetSentry.Models
{
    public record TunnelConfig(
        string TunnelId,
        string PeerName,
        string LocalPrivateKey,
        string RemotePublicKey,
        string LocalIp,
        string RemoteIp,
        int ListenPort,
        DateTimeOffset ExpiresAt
    );
}
