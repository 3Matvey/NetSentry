using System.Runtime.InteropServices;

namespace NetSentry.Drivers
{
    /// <summary>
    /// P/Invoke declarations for wintun.dll via LibraryImport (source-generated).
    /// </summary>
    internal static partial class WintunNative
    {
        private const string DllName = "wintun.dll";

        [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial IntPtr WintunCreateAdapter(string name, string tunnelType, uint preferredGuid);

        [LibraryImport(DllName)]
        internal static partial IntPtr WintunAllocateSendPacket(IntPtr handle, uint capacity);

        [LibraryImport(DllName)]
        internal static partial void WintunSendPacket(IntPtr handle, IntPtr packet);

        [LibraryImport(DllName)]
        internal static partial IntPtr WintunReceivePacket(IntPtr handle, out uint packetSize);

        [LibraryImport(DllName)]
        internal static partial void WintunCloseAdapter(IntPtr handle);
    }
}