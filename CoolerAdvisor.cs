using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeepTools
{
    // «Пора чистить кулер»: TempHistory уже пишет температуры 7 дней, здесь мы
    // раз в сутки сравниваем средние за первые и последние дни этого окна.
    // Стабильный рост при обычной работе - почти всегда пыль в радиаторе или
    // высохшая термопаста. Алерт шлём не чаще раза в 3 дня, чтобы не бесить
    public static class CoolerAdvisor
    {
        private const int MinDays = 5;         // меньше данных - молчим, вывод недостоверен
        private const int RiseThreshold = 5;   // °C роста между краями недели
        private const int AlertCooldownDays = 3;

        private static System.Windows.Forms.Timer timer;

        public static void Start()
        {
            // Первая проверка через 5 минут после запуска (датчики уже прогрелись,
            // а старт программы не тормозим), дальше - раз в сутки
            timer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
            timer.Tick += (s, e) => {
                timer.Interval = 24 * 60 * 60 * 1000;
                Check();
            };
            timer.Start();
        }

        private static void Check()
        {
            try
            {
                // Не чаще раза в N дней
                DateTime lastAlert;
                string raw = AppConfig.Get("cooler_last_alert", "");
                if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out lastAlert)
                    && (DateTime.Now - lastAlert).TotalDays < AlertCooldownDays)
                    return;

                List<TempHistory.Point> points = TempHistory.Load(24 * 7);

                // Средняя температура CPU по дням
                var daySum = new Dictionary<string, int>();
                var dayCount = new Dictionary<string, int>();
                foreach (TempHistory.Point p in points)
                {
                    if (p.CpuTemp <= 0) continue;
                    string day = p.Time.ToString("yyyy-MM-dd");
                    int cur;
                    daySum[day] = (daySum.TryGetValue(day, out cur) ? cur : 0) + p.CpuTemp;
                    dayCount[day] = (dayCount.TryGetValue(day, out cur) ? cur : 0) + 1;
                }

                var days = new List<string>(daySum.Keys);
                days.Sort();
                if (days.Count < MinDays) return;

                // Дни с парой замеров не показательны - выкидываем
                var avgs = new List<double>();
                foreach (string day in days)
                {
                    if (dayCount[day] < 30) continue; // полчаса данных минимум
                    avgs.Add((double)daySum[day] / dayCount[day]);
                }
                if (avgs.Count < MinDays) return;

                // Сравниваем края окна: первые 2 дня против последних 2
                double early = (avgs[0] + avgs[1]) / 2;
                double late = (avgs[avgs.Count - 1] + avgs[avgs.Count - 2]) / 2;

                if (late - early >= RiseThreshold)
                {
                    AppConfig.Set("cooler_last_alert", DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    TrayNotify.Warn(
                        Lang.T("Пора чистить кулер? 🌡", "Time to clean the cooler? 🌡"),
                        Lang.T("Средняя температура CPU выросла с ", "Average CPU temperature went up from ")
                            + early.ToString("0") + "°C " + Lang.T("до", "to") + " " + late.ToString("0") + "°C "
                            + Lang.T("за неделю. Похоже на пыль или подсохшую термопасту.",
                                     "over the week. Looks like dust or dried thermal paste."));
                }
            }
            catch { }
        }
    }
}
