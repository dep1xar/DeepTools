using System;
using System.Collections.Generic;

namespace DeepTools
{
    // Статистика по кадрам за последнее время. Раз в тик детекта (~1.5 с, из
    // GameSessionTracker) забираем у ETW-трейсера кадры активной игры и копим
    // кольцевой буфер на два часа. Из него строится отчёт «за 30 минут / час /
    // 2 часа», где видно не только средний FPS и 1% low, но и каждый микрофриз:
    // когда случился и сколько миллисекунд длился.
    public static class FrameLog
    {
        public class Sample
        {
            public DateTime Time;
            public string Game;
            public int Fps;
            public int LowFps;    // 1% худших кадров этой порции
            public int Frames;    // сколько кадров в порции (для среднего по окну)
            public float AvgMs;
            public float WorstMs;
            public int Stutters;
            public bool Gap;      // разрыв в данных (игра свернулась, трейсер молчал)
        }

        public class Stutter
        {
            public DateTime Time;
            public string Game;
            public float Ms;
        }

        public class Report
        {
            public DateTime From, To;
            public DateTime DataFrom;      // с какого времени вообще есть данные
            public int Samples;
            public int AvgFps;
            public int LowFps;             // худший 1% low за окно
            public int Stutters;
            public float WorstMs;
            public DateTime WorstAt;
            public string WorstGame = "";
            public int Games;
            public int ActiveSec;          // сколько секунд игры попало в окно
            public List<Sample> Timeline = new List<Sample>();
            public List<Stutter> Worst = new List<Stutter>();
        }

        // Два часа по секунде - с запасом, чтобы окно «2 часа» не упиралось в край
        private const int MaxSamples = 7200;
        private const int MaxStutters = 400;
        private const double KeepMinutes = 150;
        private const int MaxWorstInReport = 10;

        private static readonly object sync = new object();
        private static readonly List<Sample> samples = new List<Sample>();
        private static readonly List<Stutter> stutters = new List<Stutter>();
        private static int lastPid;
        private static long lastTicks;

        public static bool HasData
        {
            get { lock (sync) return samples.Count > 0; }
        }

        // Сколько времени назад есть данные (для честной подписи «данные с …»)
        public static DateTime? DataFrom
        {
            get
            {
                lock (sync)
                {
                    if (samples.Count == 0) return null;
                    DateTime min = samples[0].Time;
                    for (int i = 1; i < samples.Count; i++)
                        if (samples[i].Time < min) min = samples[i].Time;
                    return min;
                }
            }
        }

        // Зовётся из трекера сессий: pid игры (0 - игры нет) и её имя
        public static void Tick(int pid, string game)
        {
            if (pid <= 0)
            {
                // Игра закончилась: следующая начнёт окно заново
                lastPid = 0;
                lastTicks = 0;
                return;
            }
            if (pid != lastPid)
            {
                lastPid = pid;
                lastTicks = 0;
            }

            PresentTracer.FrameWindow w = PresentTracer.TakeFrames(pid, lastTicks);
            if (w == null) return;
            lastTicks = w.Newest;

            float[] ms = w.Ms;
            double sum = 0;
            for (int i = 0; i < ms.Length; i++) sum += ms[i];
            if (sum <= 0) return;

            float[] sorted = (float[])ms.Clone();
            Array.Sort(sorted);
            float median = sorted[sorted.Length / 2];
            // Микрофриз - кадр, который встал заметно дольше обычного для этой игры:
            // порог подстраивается под её FPS (25 мс минимум, иначе на 240 Гц всё
            // считалось бы фризом)
            float threshold = Math.Max(25f, median * 2f);

            var s = new Sample();
            s.Time = DateTime.Now;
            s.Game = game == null ? "" : game;
            s.Frames = ms.Length;
            s.AvgMs = (float)(sum / ms.Length);
            s.Fps = (int)Math.Round(ms.Length * 1000.0 / sum);

            int idx = (int)(sorted.Length * 0.99f);
            if (idx >= sorted.Length) idx = sorted.Length - 1;
            float worstFrame = sorted[idx];
            s.LowFps = worstFrame > 0 ? (int)(1000f / worstFrame) : 0;

            // Разрыв: если порция покрывает сильно больше времени, чем длина окна,
            // значит трейсер молчал (игра свёрнута) - помечаем, чтобы не путать
            // паузу с фризом
            s.Gap = sum > 4000;

            // Микрофризы: время считаем назад от конца порции, чтобы у каждого
            // события была своя отметка, а не одна на всю порцию
            float after = 0;
            for (int i = ms.Length - 1; i >= 0; i--)
            {
                float v = ms[i];
                if (v >= threshold)
                {
                    if (s.Stutters == 0 || v > s.WorstMs) s.WorstMs = v;
                    s.Stutters++;
                    var st = new Stutter();
                    st.Time = s.Time.AddMilliseconds(-after - v / 2.0);
                    st.Game = s.Game;
                    st.Ms = v;
                    lock (sync) stutters.Add(st);
                }
                after += v;
            }

            lock (sync)
            {
                samples.Add(s);
                TrimLocked(s.Time);
            }
        }

