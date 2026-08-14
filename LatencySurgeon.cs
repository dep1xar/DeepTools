using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeepTools
{
    // Хирург латентности: во время игры режет фоновый трафик (Windows Update,
    // Steam, браузеры), переназначает сетевые приоритеты через QoS и отслеживает
    // packet loss / джиттер для отображения в FPS-оверлее.
    public static class LatencySurgeon
    {
        // Процессы, которые суспендируем (сетевую активность) во время игры
        private static readonly string[] UpdateProcs = {
            "wuauclt", "usoclient", "msoobe", "wudfhost",
            "steamservice", "steam", "epicgameslauncher", "origin",
            "uplaywebcore", "upc",
            "onedrive", "googledrivefs", "dropbox",
        };

        // QoS DSCP: 46 = Expedited Forwarding (наивысший приоритет)
        private const int DSCP_HIGH = 46;
        private const int DSCP_LOW  = 0;

        private static bool _active = false;
        private static readonly object _lock = new object();
        private static Thread _pingThread;
        private static volatile bool _pingRunning = false;

        // Последние замеры: packet loss (0-100%), джиттер мс
        public static int PacketLoss { get; private set; }
        public static int Jitter     { get; private set; }
        public static bool IsActive  { get { return _active; } }

        // ---- QoS через netsh / registry ----

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);

        // Суспендируем сетевые процессы-паразиты через NtSuspendProcess (та же
        // техника, что и BackgroundFreezer, поэтому переиспользуем его API)
        private static readonly List<Process> _suspended = new List<Process>();

        public static void Enable()
        {
            lock (_lock)
            {
                if (_active) return;
                _active = true;
            }

            // 1. Суспендировать паразитные обновления
            SuspendUpdateProcs();

            // 2. Повысить QoS для всех пакетов через DSCP-политику
            ApplyQos(DSCP_HIGH);

            // 3. Запустить фоновый мониторинг packet loss / jitter
            StartPingMonitor();
        }

        public static void Disable()
        {
            lock (_lock)
            {
                if (!_active) return;
                _active = false;
            }

            _pingRunning = false;

            ResumeUpdateProcs();
            ApplyQos(DSCP_LOW);
        }

        private static void SuspendUpdateProcs()
        {
            _suspended.Clear();
            foreach (string name in UpdateProcs)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName(name);
                    foreach (Process p in procs)
                    {
                        if (BackgroundFreezer.Freeze(new List<Process> { p }) > 0)
                            _suspended.Add(p);
                    }
                }
                catch { }
            }
        }

        private static void ResumeUpdateProcs()
        {
            foreach (Process p in _suspended)
            {
                try { BackgroundFreezer.ResumeAll(); } catch { }
            }
            _suspended.Clear();
        }

        // Устанавливаем DSCP-метку через netsh qos (требует прав администратора)
        private static void ApplyQos(int dscp)
        {
            try
            {
                // Удаляем старую политику и ставим новую
                RunNetsh("qos delete policy name=\"DeepToolsQoS\"");
                if (dscp > 0)
                {
                    RunNetsh("qos add policy name=\"DeepToolsQoS\" " +
                             "DSCPValue=" + dscp + " IPProtocol=Both");
                }
            }
            catch { }
        }

        private static void RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(3000);
            }
            catch { }
        }

        // ---- Фоновый пинг-монитор (1.1.1.1, 5 пингов каждые 5 секунд) ----

        private static void StartPingMonitor()
        {
            _pingRunning = true;
            _pingThread = new Thread(PingLoop) { IsBackground = true, Name = "LatencySurgeon" };
            _pingThread.Start();
        }

        private static void PingLoop()
        {
            const string host = "1.1.1.1";
            while (_pingRunning)
            {
                try
                {
                    var times = new List<long>();
                    int lost = 0;
                    using (var ping = new Ping())
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            try
                            {
                                PingReply r = ping.Send(host, 1000);
                                if (r != null && r.Status == IPStatus.Success)
                                    times.Add(r.RoundtripTime);
                                else
                                    lost++;
                            }
                            catch { lost++; }
                        }
                    }

                    PacketLoss = lost * 20; // каждый потерянный из 5 = 20%

                    if (times.Count >= 2)
                    {
                        long min = long.MaxValue, max = 0;
                        foreach (long t in times)
                        {
                            if (t < min) min = t;
                            if (t > max) max = t;
                        }
                        Jitter = (int)(max - min);
                    }
                    else
                    {
                        Jitter = 0;
                    }
                }
                catch { }

                Thread.Sleep(5000);
            }
        }
    }
}
