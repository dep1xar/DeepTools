using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DeepTools
{
    // Режим «Не спать»: пока включён, Windows не гасит экран и не уходит в сон.
    // SetThreadExecutionState действует, только пока жива программа - при выходе
    // (или падении) система сама возвращает обычное поведение, так что настройки
    // питания этим режимом сломать нельзя
    public static class KeepAwake
    {
        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        private static bool enabled;

        public static bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                try
                {
                    SetThreadExecutionState(value
                        ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
                        : ES_CONTINUOUS);
                }
                catch { }
                AppConfig.Set("keep_awake", value ? "1" : "0");
            }
        }

        // Восстановление режима при старте. Звать строго из UI-потока:
        // SetThreadExecutionState действует только на поток, из которого вызван,
        // а UI-поток - единственный, который живёт всё время работы программы
        public static void Restore()
        {
            if (AppConfig.Get("keep_awake", "0") == "1") Enabled = true;
        }
    }

    // Таймауты экрана и сна активной схемы питания (то же, что Параметры → Питание).
    // Всё через powercfg, значения для питания от сети (-ac)
    public static class PowerTimeouts
    {
        // Варианты для выпадашек: 0 = «никогда»
        public static readonly int[] Choices = { 0, 5, 10, 15, 30, 60, 120 };

        public static string Format(int minutes)
        {
            return minutes <= 0 ? Lang.T("Никогда", "Never") : minutes + Lang.T(" мин", " min");
        }

        public static int GetMonitorMinutes() { return QueryMinutes("SUB_VIDEO VIDEOIDLE"); }
        public static int GetSleepMinutes() { return QueryMinutes("SUB_SLEEP STANDBYIDLE"); }

        public static void SetMonitorMinutes(int m) { Run("/change monitor-timeout-ac " + m); }
        public static void SetSleepMinutes(int m) { Run("/change standby-timeout-ac " + m); }

        // В выводе powercfg /q по одному параметру hex-значения идут в фиксированном
        // порядке: минимум, максимум, шаг, текущее AC, текущее DC. Берём предпоследнее
        // (AC) - это не зависит от языка системы, в отличие от разбора текста строк
        private static int QueryMinutes(string setting)
        {
            string outp = Run("/q SCHEME_CURRENT " + setting);
            if (outp == null) return -1;
            MatchCollection all = Regex.Matches(outp, "0x([0-9a-fA-F]{1,8})");
            if (all.Count < 2) return -1;
            try
            {
                long sec = Convert.ToInt64(all[all.Count - 2].Groups[1].Value, 16);
                return (int)(sec / 60);
            }
            catch { return -1; }
        }

        private static string Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    return p.ExitCode == 0 ? outp : null;
                }
            }
            catch { return null; }
        }
    }
}
