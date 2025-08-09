using Microsoft.Win32.SafeHandles;
using NetSentry.Drivers;
using NetSentry.Models;
using static NetSentry.Drivers.Linux.LinuxNative;

public class LinuxTunAdapter : TunAdapter
{
    private static string IfName(string id) => id.Length > 15 ? id[..15] : id;

    public override void CreateInterface(TunnelConfig config)
    {
        ThrowIfConfigIsNull(config);

        int fd = -1;
        try
        {
            fd = open("/dev/net/tun", O_RDWR);
            if (fd < 0) throw new InvalidOperationException("Cannot open /dev/net/tun");

            string name = IfName(config.TunnelId);
            ifreq_native nativeIfr = new ifreq { ifr_name = name, ifr_flags = (IFF_TUN | IFF_NO_PI) };
            if (ioctl(fd, TUNSETIFF, ref nativeIfr) < 0)
                throw new InvalidOperationException("ioctl TUNSETIFF failed");

            RunProcess("ip", $"addr add {config.LocalIp}/24 dev {name}");
            RunProcess("ip", $"link set dev {name} up");
        }
        finally
        {
            if (fd >= 0) close(fd);
        }
    }

    public override Stream OpenTunStream(TunnelConfig config)
    {
        ThrowIfConfigIsNull(config);

        int fd = open("/dev/net/tun", O_RDWR);
        if (fd < 0) throw new InvalidOperationException("Cannot open /dev/net/tun");
        try
        {
            string name = IfName(config.TunnelId);
            ifreq_native nativeIfr = new ifreq { ifr_name = name, ifr_flags = IFF_TUN | IFF_NO_PI };
            if (ioctl(fd, TUNSETIFF, ref nativeIfr) < 0)
                throw new InvalidOperationException("ioctl TUNSETIFF failed");

            return new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.ReadWrite, 1500, isAsync: true);
        }
        catch
        {
            close(fd);
            throw;
        }
    }

    public override void RemoveInterface(string tunnelId)
    {
        string name = IfName(tunnelId);
        RunProcess("ip", $"link set dev {name} down");
        RunProcess("ip", $"link delete {name}");
    }
}
