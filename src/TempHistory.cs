using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DeepTools
{
    // Фоновый журнал температур: раз в минуту пишем CPU/GPU температуру в файл,
    // храним 7 дней. На этих данных построен суточный график в Health Check.
    // Формат строки: yyyy-MM-dd HH:mm|cpuTemp|gpuTemp (-1 = датчик молчал)
    public static class TempHistory
    {
        private const int KeepDays = 7;

        private static System.Windows.Forms.Timer timer;
        private static DateTime lastTrim = DateTime.MinValue;

        public static string LogPath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools"),
                    "temps.log");
            }
        }

        public static void Start()
        {
            timer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            timer.Tick += (s, e) => Sample();
            timer.Start();
        }

        private static void Sample()
        {
            int cpu = SystemStats.CpuTempNum;
            int gpu = SystemStats.GpuTempNum;
            if (cpu <= 0 && gpu <= 0) return; // нет ни одного датчика - не мусорим

            try
            {
                string dir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "|" + cpu + "|" + gpu;
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }

            // Подрезаем старьё раз в сутки, а не на каждой записи
            if ((DateTime.Now - lastTrim).TotalHours >= 24)
            {
                lastTrim = DateTime.Now;
                Trim();
            }
        }

        private static void Trim()
        {
            try
            {
                if (!File.Exists(LogPath)) return;
                string[] lines = File.ReadAllLines(LogPath);
                if (lines.Length < 2000) return;

                DateTime cutoff = DateTime.Now.AddDays(-KeepDays);
                var keep = new List<string>();
                for (int i = 0; i < lines.Length; i++)
                {
                    DateTime when;
                    int sep = lines[i].IndexOf('|');
                    if (sep <= 0) continue;
                    if (!DateTime.TryParseExact(lines[i].Substring(0, sep), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) continue;
                    if (when >= cutoff) keep.Add(lines[i]);
                }
                File.WriteAllLines(LogPath, keep.ToArray());
            }
            catch { }
        }

        public class Point
        {
            public DateTime Time;
            public int CpuTemp;
            public int GpuTemp;
        }

        // Точки за последние N часов, для графика
        public static List<Point> Load(int hours)
        {
            var result = new List<Point>();
            try
            {
                if (!File.Exists(LogPath)) return result;
                DateTime cutoff = DateTime.Now.AddHours(-hours);

                string[] lines = File.ReadAllLines(LogPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('|');
                    if (parts.Length < 3) continue;

                    DateTime when;
                    int cpu, gpu;
                    if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) continue;
                    if (when < cutoff) continue;
                    if (!int.TryParse(parts[1], out cpu)) cpu = -1;
                    if (!int.TryParse(parts[2], out gpu)) gpu = -1;

                    result.Add(new Point { Time = when, CpuTemp = cpu, GpuTemp = gpu });
                }
            }
            catch { }
            return result;
        }
    }
}
