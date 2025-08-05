using NetSentry.Models;

namespace NetSentry.Routing
{
    /// <summary>
    /// Прописывает правила ip_forward и NAT (masquerade) для туннеля.
    /// </summary>
    public interface IRouteManager
    {
        /// <summary>
        /// Включает IP-форвардинг и добавляет правило NAT для интерфейса config.TunnelId.
        /// </summary>
        void ApplyRouting(TunnelConfig config);

        /// <summary>
        /// Убирает правило NAT и, если нужно, отключает IP-форвардинг.
        /// </summary>
        void RemoveRouting(string tunnelId);
    }
}
