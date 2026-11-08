using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // Детект скрытых майнеров: когда за компом никого нет (нет ввода 5+ минут),
    // нагрузки на CPU быть не должно. Раз в минуту сравниваем процессорное время
    // процессов; если в простое кто-то стабильно жрёт CPU - алерт из трея.
    // Про каждый процесс предупреждаем один раз за сессию, чтобы не спамить
    public static class MinerGuard
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private const int IdleThresholdSec = 300;   // считаем простоем 5 минут без мыши/клавы
        private const double CpuAlertPercent = 20;  // % от всего CPU, чтобы попасть под подозрение

        // Системное обслуживание, которое легально работает в простое
        // (Defender-скан, обновления, индексация). svchost сюда не входит намеренно:
        // майнеры часто маскируются под него
        private static readonly HashSet<string> Whitelist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "Memory Compression",
            "MsMpEng", "TiWorker", "TrustedInstaller", "wsappx",
            "CompatTelRunner", "SearchIndexer", "dwm", "DeepTools"
        };

        private static System.Windows.Forms.Timer timer;
        private static Dictionary<int, TimeSpan> prevCpuTimes;
        private static DateTime prevSampleAt;
        private static HashSet<string> alreadyAlerted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool Enabled
        {
            get { return AppConfig.GetBool("miner_guard", true); }
            set { AppConfig.SetBool("miner_guard", value); }
        }

        public static void Start()
        {
            timer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            timer.Tick += (s, e) => { if (Enabled) Tick(); };
            timer.Start();
        }

        private static int GetIdleSeconds()
        {
            try
            {
                var info = new LASTINPUTINFO();
                info.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref info)) return 0;
                return (int)((Environment.TickCount - info.dwTime) / 1000);
            }
            catch
            {
                return 0;
            }
        }

        private static void Tick()
        {
            if (GetIdleSeconds() < IdleThresholdSec)
            {
                // Юзер за компом - замеры простоя неактуальны
                prevCpuTimes = null;
                return;
            }

            var current = new Dictionary<int, TimeSpan>();
            var names = new Dictionary<int, string>();
            try
            {
                Process[] procs = Process.GetProcesses();
                for (int i = 0; i < procs.Length; i++)
                {
                    try
                    {
                        current[procs[i].Id] = procs[i].TotalProcessorTime;
                        names[procs[i].Id] = procs[i].ProcessName;
                    }
                    catch
                    {
                        // системный процесс без доступа - пропускаем
                    }
                    finally
                    {
                        procs[i].Dispose();
                    }
                }
            }
            catch
            {
                return;
            }

            if (prevCpuTimes != null)
            {
                double elapsedSec = (DateTime.Now - prevSampleAt).TotalSeconds;
                if (elapsedSec > 10)
                {
                    double totalCapacity = elapsedSec * Environment.ProcessorCount;

                    foreach (KeyValuePair<int, TimeSpan> pair in current)
                    {
                        TimeSpan prev;
                        if (!prevCpuTimes.TryGetValue(pair.Key, out prev)) continue;

                        string name;
                        if (!names.TryGetValue(pair.Key, out name)) continue;
                        if (Whitelist.Contains(name)) continue;
                        if (alreadyAlerted.Contains(name)) continue;

                        double cpuPercent = (pair.Value - prev).TotalSeconds / totalCapacity * 100;
                        if (cpuPercent >= CpuAlertPercent)
                        {
                            alreadyAlerted.Add(name);
                            TrayNotify.Warn(
                                Lang.T("Подозрительная активность", "Suspicious activity"),
                                name + ".exe " + Lang.T("грузит CPU на ", "is using ")
                                + cpuPercent.ToString("0") + "%"
                                + Lang.T(" пока ты не за компом. Возможен скрытый майнер - проверь процесс.",
                                         " CPU while you are away. Could be a hidden miner - check this process."));
                        }
                    }
                }
            }

            prevCpuTimes = current;
            prevSampleAt = DateTime.Now;
        }
    }
}
