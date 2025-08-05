namespace NetSentry.Framing
{
    /// <summary>
    /// Упаковывает и распаковывает IP-пакеты для передачи по UDP
    /// без лишних аллокаций.
    /// </summary>
    public interface IFramer
    {
        /// <summary>
        /// Фреймит «сырые» IP-пакеты из TUN.
        /// Пишет header+payload в <paramref name="destination"/> и возвращает длину результата.
        /// </summary>
        /// <param name="rawIpPacket">IP-пакет из TUN (ReadOnlySpan).</param>
        /// <param name="destination">Span, куда пишем фрейм.</param>
        /// <returns>Количество записанных байт (<= destination.Length).</returns>
        int Frame(ReadOnlySpan<byte> rawIpPacket, Span<byte> destination);

        /// <summary>
        /// Убирает обёртку и вытаскивает чистый IP-пакет.
        /// Пишет payload в <paramref name="destination"/> и возвращает длину.
        /// </summary>
        /// <param name="payload">Данные после дешифрования (ReadOnlySpan).</param>
        /// <param name="destination">Span, куда пишем IP-пакет.</param>
        /// <returns>Количество байт IP-пакета.</returns>
        int Deframe(ReadOnlySpan<byte> payload, Span<byte> destination);
    }
}
