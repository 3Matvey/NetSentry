using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace NetSentry.Drivers
{
    /// <summary>
    /// P/Invoke declarations for Linux TUN interface via source-generated marshalling.
    /// </summary>
    internal static partial class LinuxNative
    {
        private const string LibC = "libc";

        // Флаги открытия файла
        public const int O_RDWR = 2;
    
        // IOCTL команды
        public const uint TUNSETIFF = 0x400454ca;
    
        // Флаги настройки интерфейса
        public const short IFF_TUN = 0x0001;
        public const short IFF_NO_PI = 0x1000;

        [LibraryImport(LibC, SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int open(string pathname, int flags);

        [LibraryImport(LibC, SetLastError = true)]
        internal static partial int ioctl(int fd, uint request, ref ifreq_native ifr);

        [LibraryImport(LibC, SetLastError = true)]
        internal static partial int close(int fd);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal struct ifreq
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string ifr_name;
            public short ifr_flags;

            public static implicit operator ifreq_native(ifreq src)
            {
                var native = new ifreq_native();
                unsafe
                {
                    for (int i = 0; i < 16; i++)
                        native.ifr_name[i] = 0;
                    var nameBytes = System.Text.Encoding.ASCII.GetBytes(src.ifr_name ?? "");
                    for (int i = 0; i < nameBytes.Length && i < 16; i++)
                        native.ifr_name[i] = nameBytes[i];
                }
                native.ifr_flags = src.ifr_flags;
                return native;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        internal unsafe struct ifreq_native
        {
            public fixed byte ifr_name[16];
            public short ifr_flags;
        }
    }
}
