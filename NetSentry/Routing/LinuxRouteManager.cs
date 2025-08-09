using System.Diagnostics;
using NetSentry.Models;

namespace NetSentry.Routing
{
    public class LinuxRouteManager : IRouteManager
    {
        private static string IfName(string id) => id.Length > 15 ? id[..15] : id;

        public void ApplyRouting(TunnelConfig config)
        {
            // Включаем форвардинг пакетов на время жизни процесса
            Run("sysctl", "-w net.ipv4.ip_forward=1");

            var egress = Read("sh", "-c \"ip route show default | awk '/default/ {print $5; exit}'\"").Trim();
            if (string.IsNullOrWhiteSpace(egress))
                throw new InvalidOperationException("Cannot detect egress interface");

            var tun = IfName(config.TunnelId);

            // NAT (SNAT/MASQUERADE) на внешнем интерфейсе
            Run("iptables", $"-t nat -A POSTROUTING -o {egress} -j MASQUERADE");

            // Разрешаем трафик из TUN наружу и обратно для established/related
            // (используем conntrack-модуль; state тоже ок, но conntrack новее)
            Run("iptables", $"-A FORWARD -i {tun} -o {egress} -j ACCEPT");
            Run("iptables", $"-A FORWARD -i {egress} -o {tun} -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT");
        }

        public void RemoveRouting(string tunnelId)
        {
            var egress = Read("sh", "-c \"ip route show default | awk '/default/ {print $5; exit}'\"").Trim();
            if (string.IsNullOrWhiteSpace(egress))
                return;

            var tun = IfName(tunnelId);

            // Удаляем в обратном порядке то, что добавили
            Run("iptables", $"-t nat -D POSTROUTING -o {egress} -j MASQUERADE");
            Run("iptables", $"-D FORWARD -i {tun} -o {egress} -j ACCEPT");
            Run("iptables", $"-D FORWARD -i {egress} -o {tun} -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT");
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
