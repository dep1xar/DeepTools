using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Management;

namespace DeepTools
{
    // Время работы системы, момент последней загрузки и длительность последней
    // загрузки (из журнала Diagnostics-Performance, событие 100). Историю
    // длительностей копим в конфиг, чтобы показать тренд «быстрее/медленнее».
    public static class BootInfo
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        public static TimeSpan Uptime()
        {
            try { return TimeSpan.FromMilliseconds(GetTickCount64()); }
            catch { return TimeSpan.Zero; }
        }

        public static DateTime LastBoot()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
                foreach (ManagementObject mo in s.Get())
                {
                    string raw = Convert.ToString(mo["LastBootUpTime"]);
                    if (!string.IsNullOrEmpty(raw))
                        return ManagementDateTimeConverter.ToDateTime(raw);
                }
            }
            catch { }
            return DateTime.Now - Uptime();
        }

        // Длительность последней загрузки в секундах, или -1 если недоступно
        public static int LastBootDurationSec()
        {
            try
            {
                var q = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational",
                    PathType.LogName, "*[System[(EventID=100)]]");
                q.ReverseDirection = true; // новейшее событие первым
                using (var reader = new EventLogReader(q))
                {
                    EventRecord rec = reader.ReadEvent();
                    if (rec == null) return -1;
                    string xml = rec.ToXml();
                    string marker = "Name=\"BootTime\">";
                    int i = xml.IndexOf(marker, StringComparison.Ordinal);
                    if (i < 0) return -1;
                    i += marker.Length;
                    int j = xml.IndexOf('<', i);
                    if (j < 0) return -1;
                    long ms;
                    if (long.TryParse(xml.Substring(i, j - i), out ms)) return (int)(ms / 1000);
                }
            }
            catch { }
            return -1;
        }

        // Записать длительность в историю, если загрузка новая (сменился момент старта).
        // Возвращает список последних длительностей (сек) для тренда.
        public static List<int> RecordAndGetHistory()
        {
            var history = new List<int>();
            try
            {
                string hist = AppConfig.Get("boot_durations", "");
                foreach (string p in hist.Split(','))
                {
                    int v;
                    if (int.TryParse(p.Trim(), out v) && v > 0) history.Add(v);
                }

                string lastBootKey = LastBoot().ToString("yyyyMMddHHmmss");
                if (AppConfig.Get("boot_last_recorded", "") != lastBootKey)
                {
                    int dur = LastBootDurationSec();
                    if (dur > 0)
                    {
                        history.Add(dur);
                        while (history.Count > 10) history.RemoveAt(0);
                        AppConfig.Set("boot_durations", string.Join(",", history.ConvertAll(x => x.ToString()).ToArray()));
                    }
                    AppConfig.Set("boot_last_recorded", lastBootKey);
                }
            }
            catch { }
            return history;
        }
    }
}
