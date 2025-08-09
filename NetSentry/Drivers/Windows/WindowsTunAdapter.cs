using NetSentry.Models;
using System.Collections.Concurrent;
using System.Threading;
using static NetSentry.Drivers.Windows.WintunNative;

namespace NetSentry.Drivers.Windows
{
    public class WindowsTunAdapter : TunAdapter
    {
        private readonly ConcurrentDictionary<string, IntPtr> _adapterHandles = new();

        public override void CreateInterface(TunnelConfig config)
        {
            ThrowIfConfigIsNull(config);

            var handle = WintunCreateAdapter(config.TunnelId, "NetSentry", 0);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("WintunCreateAdapter failed");

            _adapterHandles[config.TunnelId] = handle;

            try
            {
                // ждём появления адаптера (до ~10 секунд)
                string alias = "";
                for (int i = 0; i < 20 && string.IsNullOrEmpty(alias); i++)
                {
                    // 1) пробуем по Name = TunnelId
                    var byName = ReadProcessOutput("powershell",
                        $"-NoProfile -ExecutionPolicy Bypass -Command \"(Get-NetAdapter -Name '{config.TunnelId}' -ErrorAction SilentlyContinue).Name\"");
                    if (!string.IsNullOrWhiteSpace(byName))
                    {
                        alias = byName.Trim();
                        break;
                    }

                    // 2) пробуем по InterfaceDescription (берём самый свежий Wintun)
                    var byDesc = ReadProcessOutput("powershell",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetAdapter | Where-Object {$_.InterfaceDescription -like 'Wintun Userspace Tunnel*'} | Sort-Object ifIndex -Descending | Select-Object -First 1 -ExpandProperty Name\"");
                    if (!string.IsNullOrWhiteSpace(byDesc))
                    {
                        alias = byDesc.Trim();
                        break;
                    }

                    Thread.Sleep(500);
                }

                if (string.IsNullOrWhiteSpace(alias))
                {
                    // для дебага выведем список адаптеров в исключение
                    var dump = ReadProcessOutput("powershell",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetAdapter | Select-Object Name, InterfaceDescription, Status, ifIndex | Format-Table -AutoSize | Out-String\"");
                    throw new InvalidOperationException("Не удалось найти созданный Wintun-адаптер в системе.\n" + dump);
                }

                // включаем адаптер
                RunProcess("powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"Enable-NetAdapter -Name '{alias}' -Confirm:$false -ErrorAction Stop\"");

                // чистим возможные старые IPv4
                RunProcess("powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetIPAddress -InterfaceAlias '{alias}' -AddressFamily IPv4 -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false\"");

                // назначаем IPv4
                var prefixLen = 24;
                RunProcess("powershell",
                    $"-NoProfile -ExecutionPolicy Bypass -Command \"New-NetIPAddress -InterfaceAlias '{alias}' -IPAddress {config.LocalIp} -PrefixLength {prefixLen} -AddressFamily IPv4 -ErrorAction Stop\"");
            }
            catch
            {
                WintunCloseAdapter(handle);
                _adapterHandles.TryRemove(config.TunnelId, out _);
                throw;
            }

        }

        public override void RemoveInterface(string tunnelId)
        {
            if (_adapterHandles.TryRemove(tunnelId, out var handle))
            {
                WintunCloseAdapter(handle);
                // Опционально: можно найти alias и снести через Disable-NetAdapter/Remove-NetAdapter
            }
        }

        public override Stream OpenTunStream(TunnelConfig config)
        {
            ThrowIfConfigIsNull(config);

            if (!_adapterHandles.TryGetValue(config.TunnelId, out var handle))
                throw new InvalidOperationException("Adapter handle not found");
            return new WintunStream(handle);
        }

        public override void Dispose()
        {
            if (_disposed) return;

            foreach (var handle in _adapterHandles.Values)
                WintunCloseAdapter(handle);
            _adapterHandles.Clear();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private static string FindWintunAlias()
        {
            try
            {
                var alias = ReadProcessOutput("powershell",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Get-NetAdapter | Where-Object {$_.InterfaceDescription -like 'Wintun Userspace Tunnel*'} | Sort-Object -Property ifIndex -Descending | Select-Object -First 1 -ExpandProperty Name\"");
                return alias.Trim();
            }
            catch
            {
                return "";
            }
        }

        private static string ReadProcessOutput(string cmd, string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(cmd, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException($"Не удалось запустить процесс: {cmd} {args}");
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"{cmd} {args} failed: {p.StandardError.ReadToEnd()}");
            return stdout;
        }
    }
}
