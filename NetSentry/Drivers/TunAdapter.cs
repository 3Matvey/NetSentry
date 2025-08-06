using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using NetSentry.Models;

/// <summary>
/// Поднимает и удаляет TUN-интерфейс под Windows или Linux.
/// Выбор реализации через условную компиляцию: WINDOWS или LINUX.
/// </summary>
namespace NetSentry.Drivers
{
    /// <summary>
    /// Поднимает и удаляет TUN-интерфейс под Windows или Linux.
    /// </summary>
    public class TunAdapter : ITunAdapter
    {
#if WINDOWS
        public void CreateInterface(TunnelConfig config)
        {
            var adapterHandle = WintunNative.WintunCreateAdapter(config.TunnelId, "NetSentry", 0);
            if (adapterHandle == IntPtr.Zero)
                throw new InvalidOperationException("WintunCreateAdapter failed");

            RunProcess("netsh", $"interface ip set address \"{config.TunnelId}\" static {config.LocalIp} 255.255.255.0");
            RunProcess("netsh", $"interface set interface \"{config.TunnelId}\" enable");
        }

        public void RemoveInterface(string tunnelId)
        {
            RunProcess("netsh", $"interface set interface \"{tunnelId}\" disable");
            RunProcess("netsh", $"interface delete interface \"{tunnelId}\"");
        }
#elif LINUX
        private const int O_RDWR     = 2;
        private const uint TUNSETIFF = 0x400454ca;
        private const short IFF_TUN  = 0x0001;
        private const short IFF_NO_PI= 0x1000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct ifreq
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string ifr_name;
            public short ifr_flags;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int open(string pathname, int flags);
        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int fd, uint request, ref ifreq ifr);
        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        public void CreateInterface(TunnelConfig config)
        {
            int fd = open("/dev/net/tun", O_RDWR);
            if (fd < 0) throw new InvalidOperationException("Cannot open /dev/net/tun");

            var name = config.TunnelId.Length > 15 ? config.TunnelId.Substring(0, 15) : config.TunnelId;
            var ifr  = new ifreq { ifr_name = name, ifr_flags = (short)(IFF_TUN | IFF_NO_PI) };
            if (ioctl(fd, TUNSETIFF, ref ifr) < 0)
                throw new InvalidOperationException("ioctl TUNSETIFF failed");
            close(fd);

            RunProcess("ip",   $"addr add {config.LocalIp}/24 dev {ifr.ifr_name}");
            RunProcess("ip",   $"link set dev {ifr.ifr_name} up");
        }

        public void RemoveInterface(string tunnelId)
        {
            var name = tunnelId.Length > 15 ? tunnelId.Substring(0, 15) : tunnelId;
            RunProcess("ip", $"link set dev {name} down");
            RunProcess("ip", $"link delete {name}");
        }
#else
        public void CreateInterface(TunnelConfig config)
            => throw new PlatformNotSupportedException();

        public void RemoveInterface(string tunnelId)
            => throw new PlatformNotSupportedException();
#endif

        private static void RunProcess(string cmd, string args)
        {
            var psi = new ProcessStartInfo(cmd, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException($"Не удалось запустить процесс: {cmd} {args}");
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException($"{cmd} {args} failed: {err}");
            }
        }
    }
}
