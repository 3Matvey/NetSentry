using System.Runtime.InteropServices;

namespace NetSentry.Shared.Platform
{
    public class PlatformInfo : IPlatformInfo
    {
        public PlatformType Platform { get; }
        public PlatformInfo()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Platform = PlatformType.Linux;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Platform = PlatformType.Windows;
            else
                throw new PlatformNotSupportedException();
        }
    }
}