        // Пометить паузу (игра свёрнута или закрылась) - в отчёте это разрыв, а не
        // провал FPS
        public static void MarkGap(string game)
        {
            lock (sync)
            {
                if (samples.Count > 0 && samples[samples.Count - 1].Gap) return;
                var s = new Sample();
                s.Time = DateTime.Now;
                s.Game = game == null ? "" : game;
                s.Gap = true;
                s.Fps = 0;
                s.LowFps = 0;
                samples.Add(s);
                TrimLocked(s.Time);
            }
        }

        private static void TrimLocked(DateTime now)
        {
            DateTime keepFrom = now.AddMinutes(-KeepMinutes);
            if (samples.Count > MaxSamples)
                samples.RemoveRange(0, samples.Count - MaxSamples);
            while (samples.Count > 0 && samples[0].Time < keepFrom) samples.RemoveAt(0);
            // Фризы чистим по тому же времени, оставляя хвост для списка
            int cut = 0;
            while (cut < stutters.Count && stutters[cut].Time < keepFrom) cut++;
            if (cut > 0) stutters.RemoveRange(0, cut);
            if (stutters.Count > MaxStutters)
                stutters.RemoveRange(0, stutters.Count - MaxStutters);
        }

        public static void Clear()
        {
            lock (sync)
            {
                samples.Clear();
                stutters.Clear();
                lastTicks = 0;
            }
        }

        // Отчёт за окно времени (30 минут / час / 2 часа)
        public static Report Build(TimeSpan window)
        {
            var r = new Report();
            r.To = DateTime.Now;
            r.From = r.To - window;

            long frames = 0;
            double fpsWeighted = 0;
            int worstLow = int.MaxValue;
            var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int gaps = 0;

            lock (sync)
            {
                foreach (Sample s in samples)
                {
                    if (r.DataFrom == default(DateTime) || s.Time < r.DataFrom) r.DataFrom = s.Time;
                    if (s.Time < r.From) continue;
                    r.Timeline.Add(s);
                    if (s.Gap) { gaps++; continue; }
                    r.Samples++;
                    frames += s.Frames;
                    fpsWeighted += (double)s.Fps * s.Frames;
                    if (s.LowFps > 0 && s.LowFps < worstLow) worstLow = s.LowFps;
                    if (s.Game != "") games.Add(s.Game);
                }

                foreach (Stutter st in stutters)
                {
                    if (st.Time < r.From) continue;
                    r.Stutters++;
                    if (st.Ms > r.WorstMs)
                    {
                        r.WorstMs = st.Ms;
                        r.WorstAt = st.Time;
                        r.WorstGame = st.Game;
                    }
                    if (r.Worst.Count < 200) r.Worst.Add(st);
                }
            }

            r.AvgFps = frames > 0 ? (int)Math.Round(fpsWeighted / frames) : 0;
            r.LowFps = worstLow == int.MaxValue ? 0 : worstLow;
            r.Games = games.Count;
            r.ActiveSec = r.Samples; // порция ~1 секунда данных

            r.Worst.Sort((a, b) => b.Ms.CompareTo(a.Ms));
            if (r.Worst.Count > MaxWorstInReport)
                r.Worst.RemoveRange(MaxWorstInReport, r.Worst.Count - MaxWorstInReport);
            return r;
        }
    }
}
