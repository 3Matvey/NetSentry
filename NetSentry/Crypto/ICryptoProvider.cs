using NetSentry.Models;

namespace NetSentry.Crypto
{
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
        TunnelConfig CreateConfig(string peerName, int durationHours);

        /// <summary>
        /// Возвращает общий секрет, сгенерированный при CreateConfig, по идентификатору туннеля.
        /// </summary>
        byte[] GetSharedSecret(string tunnelId);

        /// <summary>
        /// Удаляет сохранённый общий секрет для указанного туннеля.
        /// </summary>
        void RemoveSecret(string tunnelId);

        /// <summary>
        /// получить (или создать) крипто-сессию для туннеля
        /// </summary>
        ITunnelCryptoSession GetSession(string tunnelId);
    }
}
