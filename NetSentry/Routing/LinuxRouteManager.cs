using System.Diagnostics;
using NetSentry.Models;

namespace NetSentry.Routing
{
    public class LinuxRouteManager : IRouteManager
    {
        public void ApplyRouting(TunnelConfig config)
        {
            // Включаем форвардинг
            Run("sysctl", "-w net.ipv4.ip_forward=1");

            // Добавляем MASQUERADE на интерфейс TunnelId
            Run("iptables", $"-t nat -A POSTROUTING -o {config.TunnelId} -j MASQUERADE");
        }

        public void RemoveRouting(string tunnelId)
        {
            // Удаляем MASQUERADE
            Run("iptables", $"-t nat -D POSTROUTING -o {tunnelId} -j MASQUERADE");

            // (опционально) отключаем форвардинг, если других туннелей нет
            // Run("sysctl", "-w net.ipv4.ip_forward=0");
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
