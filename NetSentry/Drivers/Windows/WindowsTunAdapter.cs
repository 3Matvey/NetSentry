using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using NetSentry.Models;
using static NetSentry.Drivers.Windows.WintunNative;
using static NetSentry.Drivers.Windows.IpHlpApi;

namespace NetSentry.Drivers.Windows
{
    public class WindowsTunAdapter : TunAdapter
    {
        private readonly ConcurrentDictionary<string, AdapterState> _states = new();
        //private volatile bool _disposed;

        private const uint DefaultRingCapacity = 4 * 1024 * 1024; // 4MB
        private const int IfIndexWaitTimeoutMs = 15_000;
        private const int PrefixLength = 24; // при необходимости поменяй на своё поле из config

        public override void CreateInterface(TunnelConfig config)
        {
            ThrowIfConfigIsNull(config);

            // 1) Пытаемся открыть существующий адаптер
            nint adapter = WintunOpenAdapter(config.TunnelId);
            if (adapter == nint.Zero)
            {
                // 2) Создаём новый
                adapter = WintunCreateAdapter(config.TunnelId, "NetSentry", IntPtr.Zero);
                if (adapter == nint.Zero)
                    ThrowLastWin32("WintunCreateAdapter failed");
            }

            try
            {
                // 3) Получаем LUID и ждём появления ifIndex
                WintunGetAdapterLuid(adapter, out var luid);
                int ifIndex = WaitForIfIndex(luid, IfIndexWaitTimeoutMs);
                if (ifIndex == 0)
                    throw new InvalidOperationException("Wintun интерфейс не появился в системе (ifIndex == 0).");

                // 4) Включаем и назначаем IPv4 по InterfaceIndex (без alias/локалей)
                RunPs($@"
                    Enable-NetAdapter -InterfaceIndex {ifIndex} -Confirm:$false -ErrorAction Stop;
                    Get-NetIPAddress -InterfaceIndex {ifIndex} -AddressFamily IPv4 -ErrorAction SilentlyContinue | Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue;
                    New-NetIPAddress -InterfaceIndex {ifIndex} -IPAddress {config.LocalIp} -PrefixLength {PrefixLength} -AddressFamily IPv4 -ErrorAction Stop;
                ");

                _states[config.TunnelId] = new AdapterState(adapter, luid, ifIndex);
            }
            catch
            {
                WintunCloseAdapter(adapter);
                _states.TryRemove(config.TunnelId, out _);
                throw;
            }
        }

        public override void RemoveInterface(string tunnelId)
        {
            if (string.IsNullOrWhiteSpace(tunnelId)) return;

            if (_states.TryRemove(tunnelId, out var st))
            {
                // Сами сессии закрываются в WintunStream.Dispose().
                if (st.Adapter != nint.Zero)
                    WintunCloseAdapter(st.Adapter);
            }
        }

        public override Stream OpenTunStream(TunnelConfig config)
        {
            ThrowIfConfigIsNull(config);

            if (!_states.TryGetValue(config.TunnelId, out var st) || st.Adapter == nint.Zero)
            {
                // Адаптер не в кэше — откроем и подождём ifIndex
                nint adapter = WintunOpenAdapter(config.TunnelId);
                if (adapter == nint.Zero)
                    ThrowLastWin32($"WintunOpenAdapter('{config.TunnelId}') failed");

                WintunGetAdapterLuid(adapter, out var luid);
                int ifIndex = WaitForIfIndex(luid, IfIndexWaitTimeoutMs);
                if (ifIndex == 0)
                {
                    WintunCloseAdapter(adapter);
                    throw new InvalidOperationException("Wintun интерфейс не появился (ifIndex == 0).");
                }

                st = new AdapterState(adapter, luid, ifIndex);
                _states[config.TunnelId] = st;
            }

            // Стартуем сессию и отдаём твой WintunStream (ожидает session handle!)
            nint session = WintunStartSession(st.Adapter, DefaultRingCapacity);
            if (session == nint.Zero)
                ThrowLastWin32("WintunStartSession failed");

            return new WintunStream(session);
        }

        public override void Dispose()
        {
            if (_disposed) return;

            foreach (var st in _states.Values)
            {
                if (st.Adapter != nint.Zero)
                    WintunCloseAdapter(st.Adapter);
            }
            _states.Clear();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        // --- helpers ---

        private static void ThrowIfConfigIsNull(TunnelConfig? config)
        {
            if (config is null) throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(config.TunnelId))
                throw new ArgumentException("TunnelId must be set.", nameof(config));
            if (string.IsNullOrWhiteSpace(config.LocalIp))
                throw new ArgumentException("LocalIp must be set.", nameof(config));
        }

        private static int WaitForIfIndex(NET_LUID luid, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            int ifIndex = 0;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (ConvertInterfaceLuidToIndex(ref luid, out ifIndex) == 0 && ifIndex != 0)
                    return ifIndex;
                Thread.Sleep(200);
            }
            return 0;
        }

        private static void RunPs(string script)
        {
            var args =
                "-NoProfile -ExecutionPolicy Bypass -Command " +
                "\"[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);" +
                script.Replace("\"", "`\"") +
                "\"";

            var psi = new ProcessStartInfo("powershell", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить PowerShell");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"PowerShell failed ({p.ExitCode}): {stderr}\n{stdout}");
        }

        private static void ThrowLastWin32(string message)
        {
            int err = Marshal.GetLastWin32Error();
            if (err != 0) throw new Win32Exception(err, message);
            throw new InvalidOperationException(message);
        }

        private readonly struct AdapterState
        {
            public nint Adapter { get; }
            public NET_LUID Luid { get; }
            public int IfIndex { get; }

            public AdapterState(nint adapter, NET_LUID luid, int ifIndex)
            {
                Adapter = adapter;
                Luid = luid;
                IfIndex = ifIndex;
            }
        }
    }
}
