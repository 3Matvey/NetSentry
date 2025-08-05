using NetSentry.Models;

namespace NetSentry.Network
{
    /// <summary>
    /// Асинхронный UDP-транспорт, но с использованием Memory для минимизации копий.
    /// </summary>
    public interface IUdpTransport
    {
        /// <summary>
        /// Цикл приёма UDP-пакетов.
        /// </summary>
        /// <param name="cancellation">Токен отмены.</param>
        /// <returns>
        /// Поток кортежей (tunnelId, payload), 
        /// </returns>
        IAsyncEnumerable<(string TunnelId, ReadOnlyMemory<byte> Payload)> ReceiveAsync(
            CancellationToken cancellation);

        /// <summary>
        /// Отправляет готовый фрейм (header + зашифрованный payload).
        /// </summary>
        /// <param name="config">Конфигурация туннеля с адресом/портом.</param>
        /// <param name="framedEncrypted">ReadOnlyMemory с данными.</param>
        Task SendAsync(TunnelConfig config, ReadOnlyMemory<byte> framedEncrypted);
    }
}
