using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // FPS-оверлей поверх игр: маленькое полупрозрачное окно в углу экрана.
    // FPS берём из PresentTracer (ETW-события Present ядра DirectX - настоящий
    // FPS процесса активного окна, как у PresentMon). Если трейсер не работает
    // или активное окно ничего не рисует, откатываемся на DwmFlush - FPS
    // композитора, он упирается в частоту монитора и годится только как
    // грубая оценка. Плюс CPU/RAM, загрузка и температура GPU.
    // Что показывать, в каком углу и какого размера - настраивается в OverlayConfigForm
    // и хранится в AppConfig (см. OverlaySettings).
    public class OverlayForm : Form
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();
        private System.Threading.Thread fpsThread;
        private volatile bool fpsThreadStop = false;
        private volatile int currentFps = 0;
        private volatile float currentFrameMs = 0;
        private volatile int currentLowFps = 0;

        // Кольцевой буфер фреймтаймов за последние ~2000 кадров: из него считаем
        // 1% low (99-й перцентиль времени кадра) и рисуем мини-график спайков
        private const int FrameBufSize = 2000;
        private readonly float[] frameBuf = new float[FrameBufSize];
        private int frameBufPos = 0;
        private int frameBufCount = 0;
        private readonly object frameBufLock = new object();

        // Снимок последних фреймтаймов для отрисовки графика (обновляется раз в сек)
        private float[] graphFrames = new float[0];

        private static readonly int selfPid = Process.GetCurrentProcess().Id;

        private PerformanceCounter cpuCounter;
        private PerformanceCounter gpuCounter;
        private float cpuValue = 0;
        private float gpuLoad = -1;
        private int gpuTemp = -1;

        private OverlaySettings s;

        public OverlayForm()
        {
            s = OverlaySettings.Load();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Opacity = 0.82;
            DoubleBuffered = true;

            try { cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); }
            catch { cpuCounter = null; }
            try { gpuCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", "_Total"); }
            catch { gpuCounter = null; }

            LayoutBySettings();

            uiTimer.Interval = 1000;
            uiTimer.Tick += (s2, e) => RefreshStats();

            Load += (s2, e) => {
                // WS_EX_TRANSPARENT пропускает мышь сквозь оверлей, NOACTIVATE не даёт
                // ему красть фокус у игры
                int style = GetWindowLong(Handle, GWL_EXSTYLE);
                SetWindowLong(Handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                StartFpsCounter();
                uiTimer.Start();
            };
            FormClosed += (s2, e) => StopFpsCounter();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        // Размер окна и позиция считаются из настроек: масштаб + число активных строк
        private void LayoutBySettings()
        {
            float k = s.Scale;
            int rows = s.MetricRowCount;                 // CPU/GPU/RAM строки (без FPS-блока)
            int fpsBlockH = s.ShowFps ? (int)(52 * k) : 0;
            int lowRowH = (s.ShowFps && s.Show1Low) ? (int)(18 * k) : 0;
            int graphH = (s.ShowFps && s.ShowFtGraph) ? (int)(34 * k) : 0;
            int rowH = (int)(22 * k);
            int padTop = (int)(6 * k);
            int padBottom = (int)(8 * k);

            int w = (int)(s.HasBigFps ? 180 * k : 150 * k);
            int h = padTop + fpsBlockH + lowRowH + graphH + rows * rowH + padBottom;
            if (h < (int)(40 * k)) h = (int)(40 * k);
            Size = new Size(w, h);

            Rectangle b = Screen.PrimaryScreen.Bounds;
            int m = 16;
            int x, y;
            switch (s.Corner)
            {
                case 1: x = b.Right - Width - m; y = b.Top + m; break;   // верх-право
                case 2: x = b.Left + m; y = b.Bottom - Height - m; break; // низ-лево
                case 3: x = b.Right - Width - m; y = b.Bottom - Height - m; break; // низ-право
                default: x = b.Left + m; y = b.Top + m; break;           // верх-лево
            }
            Location = new Point(x, y);
        }

        // Поток обновляет FPS раз в секунду. Основной источник - PresentTracer
        // (ETW, реальные Present-кадры процесса активного окна). Если он молчит
        // (нет прав, окно ничего не рисует, рабочий стол) - фолбэк на DwmFlush:
        // счёт кадров композитора, что упирается в частоту монитора
        private void StartFpsCounter()
        {
            fpsThreadStop = false;
            PresentTracer.Start();
            fpsThread = new System.Threading.Thread(() => {
                var sw = Stopwatch.StartNew();
                var frameSw = Stopwatch.StartNew();
                int frames = 0;
                while (!fpsThreadStop)
                {
                    try { DwmFlush(); }
                    catch { System.Threading.Thread.Sleep(16); }
                    frames++;

                    float frameMs = (float)frameSw.Elapsed.TotalMilliseconds;
                    frameSw.Restart();
                    lock (frameBufLock)
                    {
                        frameBuf[frameBufPos] = frameMs;
                        frameBufPos = (frameBufPos + 1) % FrameBufSize;
                        if (frameBufCount < FrameBufSize) frameBufCount++;
                    }

                    if (sw.ElapsedMilliseconds >= 1000)
                    {
                        PresentTracer.Snap snap = null;
                        try
                        {
                            int pid = GetForegroundPid();
                            snap = PresentTracer.SnapshotForPid(pid);
                        }
                        catch { }

                        if (snap != null)
                        {
                            // Настоящий FPS игры из ETW
                            currentFps = snap.Fps;
                            currentFrameMs = snap.AvgMs;
                            currentLowFps = snap.LowFps;
                            graphFrames = snap.FrameMs;
                        }
                        else
                        {
                            // Фолбэк: FPS композитора через DwmFlush
                            int fps = (int)(frames * 1000L / sw.ElapsedMilliseconds);
                            currentFps = fps;
                            currentFrameMs = fps > 0 ? 1000f / fps : 0;
                            currentLowFps = CalcOnePercentLow();
                        }
                        frames = 0;
                        sw.Restart();
                    }
                }
            });
            fpsThread.IsBackground = true;
            fpsThread.Start();
        }

        // PID процесса активного окна: чей FPS показываем. Своё окно и оверлей
        // пропускаем - иначе при клике по оверлею начнём мерить сами себя
        private int GetForegroundPid()
        {
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return 0;
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid == (uint)selfPid) return 0;
            return (int)pid;
        }

        // 1% low: берём 99-й перцентиль времени кадра и переводим в FPS.
        // Это FPS в худшей сотой доле кадров - показывает фризы, которые
        // средний FPS прячет
        private int CalcOnePercentLow()
        {
            float[] copy;
            lock (frameBufLock)
            {
                if (frameBufCount < 100) return 0; // мало данных - не обманываем
                copy = new float[frameBufCount];
                // Копируем в хронологическом порядке (для графика важен порядок)
                int start = frameBufCount < FrameBufSize ? 0 : frameBufPos;
                for (int i = 0; i < frameBufCount; i++)
                    copy[i] = frameBuf[(start + i) % FrameBufSize];
            }
            graphFrames = copy;

            float[] sorted = (float[])copy.Clone();
            Array.Sort(sorted);
            int idx = (int)(sorted.Length * 0.99f);
            if (idx >= sorted.Length) idx = sorted.Length - 1;
            float worstMs = sorted[idx];
            return worstMs > 0 ? (int)(1000f / worstMs) : 0;
        }

        private void StopFpsCounter()
        {
            fpsThreadStop = true;
            PresentTracer.Stop();
        }

        private void RefreshStats()
        {
            try { if (cpuCounter != null) cpuValue = cpuCounter.NextValue(); } catch { }
            gpuLoad = GpuLoad.Get();
            gpuTemp = NvmlGpu.GetTemperature();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float k = s.Scale;

            using (var path = Theme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), (int)(10 * k)))
            using (var bg = new SolidBrush(Color.FromArgb(16, 20, 28)))
            using (var border = new Pen(Color.FromArgb(60, 70, 90)))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            Color dim = Color.FromArgb(150, 160, 175);
            Color main = Color.FromArgb(220, 226, 235);
            int leftPad = (int)(12 * k);
            int y = (int)(6 * k);

            // FPS крупно (+ frametime рядом, если включён)
            if (s.ShowFps)
            {
                Color fpsColor = currentFps >= 60 ? Color.FromArgb(46, 214, 140)
                    : currentFps >= 30 ? Color.FromArgb(240, 180, 70)
                    : Color.FromArgb(230, 90, 90);
                using (var fpsFont = new Font("Segoe UI", 22F * k, FontStyle.Bold))
                using (var fpsBrush = new SolidBrush(fpsColor))
                    g.DrawString(currentFps.ToString(), fpsFont, fpsBrush, leftPad - 2, y);

                using (var lblFont = new Font("Segoe UI", 8F * k))
                using (var lblBrush = new SolidBrush(dim))
                    g.DrawString("FPS", lblFont, lblBrush, leftPad + 2, y + (int)(38 * k));

                if (s.ShowFrametime)
                {
                    using (var ftFont = new Font("Segoe UI", 9F * k, FontStyle.Bold))
                    using (var ftBrush = new SolidBrush(main))
                    using (var ftLbl = new Font("Segoe UI", 8F * k))
                    using (var ftLblBrush = new SolidBrush(dim))
                    {
                        g.DrawString(string.Format("{0:0.0}", currentFrameMs), ftFont, ftBrush, leftPad + (int)(64 * k), y + (int)(6 * k));
                        g.DrawString("ms", ftLbl, ftLblBrush, leftPad + (int)(64 * k), y + (int)(26 * k));
                    }
                }
                y += (int)(52 * k);

                // 1% low: FPS в худшем проценте кадров - виден статтер
                if (s.Show1Low)
                {
                    using (var lowLblFont = new Font("Segoe UI", 8F * k))
                    using (var lowValFont = new Font("Segoe UI", 8.5F * k, FontStyle.Bold))
                    using (var lowLblBrush = new SolidBrush(dim))
                    using (var lowValBrush = new SolidBrush(currentLowFps > 0 && currentFps > 0 && currentLowFps * 2 < currentFps
                        ? Color.FromArgb(240, 180, 70) : main))
                    {
                        g.DrawString("1% low", lowLblFont, lowLblBrush, leftPad, y);
                        g.DrawString(currentLowFps > 0 ? currentLowFps.ToString() : "-", lowValFont, lowValBrush, leftPad + (int)(48 * k), y - (int)(1 * k));
                    }
                    y += (int)(18 * k);
                }

                // Мини-график фреймтайма (последние ~2 сек): спайки = фризы
                if (s.ShowFtGraph)
                {
                    int gh = (int)(28 * k);
                    var plot = new Rectangle(leftPad, y + (int)(2 * k), Width - leftPad * 2, gh);
                    using (var plotBg = new SolidBrush(Color.FromArgb(28, 34, 46)))
                        g.FillRectangle(plotBg, plot);

                    float[] frames = graphFrames;
                    if (frames.Length >= 2)
                    {
                        // Берём хвост буфера - примерно 2 секунды кадров
                        int take = Math.Min(frames.Length, Math.Max(120, currentFps * 2));
                        int off = frames.Length - take;
                        // Шкала: 0..33мс (30 FPS); что выше - упирается в потолок
                        const float maxMs = 33.3f;
                        float stepX = plot.Width / (float)(take - 1);
                        var pts = new PointF[take];
                        for (int i = 0; i < take; i++)
                        {
                            float v = Math.Min(frames[off + i], maxMs);
                            pts[i] = new PointF(plot.Left + i * stepX, plot.Bottom - v / maxMs * plot.Height);
                        }
                        using (var linePen = new Pen(Color.FromArgb(90, 200, 250), 1f))
                        {
                            for (int i = 1; i < take; i++) g.DrawLine(linePen, pts[i - 1], pts[i]);
                        }
                        // Линия 16.7мс (60 FPS) для ориентира
                        int y60 = (int)(plot.Bottom - 16.7f / maxMs * plot.Height);
                        using (var refPen = new Pen(Color.FromArgb(70, 255, 255, 255)))
                            g.DrawLine(refPen, plot.Left, y60, plot.Right, y60);
                    }
                    y += (int)(34 * k);
                }
            }

            // Строки метрик
            using (var lblFont = new Font("Segoe UI", 8.5F * k))
            using (var valFont = new Font("Segoe UI", 9F * k, FontStyle.Bold))
            using (var lblBrush = new SolidBrush(dim))
            using (var valBrush = new SolidBrush(main))
            {
                int rowH = (int)(22 * k);
                int valX = leftPad + (int)(48 * k);
                foreach (var row in BuildMetricRows())
                {
                    g.DrawString(row.Key, lblFont, lblBrush, leftPad, y + (int)(2 * k));
                    g.DrawString(row.Value, valFont, valBrush, valX, y);
                    y += rowH;
                }
            }
        }

        private List<KeyValuePair<string, string>> BuildMetricRows()
        {
            var rows = new List<KeyValuePair<string, string>>();
            if (s.ShowCpu) rows.Add(new KeyValuePair<string, string>("CPU", string.Format("{0:0}%", cpuValue)));
            if (s.ShowGpuLoad) rows.Add(new KeyValuePair<string, string>("GPU", gpuLoad >= 0 ? string.Format("{0:0}%", gpuLoad) : "-"));
            if (s.ShowGpuTemp) rows.Add(new KeyValuePair<string, string>(s.ShowGpuLoad ? "T°" : "GPU", gpuTemp >= 0 ? gpuTemp + "°C" : "-"));
            if (s.ShowRam) rows.Add(new KeyValuePair<string, string>("RAM", string.Format("{0:0}%", GetRamPercent())));
            return rows;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private float GetRamPercent()
        {
            try
            {
                var state = new MEMORYSTATUSEX();
                if (!GlobalMemoryStatusEx(state)) return 0;
                return state.dwMemoryLoad;
            }
            catch { return 0; }
        }
    }

    // Настройки оверлея: угол, размер, набор метрик. Хранятся в AppConfig
    public class OverlaySettings
    {
        public int Corner;      // 0=верх-лево, 1=верх-право, 2=низ-лево, 3=низ-право
        public int SizeIndex;   // 1=S, 2=M, 3=L
        public bool ShowFps;
        public bool ShowFrametime;
        public bool Show1Low;     // 1% low FPS под крупным FPS
        public bool ShowFtGraph;  // мини-график фреймтайма
        public bool ShowCpu;
        public bool ShowGpuLoad;
        public bool ShowGpuTemp;
        public bool ShowRam;

        public float Scale
        {
            get { return SizeIndex <= 1 ? 0.85f : (SizeIndex >= 3 ? 1.2f : 1.0f); }
        }

        // Есть ли крупный FPS-блок (влияет на ширину окна)
        public bool HasBigFps { get { return ShowFps; } }

        public int MetricRowCount
        {
            get
            {
                int n = 0;
                if (ShowCpu) n++;
                if (ShowGpuLoad) n++;
                if (ShowGpuTemp) n++;
                if (ShowRam) n++;
                return n;
            }
        }

        public static OverlaySettings Load()
        {
            return new OverlaySettings
            {
                Corner = ParseInt(AppConfig.Get("overlay_corner", "0"), 0),
                SizeIndex = ParseInt(AppConfig.Get("overlay_size", "2"), 2),
                ShowFps = AppConfig.GetBool("overlay_fps", true),
                ShowFrametime = AppConfig.GetBool("overlay_frametime", false),
                Show1Low = AppConfig.GetBool("overlay_1low", false),
                ShowFtGraph = AppConfig.GetBool("overlay_ftgraph", false),
                ShowCpu = AppConfig.GetBool("overlay_cpu", true),
                ShowGpuLoad = AppConfig.GetBool("overlay_gpuload", false),
                ShowGpuTemp = AppConfig.GetBool("overlay_gputemp", true),
                ShowRam = AppConfig.GetBool("overlay_ram", true)
            };
        }

        public void Save()
        {
            AppConfig.Set("overlay_corner", Corner.ToString());
            AppConfig.Set("overlay_size", SizeIndex.ToString());
            AppConfig.SetBool("overlay_fps", ShowFps);
            AppConfig.SetBool("overlay_frametime", ShowFrametime);
            AppConfig.SetBool("overlay_1low", Show1Low);
            AppConfig.SetBool("overlay_ftgraph", ShowFtGraph);
            AppConfig.SetBool("overlay_cpu", ShowCpu);
            AppConfig.SetBool("overlay_gpuload", ShowGpuLoad);
            AppConfig.SetBool("overlay_gputemp", ShowGpuTemp);
            AppConfig.SetBool("overlay_ram", ShowRam);
        }

        private static int ParseInt(string v, int def)
        {
            int r;
            return int.TryParse(v, out r) ? r : def;
        }
    }
}
