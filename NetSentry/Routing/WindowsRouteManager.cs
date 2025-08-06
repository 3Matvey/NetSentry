using System.Diagnostics;
using NetSentry.Models;

namespace NetSentry.Routing
{
    public class WindowsRouteManager : IRouteManager
    {
        public void ApplyRouting(TunnelConfig config)
        {
            // Включаем NAT для подсети туннеля
            // Здесь создаётся NAT с именем на основе TunnelId
            Run("powershell",
                $"New-NetNat -Name {config.TunnelId} -InternalIPInterfaceAddressPrefix \"{config.LocalIp}/32\" -ExternalIPInterfaceAddressPrefix \"0.0.0.0/0\"");
        }

        public void RemoveRouting(string tunnelId)
        {
            // Удаляем NAT по имени туннеля
            Run("powershell", $"Remove-NetNat -Name {tunnelId}");
        }

        private static void Run(string cmd, string args)
        {
            var psi = new ProcessStartInfo(cmd, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)
                       ?? throw new InvalidOperationException($"Не удалось запустить процесс: {cmd} {args}");
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                var err = p.StandardError.ReadToEnd();
                throw new InvalidOperationException($"{cmd} {args} failed: {err}");
            }
        }
    }
}
