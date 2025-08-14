using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NetSentry.Drivers.Windows
{
    internal class WintunStream(nint handle) : Stream
    {
        private readonly nint _session = handle;
        private readonly nint _readEvent = WintunNative.WintunGetReadWaitEvent(handle);

        private bool _disposed;
        private byte[]? _rxBuf;
        private int _rxPos;

        // ожидание событи€ чтени€
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);
        private const uint WAIT_OBJECT_0 = 0;
        private const uint WAIT_TIMEOUT = 0x00000102;

        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(buffer);
            if ((uint)offset > buffer.Length || (uint)count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException();

            // 1) сначала отдаЄм хвост предыдущего пакета
            if (_rxBuf is not null && _rxPos < _rxBuf.Length)
            {
                int n = Math.Min(count, _rxBuf.Length - _rxPos);
                Buffer.BlockCopy(_rxBuf, _rxPos, buffer, offset, n);
                _rxPos += n;
                if (_rxPos >= _rxBuf.Length) { _rxBuf = null; _rxPos = 0; }
                return n;
            }

            // 2) пробуем получить новый пакет
            while (true)
            {
                nint pkt = WintunNative.WintunReceivePacket(_session, out uint size);
                if (pkt != nint.Zero && size > 0)
                {
                    try
                    {
                        int len = checked((int)size);
                        var tmp = new byte[len];
                        Marshal.Copy(pkt, tmp, 0, len);
                        _rxBuf = tmp;
                        _rxPos = 0;
                    }
                    finally
                    {
                        // освободить пакет ќЅя«ј“≈Ћ№Ќќ
                        WintunNative.WintunReleaseReceivePacket(_session, pkt);
                    }

                    // отдаЄм сколько попросили, остальное буферим
                    int n = Math.Min(count, _rxBuf!.Length);
                    Buffer.BlockCopy(_rxBuf, 0, buffer, offset, n);
                    _rxPos = n;
                    if (_rxPos >= _rxBuf.Length) { _rxBuf = null; _rxPos = 0; }
                    return n;
                }

                // 3) если пакета нет Ч немного подождЄм событие чтени€ и вернЄм 0 (не блокируемс€ навечно)
                if (_readEvent != nint.Zero)
                {
                    uint wr = WaitForSingleObject(_readEvent, 2000);
                    if (wr == WAIT_OBJECT_0) continue;   // по€вилс€ пакет Ч пробуем снова
                    if (wr == WAIT_TIMEOUT) return 0;     // нет данных Ч не блокируемс€ бесконечно
                    throw new IOException("WaitForSingleObject(WintunReadEvent) failed");
                }

                // если событи€ нет Ч просто non-blocking поведение
                return 0;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(buffer);
            if ((uint)offset > buffer.Length || (uint)count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException();
            if (count == 0) return;

            nint pkt = WintunNative.WintunAllocateSendPacket(_session, (uint)count);
            if (pkt == nint.Zero)
                throw new IOException("WintunAllocateSendPacket failed");

            Marshal.Copy(buffer, offset, pkt, count);
            WintunNative.WintunSendPacket(_session, pkt);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                // закрываем —≈——»ё (адаптер закрываетс€ в другом месте)
                WintunNative.WintunEndSession(_session);
                _rxBuf = null;
                _rxPos = 0;
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
