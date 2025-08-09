using System.Runtime.InteropServices;

namespace NetSentry.Drivers.Windows
{
    internal class WintunStream(nint handle) : Stream
    {
        private bool _disposed;

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
            
            nint packet = WintunNative.WintunReceivePacket(handle, out uint size);
            if (packet == nint.Zero || size == 0)
                return 0;

            int bytesToCopy = Math.Min(count, (int)size);
            Marshal.Copy(packet, buffer, offset, bytesToCopy);
            return bytesToCopy;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();

            nint packet = WintunNative.WintunAllocateSendPacket(handle, (uint)count);
            if (packet == nint.Zero)
                throw new IOException("Failed to allocate packet buffer");

            Marshal.Copy(buffer, offset, packet, count);
            WintunNative.WintunSendPacket(handle, packet);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => 
            throw new NotSupportedException();
        public override void SetLength(long value) => 
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                // WintunCloseAdapter גûחûגאועסÿ ג TunAdapter
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }
}