//using System;
//using System.Collections.Concurrent;
//using System.Diagnostics;
//using System.IO;
//using System.Runtime.InteropServices;
//using Microsoft.Win32.SafeHandles;
//using NetSentry.Models;
//using static NetSentry.Drivers.LinuxNative;
//using static NetSentry.Drivers.WintunNative;

//namespace NetSentry.Drivers
//{
//    /// <summary>
//    /// Поднимает и удаляет виртуальные сетевые интерфейсы для VPN-туннеля.
//    /// Выбор реализации происходит в рантайме в зависимости от ОС.
//    /// </summary>
//    public class TunAdapter1 : ITunAdapter, IDisposable
//    {
//        private readonly ConcurrentDictionary<string, IntPtr> _adapterHandles = new();
//        private bool _disposed;

//        ///// <summary>
//        ///// Общий метод запуска процесса с выбросом исключения при ошибке.
//        ///// </summary>
//        //private static void RunProcess(string cmd, string args)
//        //{
//        //    var psi = new ProcessStartInfo(cmd, args)
//        //    {
//        //        RedirectStandardError = true,
//        //        UseShellExecute = false,
//        //        CreateNoWindow = true
//        //    };
//        //    using var proc = Process.Start(psi)
//        //        ?? throw new InvalidOperationException($"Не удалось запустить процесс: {cmd} {args}");
//        //    proc.WaitForExit();
//        //    if (proc.ExitCode != 0)
//        //    {
//        //        var err = proc.StandardError.ReadToEnd();
//        //        throw new InvalidOperationException($"{cmd} {args} failed: {err}");
//        //    }
//        //}

//        //private static void ThrowIfConfigIsNull(TunnelConfig config)
//        //{
//        //    ArgumentNullException.ThrowIfNull(config);
//        //    ArgumentException.ThrowIfNullOrEmpty(config.TunnelId);
//        //    ArgumentException.ThrowIfNullOrEmpty(config.LocalIp);
//        //}

//        public void CreateInterface(TunnelConfig config)
//        {
//            ThrowIfConfigIsNull(config);

//            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//            {
//                CreateInterfaceWindows(config);
//            }
//            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
//            {
//                CreateInterfaceLinux(config);
//            }
//            else
//            {
//                throw new PlatformNotSupportedException();
//            }
//        }

//        public void RemoveInterface(string tunnelId)
//        {
//            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//            {
//                RemoveInterfaceWindows(tunnelId);
//            }
//            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
//            {
//                RemoveInterfaceLinux(tunnelId);
//            }
//            else
//            {
//                throw new PlatformNotSupportedException();
//            }
//        }

//        public Stream OpenTunStream(TunnelConfig config)
//        {
//            ThrowIfConfigIsNull(config);

//            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//            {
//                if (!_adapterHandles.TryGetValue(config.TunnelId, out var handle))
//                    throw new InvalidOperationException("Adapter handle not found");
//                return new WintunStream(handle);
//            }
//            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
//            {
//                int fd = open("/dev/net/tun", O_RDWR);
//                if (fd < 0)
//                    throw new InvalidOperationException("Cannot open /dev/net/tun");
//                try
//                {
//                    string name = config.TunnelId.Length > 15 ? config.TunnelId[..15] : config.TunnelId;
//                    ifreq_native nativeIfr = new ifreq { ifr_name = name, ifr_flags = IFF_TUN | IFF_NO_PI };
//                    if (ioctl(fd, TUNSETIFF, ref nativeIfr) < 0)
//                        throw new InvalidOperationException("ioctl TUNSETIFF failed");

//                    return new FileStream(new SafeFileHandle((IntPtr)fd, ownsHandle: true), FileAccess.ReadWrite, 1500, isAsync: true);
//                }
//                catch
//                {
//                    close(fd);
//                    throw;
//                }
//            }
//            else
//            {
//                throw new PlatformNotSupportedException();
//            }
//        }

//        private void CreateInterfaceWindows(TunnelConfig config)
//        {
//            var handle = WintunCreateAdapter(config.TunnelId, "NetSentry", 0);
//            if (handle == IntPtr.Zero)
//                throw new InvalidOperationException("WintunCreateAdapter failed");

//            _adapterHandles[config.TunnelId] = handle;
//            try
//            {
//                RunProcess("netsh", $"interface ip set address \"{config.TunnelId}\" static {config.LocalIp} 255.255.255.0");
//                RunProcess("netsh", $"interface set interface \"{config.TunnelId}\" enable");
//            }
//            catch
//            {
//                WintunCloseAdapter(handle);
//                _adapterHandles.TryRemove(config.TunnelId, out _);
//                throw;
//            }
//        }

//        private void RemoveInterfaceWindows(string tunnelId)
//        {
//            if (_adapterHandles.TryRemove(tunnelId, out var handle))
//            {
//                WintunCloseAdapter(handle);
//                RunProcess("netsh", $"interface set interface \"{tunnelId}\" disable");
//                RunProcess("netsh", $"interface delete interface \"{tunnelId}\"");
//            }
//        }

//        private static void CreateInterfaceLinux(TunnelConfig config)
//        {
//            int fd = -1;
//            try
//            {
//                fd = open("/dev/net/tun", O_RDWR);
//                if (fd < 0)
//                    throw new InvalidOperationException("Cannot open /dev/net/tun");

//                string name = config.TunnelId.Length > 15 ? config.TunnelId[..15] : config.TunnelId;
//                ifreq_native nativeIfr = new ifreq { ifr_name = name, ifr_flags = (IFF_TUN | IFF_NO_PI) };
//                if (ioctl(fd, TUNSETIFF, ref nativeIfr) < 0)
//                    throw new InvalidOperationException("ioctl TUNSETIFF failed");

//                RunProcess("ip", $"addr add {config.LocalIp}/24 dev {name}");
//                RunProcess("ip", $"link set dev {name} up");
//            }
//            finally
//            {
//                if (fd >= 0)
//                    close(fd);
//            }
//        }

//        private static void RemoveInterfaceLinux(string tunnelId)
//        {
//            string name = tunnelId.Length > 15 ? tunnelId[..15] : tunnelId;
//            RunProcess("ip", $"link set dev {name} down");
//            RunProcess("ip", $"link delete {name}");
//        }

//        ~TunAdapter() => Dispose();

//        public void Dispose()
//        {
//            if (_disposed) return;

//            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
//            {
//                foreach (var handle in _adapterHandles.Values)
//                    WintunCloseAdapter(handle);
//                _adapterHandles.Clear();
//            }

//            _disposed = true;
//            GC.SuppressFinalize(this);
//        }
//    }
//}
