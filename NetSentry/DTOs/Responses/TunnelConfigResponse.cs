using NetSentry.Models;

namespace NetSentry.DTOs.Responses
{
    public class TunnelConfigResponse
    {
        public string TunnelId { get; set; } = string.Empty;
        public string PeerName { get; set; } = string.Empty;
        public string LocalIp { get; set; } = string.Empty;
        public string RemoteIp { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }

        public static TunnelConfigResponse FromConfig(TunnelConfig config) => new TunnelConfigResponse
        {
            TunnelId = config.TunnelId,
            PeerName = config.PeerName,
            LocalIp = config.LocalIp,
            RemoteIp = config.RemoteIp,
            ExpiresAt = config.ExpiresAt.DateTime
        };
    }
}
