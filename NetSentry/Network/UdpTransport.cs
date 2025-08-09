using NetSentry.Models;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace NetSentry.Network
{
    /// <summary>
    /// Реализация асинхронного UDP-транспорта для передачи VPN-фреймов.
    /// </summary>
    public class UdpTransport : IUdpTransport, IDisposable
    {
        private readonly UdpClient _socket;

        /// <summary>
        /// Инициализация транспорта и привязка к локальному порту.
        /// </summary>
        /// <param name="listenPort">Порт для приема UDP-пакетов.</param>
        public UdpTransport(int listenPort)
        {
            _socket = new UdpClient(listenPort)
            {
                // Увеличиваем буферы для высокой пропускной способности.
                Client = { ReceiveBufferSize = 1_000_000, SendBufferSize = 1_000_000 }
            };
        }

        /// <summary>
        /// Асинхронный цикл приема UDP-пакетов.
        /// Возвращает поток кортежей (TunnelId, Payload).
        /// TunnelId здесь не извлекается, т.к. он будет получен на уровне фреймера.
        /// </summary>
        /// <param name="cancellation">Токен отмены для остановки приема.</param>
        /// <returns>Поток входящих данных для обработки выше.</returns>
        public async IAsyncEnumerable<(string TunnelId, ReadOnlyMemory<byte> Payload)>
            ReceiveAsync([EnumeratorCancellation] CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                UdpReceiveResult res;
                //  Ожидание входящего UDP-пакета.
                try { res = await _socket.ReceiveAsync(cancellation); }
                catch (OperationCanceledException) { yield break; }

                byte[] data = res.Buffer;
                // Первый байт — версия, второй — длина TunnelId, но здесь просто передаем сырой фрейм.
                yield return (TunnelId: null!, Payload: data);
            }
        }

        /// <summary>
        /// Асинхронная отправка UDP-пакета на удаленный адрес.
        /// </summary>
        /// <param name="config">Конфигурация туннеля с адресом и портом назначения.</param>
        /// <param name="frame">Готовый фрейм для отправки.</param>
        /// <returns>Задача отправки.</returns>
        public async Task SendAsync(TunnelConfig config, ReadOnlyMemory<byte> frame)
        {
            var address = IPAddress.TryParse(config.RemoteHost, out var ip)
                ? ip
                : (await Dns.GetHostAddressesAsync(config.RemoteHost))
                    .First(a => a.AddressFamily == AddressFamily.InterNetwork);

            var endpoint = new IPEndPoint(address, config.RemotePort);
            await _socket.SendAsync(frame, endpoint);
        }

        public void Dispose()
        {
            _socket.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
