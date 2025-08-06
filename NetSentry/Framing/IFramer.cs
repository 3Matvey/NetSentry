namespace NetSentry.Framing
{
    /// <summary>
    /// Упаковывает и распаковывает IP-пакеты для передачи по UDP
    /// без лишних аллокаций.
    /// Заголовок включает версию протокола, TunnelId и длину пакета.
    /// </summary>
    public interface IFramer
    {
        /// <summary>
        /// Упаковывает raw-ip-пакет в кадр с заголовком.
        /// Заголовок: [1 байт version][1 байт idLen][idLen байт UTF8 TunnelId]
        ///           [2 байта BE длина payload]
        /// Затем payload.
        /// </summary>
        /// <param name="tunnelId">Идентификатор туннеля.</param>
        /// <param name="rawIpPacket">IP-пакет из TUN.</param>
        /// <param name="destination">Буфер для кадра.</param>
        /// <returns>Длина записанного кадра.</returns>
        int Frame(string tunnelId, ReadOnlySpan<byte> rawIpPacket, Span<byte> destination);

        /// <summary>
        /// Разбирает кадр, извлекая TunnelId и payload.
        /// </summary>
        /// <param name="frame">Данные кадра (после дешифровки).</param>
        /// <param name="tunnelId">Выходной TunnelId.</param>
        /// <param name="destination">Буфер для payload (IP-пакета).</param>
        /// <returns>Длина IP-пакета.</returns>
        int Deframe(ReadOnlySpan<byte> frame, out string tunnelId, Span<byte> destination);
    }
}
