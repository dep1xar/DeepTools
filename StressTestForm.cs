using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace DeepTools
{
    // Стресс-тест охлаждения: грузим все ядра CPU на 100% на 2 минуты и смотрим,
    // до скольки градусов дойдёт. Показывает, вывозит ли кулер и не пора ли менять пасту
    public class StressTestForm : Form
    {
        private const int DurationSec = 120;

        private LoadGraph tempGraph;
        private Label currentTempLabel;
        private Label maxTempLabel;
        private Label timeLabel;
        private Label verdictLabel;
        private RoundedButton startBtn;

        private volatile bool running = false;
        private Thread[] loadThreads;
        private System.Windows.Forms.Timer sampleTimer;
        private int elapsed = 0;
        private int maxTemp = -1;

        private Point dragStart;
        private bool draggingForm = false;

        public StressTestForm()
        {
            Text = "DeepTools Stress Test";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(500, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => ApplyRoundedRegion();
            FormClosing += (s, e) => StopLoad();
        }

        private void ApplyRoundedRegion()
        {
            var path = new GraphicsPath();
            int r = 12, d = r * 2;
            var rect = new Rectangle(0, 0, Width, Height);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        private void BuildUi()
        {
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.BgColor };
            titleBar.MouseDown += (s, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) => {
                if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("🌡 Стресс-тест охлаждения", "🌡 Cooling stress test"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            };
            titleBar.Controls.Add(titleLbl);

            var closeBtn = new Label
            {
                Text = "✕",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F),
                Size = new Size(30, 26),
                Location = new Point(Width - 42, 7),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => Close();
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            var card = Theme.MakeCard(this, new Point(16, 52), new Size(468, 240));

            var graphTitle = new Label
            {
                Text = Lang.T("Температура CPU, °C", "CPU temperature, °C"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(14, 12),
                AutoSize = true
            };
            card.Controls.Add(graphTitle);

            tempGraph = new LoadGraph { Location = new Point(14, 36), Size = new Size(440, 150), LineColor = Theme.Warning, MaxPoints = DurationSec };
            card.Controls.Add(tempGraph);

            currentTempLabel = MakeStat(card, Lang.T("Сейчас", "Now"), 14, 194);
            maxTempLabel = MakeStat(card, Lang.T("Максимум", "Max"), 170, 194);
            timeLabel = MakeStat(card, Lang.T("Время", "Time"), 330, 194);

            startBtn = new RoundedButton
            {
                Text = Lang.T("Начать (2 минуты)", "Start (2 minutes)"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(16, 306),
                Size = new Size(190, 38)
            };
            startBtn.Click += (s, e) => { if (running) StopAndFinish(); else StartLoad(); };
            Controls.Add(startBtn);

            verdictLabel = new Label
            {
                Text = Lang.T("Тест нагреет процессор до предела - закрой тяжёлые программы. Остановить можно в любой момент.",
                              "The test pushes the CPU to its limit - close heavy apps first. You can stop at any time."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 352),
                Size = new Size(468, 50)
            };
            Controls.Add(verdictLabel);
        }

        private Label MakeStat(Control parent, string title, int x, int y)
        {
            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(x, y),
                AutoSize = true
            };
            parent.Controls.Add(titleLbl);

            var valueLbl = new Label
            {
                Text = "—",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(x + 60, y - 6),
                AutoSize = true
            };
            parent.Controls.Add(valueLbl);
            return valueLbl;
        }

        private void StartLoad()
        {
            running = true;
            elapsed = 0;
            maxTemp = -1;
            startBtn.Text = Lang.T("Остановить", "Stop");
            startBtn.ButtonColor = Theme.Danger;
            startBtn.HoverColor = Theme.DangerHover;
            startBtn.TextColor = Theme.BgColor;
            startBtn.Invalidate();
            verdictLabel.Text = Lang.T("Греем процессор...", "Heating up the CPU...");
            verdictLabel.ForeColor = Theme.TextDim;

            int cores = Environment.ProcessorCount;
            loadThreads = new Thread[cores];
            for (int i = 0; i < cores; i++)
            {
                loadThreads[i] = new Thread(() => {
                    double acc = 1.0001;
                    ulong x = 123456789UL;
                    while (running)
                    {
                        for (int k = 0; k < 200000; k++)
                        {
                            x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                            acc = acc * 1.0000001 + (x & 0xFF);
                        }
                    }
                    if (acc == 0) Console.WriteLine(acc);
                });
                loadThreads[i].IsBackground = true;
                loadThreads[i].Priority = ThreadPriority.BelowNormal; // интерфейс не должен зависнуть
                loadThreads[i].Start();
            }

            sampleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            sampleTimer.Tick += (s, e) => Sample();
            sampleTimer.Start();
        }

        private void Sample()
        {
            elapsed++;
            int temp = CpuSensor.GetTemperature();

            if (temp > 0)
            {
                tempGraph.AddPoint(temp);
                if (temp > maxTemp) maxTemp = temp;
                currentTempLabel.Text = temp + "°C";
                currentTempLabel.ForeColor = temp >= 90 ? Theme.Off : (temp >= 80 ? Theme.Warning : Theme.On);
                maxTempLabel.Text = maxTemp + "°C";
            }
            else
            {
                currentTempLabel.Text = Lang.T("н/д", "n/a");
            }

            timeLabel.Text = elapsed + " / " + DurationSec + Lang.T(" с", " s");

            if (elapsed >= DurationSec) StopAndFinish();
        }

        private void StopLoad()
        {
            running = false;
            if (sampleTimer != null) { sampleTimer.Stop(); sampleTimer.Dispose(); sampleTimer = null; }
        }

        private void StopAndFinish()
        {
            bool completed = elapsed >= DurationSec;
            StopLoad();

            startBtn.Text = Lang.T("Запустить ещё раз", "Run again");
            startBtn.ButtonColor = Theme.Accent;
            startBtn.HoverColor = Theme.AccentHover;
            startBtn.Invalidate();

            if (maxTemp <= 0)
            {
                verdictLabel.Text = Lang.T("Датчик температуры недоступен - оценить охлаждение не получилось", "Temperature sensor unavailable - could not evaluate cooling");
                verdictLabel.ForeColor = Theme.Warning;
                return;
            }

            string prefix = completed
                ? Lang.T("Тест завершён. Максимум: ", "Test finished. Max: ")
                : Lang.T("Тест остановлен. Максимум: ", "Test stopped. Max: ");

            if (maxTemp < 75)
            {
                verdictLabel.Text = prefix + maxTemp + Lang.T("°C - отличное охлаждение, запас есть ✓", "°C - excellent cooling, plenty of headroom ✓");
                verdictLabel.ForeColor = Theme.On;
            }
            else if (maxTemp < 85)
            {
                verdictLabel.Text = prefix + maxTemp + Lang.T("°C - норма для нагрузки, но летом может быть жарче", "°C - fine under load, but summer may push it higher");
                verdictLabel.ForeColor = Theme.On;
            }
            else if (maxTemp < 95)
            {
                verdictLabel.Text = prefix + maxTemp + Lang.T("°C - горячо: почисти кулер от пыли, проверь термопасту", "°C - hot: clean the cooler dust, check thermal paste");
                verdictLabel.ForeColor = Theme.Warning;
            }
            else
            {
                verdictLabel.Text = prefix + maxTemp + Lang.T("°C - перегрев! CPU сбрасывает частоты. Срочно займись охлаждением", "°C - overheating! CPU is throttling. Fix your cooling ASAP");
                verdictLabel.ForeColor = Theme.Off;
            }
        }
    }
}
