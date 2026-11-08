using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DeepTools
{
    // Счёт здоровья/производительности ПК (0-100) для дашборда 2.0. Считает
    // штрафы по факторам (память, температуры, автозагрузка, тяжёлый фон) и
    // выдаёт список проблем с рекомендациями. Быстрый - без сканирования диска.
    public static class PcScore
    {
        public class Issue
        {
            public string Title;      // человекочитаемая проблема
            public string Key;        // ram / temp / startup / background
            public bool AutoFixable;  // чинится кнопкой «Оптимизировать»
            public int Severity;      // вес штрафа (для сортировки)
        }

        public class Result
        {
            public int Score;
            public string Verdict;
            public List<Issue> Issues = new List<Issue>();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                         ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX b);

        private static readonly string[] HeavyApps = {
            "chrome", "msedge", "firefox", "opera", "yandexbrowser",
            "discord", "spotify", "epicgameslauncher", "origin", "steamwebhelper", "teams"
        };

        public static Result Compute()
        {
            var r = new Result();
            int score = 100;

            // 1. Память
            uint load = MemoryLoad();
            if (load >= 85) { score -= 15; Add(r, Lang.T("Мало свободной RAM (память забита)", "Low free RAM (memory is full)"), "ram", true, 15); }
            else if (load >= 72) { score -= 7; Add(r, Lang.T("Память загружена — стоит освободить RAM", "Memory is high — free some RAM"), "ram", true, 7); }

            // 2. Температуры (из HealthCheck, если он уже опросил датчики)
            int cpuT = SystemStats.CpuTempNum;
            if (cpuT >= 85) { score -= 12; Add(r, Lang.T("Высокая температура CPU — возможен троттлинг", "High CPU temperature — possible throttling"), "temp", false, 12); }
            else if (cpuT >= 75) { score -= 6; Add(r, Lang.T("CPU греется — проверь охлаждение", "CPU running warm — check cooling"), "temp", false, 6); }

            int gpuT = SystemStats.GpuTempNum;
            if (gpuT >= 85) { score -= 10; Add(r, Lang.T("Высокая температура GPU", "High GPU temperature"), "temp", false, 10); }
            else if (gpuT >= 78) { score -= 5; Add(r, Lang.T("GPU греется", "GPU running warm"), "temp", false, 5); }

            // 3. Автозагрузка
            int startup = StartupCount();
            if (startup > 12) { score -= 10; Add(r, Lang.T("Много программ в автозагрузке (", "Too many startup apps (") + startup + ")", "startup", false, 10); }
            else if (startup > 8) { score -= 6; Add(r, Lang.T("Многовато автозагрузки (", "A lot of startup apps (") + startup + ")", "startup", false, 6); }
            else if (startup > 5) { score -= 3; Add(r, Lang.T("Автозагрузка выше среднего (", "Startup above average (") + startup + ")", "startup", false, 3); }

            // 4. Тяжёлый фон
            int heavy = HeavyBackgroundCount();
            if (heavy > 0)
            {
                int pen = Math.Min(heavy * 2, 10);
                score -= pen;
                Add(r, Lang.T("Тяжёлые программы в фоне: ", "Heavy background apps: ") + heavy, "background", false, pen);
            }

            r.Score = Math.Max(0, Math.Min(100, score));
            r.Verdict = r.Score >= 85 ? Lang.T("Отлично", "Excellent")
                      : r.Score >= 70 ? Lang.T("Хорошо", "Good")
                      : r.Score >= 50 ? Lang.T("Средне", "Fair")
                      : Lang.T("Плохо", "Poor");
            r.Issues.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            return r;
        }

        private static void Add(Result r, string title, string key, bool fix, int sev)
        {
            r.Issues.Add(new Issue { Title = title, Key = key, AutoFixable = fix, Severity = sev });
        }

        public static uint MemoryLoad()
        {
            try { var s = new MEMORYSTATUSEX(); if (GlobalMemoryStatusEx(s)) return s.dwMemoryLoad; }
            catch { }
            return 0;
        }

        private static int StartupCount()
        {
            int n = 0;
            n += RunKeyCount(Registry.CurrentUser);
            n += RunKeyCount(Registry.LocalMachine);
            try
            {
                foreach (Environment.SpecialFolder f in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
                {
                    string dir = Environment.GetFolderPath(f);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        foreach (string file in Directory.GetFiles(dir))
                            if (!file.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)) n++;
                }
            }
            catch { }
            return n;
        }

        private static int RunKeyCount(RegistryKey root)
        {
            try
            {
                using (RegistryKey k = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"))
                    if (k != null) return k.GetValueNames().Length;
            }
            catch { }
            return 0;
        }

        private static int HeavyBackgroundCount()
        {
            int n = 0;
            foreach (string name in HeavyApps)
            {
                try { if (Process.GetProcessesByName(name).Length > 0) n++; }
                catch { }
            }
            return n;
        }
    }
}
