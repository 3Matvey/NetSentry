namespace NetSentry.DTOs.Requests
{
    public class CreateTunnelRequest
    {
        public string PeerName { get; set; } = string.Empty;
        public int DurationHours { get; set; }
    }
}
