using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DeepTools
{
    // Загрузка GPU через счётчики "GPU Engine" (нет инстанса "_Total" - суммируем
    // 3D-движки всех процессов, как в диспетчере задач).
    // ВАЖНО: перечисление и чтение этих счётчиков на некоторых машинах ОЧЕНЬ медленное
    // и может подвесить поток на секунды. Поэтому вся работа идёт в фоновом потоке,
    // а Get() лишь мгновенно возвращает последнее значение - UI не блокируется
    public static class GpuLoad
    {
        private static volatile float cached = -1f;
        private static readonly object startLock = new object();
        private static bool started = false;

        // Загрузка GPU в %, или -1 если недоступно/ещё не измерено. Не блокирует
        public static float Get()
        {
            EnsureStarted();
            return cached;
        }

        private static void EnsureStarted()
        {
            if (started) return;
            lock (startLock)
            {
                if (started) return;
                started = true;
                var t = new Thread(Loop) { IsBackground = true };
                t.Start();
            }
        }

        private static void Loop()
        {
            var counters = new List<PerformanceCounter>();
            DateTime lastRefresh = DateTime.MinValue;
            bool categoryOk = true;

            while (true)
            {
                try
                {
                    // Набор инстансов обновляем раз в 5 сек (процессы приходят/уходят)
                    if ((DateTime.Now - lastRefresh).TotalSeconds > 5)
                    {
                        lastRefresh = DateTime.Now;
                        for (int i = 0; i < counters.Count; i++) { try { counters[i].Dispose(); } catch { } }
                        counters.Clear();
                        try
                        {
                            var cat = new PerformanceCounterCategory("GPU Engine");
                            string[] names = cat.GetInstanceNames();
                            for (int i = 0; i < names.Length; i++)
                            {
                                if (names[i].IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                try { counters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", names[i], true)); }
                                catch { }
                            }
                            categoryOk = true;
                        }
                        catch { categoryOk = false; }
                    }

                    if (!categoryOk)
                    {
                        cached = -1f;
                    }
                    else
                    {
                        float sum = 0;
                        for (int i = 0; i < counters.Count; i++)
                        {
                            try { sum += counters[i].NextValue(); } catch { }
                        }
                        if (sum < 0) sum = 0;
                        if (sum > 100) sum = 100;
                        cached = sum;
                    }
                }
                catch { }

                Thread.Sleep(1500);
            }
        }
    }
}
