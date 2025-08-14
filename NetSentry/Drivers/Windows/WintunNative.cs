using System.Runtime.InteropServices;
using System.Text;

namespace NetSentry.Drivers.Windows
{
    internal static partial class WintunNative
    {
        private const string DllName = "wintun.dll";

        // Адаптер
        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial IntPtr WintunCreateAdapter(string name, string tunnelType, IntPtr requestedGuid); // передавайте IntPtr.Zero

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial IntPtr WintunOpenAdapter(string name);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial void WintunCloseAdapter(IntPtr adapter);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial void WintunDeleteAdapter(IntPtr adapter);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial void WintunGetAdapterLuid(IntPtr adapter, out NET_LUID luid);

        // Сессия
        [LibraryImport(DllName, SetLastError = true)]
        internal static partial IntPtr WintunStartSession(IntPtr adapter, uint capacity);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial void WintunEndSession(IntPtr session);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial IntPtr WintunGetReadWaitEvent(IntPtr session);

        // Пакеты (именно session!)
        [LibraryImport(DllName, SetLastError = true)]
        internal static partial IntPtr WintunAllocateSendPacket(IntPtr session, uint size);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial void WintunSendPacket(IntPtr session, IntPtr packet);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial IntPtr WintunReceivePacket(IntPtr session, out uint size);

        [LibraryImport(DllName, SetLastError = true)]
        internal static partial void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NET_LUID { public ulong Value; }

    internal static partial class IpHlpApi
    {
        [LibraryImport("iphlpapi.dll", SetLastError = true)]
        internal static partial int ConvertInterfaceLuidToIndex(ref NET_LUID luid, out int ifIndex);

        [LibraryImport("iphlpapi.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial int ConvertInterfaceLuidToNameW(ref NET_LUID luid, string name, int nameLen);
    }
}
