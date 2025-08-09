using NetSentry.Models;
using System.Diagnostics;

namespace NetSentry.Drivers
{
    /// <summary>
    /// Управление виртуальным сетевым интерфейсом для VPN-туннеля.
    /// </summary>
    public abstract class TunAdapter : IDisposable
    {
        private protected bool _disposed;    
        /// <summary>
        /// Поднимает на хосте интерфейс с именем config.TunnelId,
        /// назначает ему IP, MTU и т.п.
        /// </summary>
        public abstract void CreateInterface(TunnelConfig config);

        /// <summary>
        /// Удаляет ранее созданный интерфейс по config.TunnelId.
        /// </summary>
        public abstract void RemoveInterface(string tunnelId);

        /// <summary>
        /// Открывает Stream для обмена «сырыми» IP-пакетами через TUN-интерфейс.
        /// </summary>
        public abstract Stream OpenTunStream(TunnelConfig config);
        public virtual void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        protected static void RunProcess(string cmd, string args)
        {
            var psi = new ProcessStartInfo(cmd, args)
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
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

        protected static void ThrowIfConfigIsNull(TunnelConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentException.ThrowIfNullOrEmpty(config.TunnelId);
            ArgumentException.ThrowIfNullOrEmpty(config.LocalIp);
        }
    }
}
