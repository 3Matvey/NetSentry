using NetSentry.Models;
using System.Diagnostics;

namespace NetSentry.Routing
{
    public class WindowsRouteManager : IRouteManager
    {
        public void ApplyRouting(TunnelConfig config)
        {
            var egress = DetectEgressInterface();

            // Подсеть туннеля: делаем /24 на основе LocalIp (10.x.x.x -> 10.x.x.0/24)
            var lastDot = config.LocalIp.LastIndexOf('.');
            if (lastDot <= 0) throw new InvalidOperationException("Invalid LocalIp");
            var subnet = $"{config.LocalIp[..lastDot]}.0/24";

            // Включаем NAT через PowerShell: имя правила привяжем к tunnelId
            // Internal — это наша TUN-подсеть (/24), External — egress-интерфейс по default route
            Run("powershell", $"-Command \"if (-not (Get-NetNat -Name '{config.TunnelId}' -ErrorAction SilentlyContinue)) " +
                              $"{{ New-NetNat -Name '{config.TunnelId}' -InternalIPInterfaceAddressPrefix '{subnet}' " +
                              $"-ExternalIPInterface '{egress}' | Out-Null }}\"");

            // Разрешаем форвардинг на внешнем интерфейсе (на всякий случай)
            Run("powershell", $"-Command \"Set-NetIPInterface -InterfaceAlias '{egress}' -Forwarding Enabled\"");
        }

        public void RemoveRouting(string tunnelId)
        {
            // Сносим NAT-правило, если было
            Run("powershell", $"-Command \"$n = Get-NetNat -Name '{tunnelId}' -ErrorAction SilentlyContinue; " +
                              "if ($n) { Remove-NetNat -Name $n.Name -Confirm:$false }\"");
        }

        private static string DetectEgressInterface()
        {
            // Берём интерфейс с самым дешёвым маршрутом по умолчанию
            var name = Read("powershell",
                "-Command \"(Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | Select-Object -First 1).InterfaceAlias\"");
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Cannot detect egress interface");
            return name.Trim();
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
                throw new InvalidOperationException($"{cmd} {args} failed: {p.StandardError.ReadToEnd()}");
        }

        private static string Read(string cmd, string args)
        {
            var psi = new ProcessStartInfo(cmd, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi)
                ?? throw new InvalidOperationException($"Не удалось запустить процесс: {cmd} {args}");
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"{cmd} {args} failed: {p.StandardError.ReadToEnd()}");
            return stdout;
        }
    }
}
