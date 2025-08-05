using NetSentry.Models;
using NetSentry.ResultPattern;

namespace NetSentry.Services
{
    /// <summary>
    /// Основной сервис, которым оперируют контроллеры:
    /// создание, чтение и удаление VPN-туннелей.
    /// </summary>
    public interface ITunnelService
    {
        /// <summary>
        /// Создаёт новый туннель для указанного peerName и на заданное количество часов.
        /// </summary>
        /// <param name="peerName">Уникальное имя «peer» (для логов и конфигов).</param>
        /// <param name="durationHours">Время жизни туннеля в часах.</param>
        Task<Result<TunnelConfig>> CreateAsync(string peerName, int durationHours);

        /// <summary>
        /// Возвращает конфигурацию уже существующего туннеля.
        /// </summary>
        /// <param name="tunnelId">Идентификатор туннеля</param>
        Task<Result<TunnelConfig>> GetAsync(string tunnelId);

        /// <summary>
        /// Удаляет туннель и все связанные с ним системные ресурсы.
        /// </summary>
        /// <param name="tunnelId">Идентификатор туннеля.</param>
        Task<Result> DeleteAsync(string tunnelId);
    }
}
