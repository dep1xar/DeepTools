using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Окно "История температур": график CPU/GPU за последние 24 часа
    // из журнала TempHistory + разбор пиков с вердиктом
    public class TempHistoryForm : Form
    {
        private List<TempHistory.Point> points;


        public TempHistoryForm()
        {
            Text = "DeepTools Temp History";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(640, 460);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            points = TempHistory.Load(24);

            BuildUi();
            Load += (s, e) => ApplyRoundedRegion();
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
            // Перетаскивание за шапку: точка захвата считается от курсора, а не от
            // уезжающего вместе с окном контрола - окно едет ровно под мышью
            WindowDrag.Attach(titleBar, this);
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("📈 История температур (24 часа)", "📈 Temperature history (24 hours)"),
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

            var card = Theme.MakeCard(this, new Point(16, 52), new Size(608, 310));
            var graph = new TempGraphControl(points) { Location = new Point(12, 12), Size = new Size(584, 286) };
            card.Controls.Add(graph);

            // Легенда и вердикт
            var legendCpu = new Label
            {
                Text = "━ CPU",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(20, 372),
                AutoSize = true
            };
            Controls.Add(legendCpu);

            var legendGpu = new Label
            {
                Text = "━ GPU",
                ForeColor = Color.FromArgb(90, 160, 240),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(90, 372),
                AutoSize = true
            };
            Controls.Add(legendGpu);

            var verdict = new Label
            {
                Text = BuildVerdict(),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 398),
                Size = new Size(600, 50)
            };
            Controls.Add(verdict);
        }

        private string BuildVerdict()
        {
            if (points.Count < 5)
            {
                return Lang.T("Данных пока мало - журнал пишется раз в минуту, пока программа работает. Загляни через пару часов.",
                              "Not much data yet - the log is written once a minute while the app runs. Check back in a couple of hours.");
            }

            int maxCpu = -1, maxGpu = -1;
            DateTime maxCpuAt = DateTime.MinValue, maxGpuAt = DateTime.MinValue;
            foreach (TempHistory.Point p in points)
            {
                if (p.CpuTemp > maxCpu) { maxCpu = p.CpuTemp; maxCpuAt = p.Time; }
                if (p.GpuTemp > maxGpu) { maxGpu = p.GpuTemp; maxGpuAt = p.Time; }
            }

            string text = "";
            if (maxCpu > 0)
                text += Lang.T("Пик CPU: ", "CPU peak: ") + maxCpu + "°C " + Lang.T("в ", "at ") + maxCpuAt.ToString("HH:mm") + ".  ";
            if (maxGpu > 0)
                text += Lang.T("Пик GPU: ", "GPU peak: ") + maxGpu + "°C " + Lang.T("в ", "at ") + maxGpuAt.ToString("HH:mm") + ".  ";

            int worst = Math.Max(maxCpu, maxGpu);
            if (worst >= 90) text += Lang.T("Пики опасно высокие - проверь охлаждение.", "Peaks are dangerously high - check your cooling.");
            else if (worst >= 80) text += Lang.T("Под нагрузкой жарковато, но терпимо.", "A bit hot under load, but tolerable.");
            else text += Lang.T("Температуры в полном порядке.", "Temperatures look perfectly fine.");

            return text;
        }
    }

    // График температур: две линии (CPU/GPU) на суточной шкале времени.
    // Дыры в данных (комп спал / программа не работала) не соединяем линией
    public class TempGraphControl : Control
    {
        private List<TempHistory.Point> points;

        public TempGraphControl(List<TempHistory.Point> data)
        {
            points = data;
            DoubleBuffered = true;
            BackColor = Theme.SidebarColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle plot = new Rectangle(38, 8, Width - 48, Height - 34);
            if (plot.Width <= 0 || plot.Height <= 0) return;

            const int minTemp = 20, maxTemp = 100;

            // Сетка: горизонтали каждые 20°, подписи слева
            using (var gridPen = new Pen(Theme.BorderColor))
            using (var labelFont = new Font("Segoe UI", 7F))
            {
                for (int t = minTemp; t <= maxTemp; t += 20)
                {
                    int y = plot.Bottom - (t - minTemp) * plot.Height / (maxTemp - minTemp);
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                    TextRenderer.DrawText(g, t + "°", labelFont, new Point(6, y - 7), Theme.TextDim, TextFormatFlags.NoPadding);
                }

                // Вертикали каждые 4 часа
                DateTime now = DateTime.Now;
                for (int h = 0; h <= 24; h += 4)
                {
                    DateTime mark = now.AddHours(-h);
                    int x = plot.Right - h * plot.Width / 24;
                    g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
                    string label = h == 0 ? Lang.T("сейчас", "now") : mark.ToString("HH:mm");
                    TextRenderer.DrawText(g, label, labelFont, new Point(x - 16, plot.Bottom + 4), Theme.TextDim, TextFormatFlags.NoPadding);
                }
            }

            if (points.Count < 2) return;

            DrawSeries(g, plot, minTemp, maxTemp, true, Theme.Accent);
            DrawSeries(g, plot, minTemp, maxTemp, false, Color.FromArgb(90, 160, 240));
        }

        private void DrawSeries(Graphics g, Rectangle plot, int minTemp, int maxTemp, bool cpu, Color color)
        {
            DateTime now = DateTime.Now;
            using (var pen = new Pen(color, 2))
            {
                PointF? prev = null;
                DateTime prevTime = DateTime.MinValue;

                foreach (TempHistory.Point p in points)
                {
                    int temp = cpu ? p.CpuTemp : p.GpuTemp;
                    if (temp <= 0) { prev = null; continue; }

                    double hoursAgo = (now - p.Time).TotalHours;
                    if (hoursAgo > 24) continue;

                    float x = plot.Right - (float)(hoursAgo * plot.Width / 24);
                    float y = plot.Bottom - (Math.Max(minTemp, Math.Min(maxTemp, temp)) - minTemp) * plot.Height / (float)(maxTemp - minTemp);
                    var pt = new PointF(x, y);

                    // Разрыв больше 5 минут - значит, комп спал или программа была закрыта
                    if (prev.HasValue && (p.Time - prevTime).TotalMinutes <= 5)
                    {
                        g.DrawLine(pen, prev.Value, pt);
                    }
                    prev = pt;
                    prevTime = p.Time;
                }
            }
        }
    }
}
