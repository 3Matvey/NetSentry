using System.Text;

namespace NetSentry.Framing
{
    /// <summary>
    /// Класс AdvancedFramer отвечает за упаковку (Frame) и распаковку (Deframe)
    /// IP-пакетов для передачи через VPN-туннель с добавлением метаданных:
    /// версии протокола, идентификатора туннеля и длины полезной нагрузки.
    /// </summary>
    public class AdvancedFramer : IFramer
    {
        // Текущая версия протокола фрейминга
        private const byte Version = 1;

        /// <summary>
        /// Формирует кадр из сырого IP-пакета, добавляя служебную информацию:
        ///   1 байт - Version (версия протокола)
        ///   1 байт - IdLen (длина идентификатора туннеля)
        ///   N байт  - TunnelId (UTF-8 представление идентификатора туннеля)
        ///   2 байта - Payload Length (длина IP-пакета, big-endian)
        ///   M байт  - IP Packet (собственно полезная нагрузка)
        /// </summary>
        /// <param name="tunnelId">Уникальный идентификатор VPN-туннеля</param>
        /// <param name="rawIpPacket">Сырые данные IP-пакета от TUN-интерфейса</param>
        /// <param name="destination">Буфер, куда будет записан сформированный кадр</param>
        /// <returns>Общая длина записанного кадра в байтах</returns>
        public int Frame(string tunnelId, ReadOnlySpan<byte> rawIpPacket, Span<byte> destination)
        {
            // идентификатор туннеля в UTF-8 байты
            byte[] idBytes = Encoding.UTF8.GetBytes(tunnelId);
            if (idBytes.Length == 0 || idBytes.Length > byte.MaxValue)
                throw new ArgumentException("TunnelId length must be 1..255 bytes");

            // длина заголовка: версия + длина id    + сам id      + 2 байта под длину полезной нагрузки
            int headerLen =        1   +   1     +  idBytes.Length + 2;

            // длина кадра = заголовок + размер IP-пакета
            int totalLen = headerLen + rawIpPacket.Length;

            if (destination.Length < totalLen)
                throw new ArgumentException("Destination buffer too small");

            int pos = 0;
            destination[pos++] = Version; // 1. Записываем версию протокола

            destination[pos++] = (byte)idBytes.Length; // 2. Записываем длину идентификатора туннеля

            idBytes.CopyTo(destination.Slice(pos, idBytes.Length)); // 3. Копируем сам идентификатор туннеля
            pos += idBytes.Length;

            // 4. Запись длины полезной нагрузки (IP-пакета) в big-endian порядке
            ushort payloadLen = (ushort)rawIpPacket.Length;
            destination[pos++] = (byte)(payloadLen >> 8);        // старший байт. Сдвиг, после приведение, которое берет нижние 8 бит 
            destination[pos++] = (byte)(payloadLen & 0xFF);      // младший байт. Сравнение с маской оставляет младшие 8 бит

            // 5. Копируем IP-пакет сразу после заголовка
            rawIpPacket.CopyTo(destination.Slice(pos, rawIpPacket.Length));
            pos += rawIpPacket.Length;

            // Возвращаем фактически записанное число байт
            return pos;
        }

        /// <summary>
        /// Разбирает принятый кадр, извлекая из него:
        ///   - проверяет версию протокола
        ///   - считывает идентификатор туннеля
        ///   - получает длину и содержимое IP-пакета
        /// </summary>
        /// <param name="frame">Принятый кадр байт</param>
        /// <param name="tunnelId">Выходной параметр: извлеченный идентификатор туннеля</param>
        /// <param name="destination">Буфер для записи извлеченного IP-пакета</param>
        /// <returns>Длина извлеченного IP-пакета</returns>
        public int Deframe(ReadOnlySpan<byte> frame, out string tunnelId, Span<byte> destination)
        {
            // Минимальный размер кадра: 1 байт версия + 1 байт длина id + 2 байта длина полезной нагрузки
            if (frame.Length < 4)
                throw new ArgumentException("Frame too small");

            int pos = 0;

            byte ver = frame[pos++];
            if (ver != Version)
                throw new InvalidOperationException($"Unsupported framer version {ver}");

            int idLen = frame[pos++]; // длина идентификатора туннеля

            //                достаточность данных в кадре
            if (idLen <= 0 || frame.Length < 2 + idLen + 2)
                throw new ArgumentException("Invalid TunnelId length");

            // Извлекаем идентификатор туннеля из UTF-8 байтов
            tunnelId = Encoding.UTF8.GetString(frame.Slice(pos, idLen));
            pos += idLen;

            // чтение длины полезной нагрузки (big-endian)
            //                старший байт           
            int payloadLen = (frame[pos++] << 8) | frame[pos++];

            // в кадре достаточно байт для полезной нагрузки
            if (frame.Length < pos + payloadLen)
                throw new ArgumentException("Frame payload length mismatch");

            // буфер назначения достаточен для IP-пакета
            if (destination.Length < payloadLen)
                throw new ArgumentException("Destination buffer too small");

            // Копируем IP-пакет в буфер назначения
            frame.Slice(pos, payloadLen).CopyTo(destination);

            return payloadLen;
        }
    }
}
