using NetSentry.Models;

namespace NetSentry.Drivers
{
    /// <summary>
    /// Управление виртуальным сетевым интерфейсом для VPN-туннеля.
    /// </summary>
    public interface ITunAdapter
    {
        /// <summary>
        /// Поднимает на хосте интерфейс с именем config.TunnelId,
        /// назначает ему IP, MTU и т.п.
        /// </summary>
        void CreateInterface(TunnelConfig config);

        /// <summary>
        /// Удаляет ранее созданный интерфейс по config.TunnelId.
        /// </summary>
        void RemoveInterface(string tunnelId);

        /// <summary>
        /// Открывает Stream для обмена «сырыми» IP-пакетами через TUN-интерфейс.
        /// </summary>
        Stream OpenTunStream(TunnelConfig config);
    }
}
