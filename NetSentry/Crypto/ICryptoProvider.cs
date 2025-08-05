using NetSentry.Models;

namespace NetSentry.Crypto
{
    /// <summary>
    /// Генерирует криптографические артефакты и базовую конфигурацию туннеля.
    /// </summary>
    public interface ICryptoProvider
    {
        /// <summary>
        /// Создаёт новый TunnelConfig:
        /// - локальная приватная и публичная пара ключей,
        /// - публичный ключ удалённого сервера (peer),
        /// - виртуальный IP-адреса для local/remote,
        /// - порт (UDP) для прослушивания,
        /// - время истечения (ExpiresAt).
        /// </summary>
        /// <param name="peerName">Уникальное имя peer-а (для логов, конфигов).</param>
        /// <param name="durationHours">Сколько часов жить туннелю.</param>
        TunnelConfig CreateConfig(string peerName, int durationHours);
    }
}
