using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Отчёт по кадрам за последние 30 минут / час / 2 часа: средний FPS, худший
    // 1% low, и главное - микрофризы. Данные копит FrameLog, пока GameBooster
    // видит игру, поэтому окно можно держать открытым и смотреть на живой график
    public class GameStatsForm : Form
    {
        private static readonly int[] RangeMinutes = { 30, 60, 120 };

        private int rangeIndex = 1;   // по умолчанию час
        private Panel dragBar;
        private Point dragStart;
        private bool dragging;

        private RoundedButton[] rangeBtns;
        private Label periodLabel;
        private Label[] valueLabels;
        private Label[] captionLabels;
        private Label emptyLabel;
        private FrameTimeline chart;
        private Panel stutterCard;
        private Timer liveTimer;

        public GameStatsForm()
        {
            Text = "DeepTools Frame Stats";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(620, 540);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => { ApplyRoundedRegion(); RefreshReport(); };

            // Пока окно открыто, отчёт обновляется сам - в игре это удобно
            liveTimer = new Timer { Interval = 3000 };
            liveTimer.Tick += (s, e) => RefreshReport();
            liveTimer.Start();
            FormClosed += (s, e) => liveTimer.Stop();
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
            dragBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.BgColor };
            dragBar.MouseDown += (s, e) => { dragging = true; dragStart = new Point(e.X, e.Y); };
            dragBar.MouseMove += (s, e) =>
            {
                if (dragging) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            dragBar.MouseUp += (s, e) => { dragging = false; };
            Controls.Add(dragBar);

            var titleLbl = new Label
            {
                Text = Lang.T("▤ Статистика кадров", "▤ Frame statistics"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            };
            dragBar.Controls.Add(titleLbl);

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
            dragBar.Controls.Add(closeBtn);

            // Переключатель окна времени
            rangeBtns = new RoundedButton[RangeMinutes.Length];
            string[] captions = {
                Lang.T("30 минут", "30 minutes"),
                Lang.T("1 час", "1 hour"),
                Lang.T("2 часа", "2 hours")
            };
            for (int i = 0; i < RangeMinutes.Length; i++)
            {
                int idx = i;
                var btn = new RoundedButton
                {
                    Text = captions[i],
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    CornerRadius = 10,
                    Size = new Size(112, 30),
                    Location = new Point(18 + i * 120, 48)
                };
                btn.Click += (s, e) => { rangeIndex = idx; UpdateRangeButtons(); RefreshReport(); };
                Controls.Add(btn);
                rangeBtns[i] = btn;
            }
            UpdateRangeButtons();

            periodLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(390, 54),
                Size = new Size(214, 18),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };
            Controls.Add(periodLabel);

            // Сводка: четыре числа
            var summary = Theme.MakeCard(this, new Point(16, 88), new Size(588, 98));
            string[] caps = {
                Lang.T("средний FPS", "average FPS"),
                Lang.T("худший 1% low", "worst 1% low"),
                Lang.T("микрофризов", "micro-stutters"),
                Lang.T("худший фриз", "worst stutter")
            };
            valueLabels = new Label[caps.Length];
            captionLabels = new Label[caps.Length];
            for (int i = 0; i < caps.Length; i++)
            {
                var val = new Label
                {
                    Text = "—",
                    ForeColor = Theme.Accent,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                    Location = new Point(18 + i * 144, 12),
                    Size = new Size(134, 40),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                summary.Controls.Add(val);
                valueLabels[i] = val;

                var cap = new Label
                {
                    Text = caps[i],
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(18 + i * 144, 58),
                    Size = new Size(134, 18),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                summary.Controls.Add(cap);
                captionLabels[i] = cap;
            }

            emptyLabel = new Label
            {
                Text = Lang.T("Пока нет данных: статистика копится, пока GameBooster видит игру",
                              "No data yet: statistics are collected while GameBooster sees a game"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(18, 30),
                Size = new Size(552, 60),
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            summary.Controls.Add(emptyLabel);

            // График
            var chartCard = Theme.MakeCard(this, new Point(16, 194), new Size(588, 190));
            var chartTitle = new Label
            {
                Text = Lang.T("Кадры по времени (красные - с микрофризами)", "Frames over time (red = micro-stutters)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(14, 8),
                AutoSize = true
            };
            chartCard.Controls.Add(chartTitle);

            chart = new FrameTimeline
            {
                Location = new Point(14, 30),
                Size = new Size(560, 146)
            };
            chartCard.Controls.Add(chart);

            // Худшие фризы
            stutterCard = Theme.MakeCard(this, new Point(16, 392), new Size(588, 132));
            var stTitle = new Label
            {
                Text = Lang.T("Самые долгие микрофризы", "Longest micro-stutters"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(14, 8),
                AutoSize = true
            };
            stutterCard.Controls.Add(stTitle);
        }

        private void UpdateRangeButtons()
        {
            for (int i = 0; i < rangeBtns.Length; i++)
            {
                bool active = i == rangeIndex;
                rangeBtns[i].ButtonColor = active ? Theme.Accent : Theme.KeyColor;
                rangeBtns[i].HoverColor = active ? Theme.AccentHover : Theme.KeyHover;
                rangeBtns[i].TextColor = active ? Theme.BgColor : Theme.TextMain;
                rangeBtns[i].Invalidate();
            }
        }

        private void RefreshReport()
        {
            FrameLog.Report r = FrameLog.Build(TimeSpan.FromMinutes(RangeMinutes[rangeIndex]));

            bool hasData = r.Samples > 0;
            for (int i = 0; i < valueLabels.Length; i++)
                valueLabels[i].Visible = hasData;
            for (int i = 0; i < captionLabels.Length; i++)
                captionLabels[i].Visible = hasData;
            emptyLabel.Visible = !hasData;

            if (hasData)
            {
                valueLabels[0].Text = r.AvgFps > 0 ? r.AvgFps.ToString() : "—";
                valueLabels[1].Text = r.LowFps > 0 ? r.LowFps.ToString() : "—";
                valueLabels[2].Text = r.Stutters.ToString();

                if (r.WorstMs > 0)
                {
                    valueLabels[3].Text = Math.Round(r.WorstMs) + Lang.T(" мс", " ms");
                    valueLabels[3].ForeColor = r.WorstMs >= 100 ? Theme.Off
                        : r.WorstMs >= 50 ? Theme.Warning : Theme.On;
                }
                else
                {
                    valueLabels[3].Text = Lang.T("нет", "none");
                    valueLabels[3].ForeColor = Theme.On;
                }
            }

            // Подпись: за какой период данные и сколько их реально. Строку держим
            // короткой (в одну строку), подробности - в тултипе
            string rangeText = RangeMinutes[rangeIndex] >= 60
                ? (RangeMinutes[rangeIndex] / 60) + Lang.T(" ч", " h")
                : RangeMinutes[rangeIndex] + Lang.T(" мин", " min");
            string shortText = Lang.T("за ", "for ") + rangeText
                + (r.Games > 0 ? Lang.T(" · игр: ", " · games: ") + r.Games : "");

            string fullText = shortText;
            if (r.DataFrom != default(DateTime))
            {
                shortText += Lang.T(" · с ", " · since ") + r.DataFrom.ToString("HH:mm");
                fullText = shortText;
                if (r.DataFrom > r.From)
                    fullText += Lang.T(" (DeepTools запущен позже начала периода)",
                                       " (DeepTools started after the period began)");
            }
            periodLabel.Text = shortText;
            DarkTip.Set(periodLabel, fullText);

            chart.SetData(r.Timeline, r.From, r.To);

            // Список худших фризов: сначала чистим прежние строки
            for (int i = stutterCard.Controls.Count - 1; i >= 0; i--)
            {
                Control c = stutterCard.Controls[i];
                if (c is Label && c.Tag != null && (string)c.Tag == "stutter") stutterCard.Controls.RemoveAt(i);
            }

            if (r.Worst.Count == 0)
            {
                stutterCard.Controls.Add(new Label
                {
                    Text = hasData
                        ? Lang.T("Микрофризов за это время не было", "No micro-stutters in this period")
                        : Lang.T("Нет данных", "No data"),
                    Tag = "stutter",
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(16, 34),
                    AutoSize = true
                });
            }
            else
            {
                int shown = Math.Min(5, r.Worst.Count);
                for (int i = 0; i < shown; i++)
                {
                    FrameLog.Stutter st = r.Worst[i];
                    Color color = st.Ms >= 100 ? Theme.Off : st.Ms >= 50 ? Theme.Warning : Theme.TextMain;
                    var row = new Label
                    {
                        Text = st.Time.ToString("HH:mm:ss") + "   "
                            + Math.Round(st.Ms) + Lang.T(" мс", " ms") + "   "
                            + (st.Game == "" ? "" : st.Game + ".exe"),
                        Tag = "stutter",
                        ForeColor = color,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                        Location = new Point(16, 32 + i * 18),
                        Size = new Size(552, 18),
                        AutoEllipsis = true
                    };
                    stutterCard.Controls.Add(row);
                }
                if (r.Worst.Count > shown)
                {
                    stutterCard.Controls.Add(new Label
                    {
                        Text = Lang.T("…и ещё ", "…and ") + (r.Worst.Count - shown)
                            + Lang.T(" за это время", " more in this period"),
                        Tag = "stutter",
                        ForeColor = Theme.TextDim,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 8F),
                        Location = new Point(300, 32 + shown * 18),
                        AutoSize = true
                    });
                }
            }
        }

        // График кадров по времени окна: столбик = средний FPS в своём интервале,
        // красные - там, где были микрофризы. Временная шкала честная: окно в два
        // часа и десять минут данных - это разные вещи, и пустое место слева
        // означает, что DeepTools тогда ещё не работал
        private class FrameTimeline : Control
        {
            private List<FrameLog.Sample> data = new List<FrameLog.Sample>();
            private DateTime from, to;

            public FrameTimeline()
            {
                DoubleBuffered = true;
                BackColor = Theme.SidebarColor;
            }

            public void SetData(List<FrameLog.Sample> samples, DateTime winFrom, DateTime winTo)
            {
                data = samples ?? new List<FrameLog.Sample>();
                from = winFrom;
                to = winTo;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.None;

                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var pen = new Pen(Theme.BorderColor)) g.DrawRectangle(pen, rect);

                int left = 26, top = 4, bottom = rect.Height - 14;
                int usableH = bottom - top;
                int usableW = rect.Width - left - 6;
                if (usableW <= 0 || usableH <= 0) return;

                // Столбики по интервалам времени, а не «по количеству замеров»
                double span = Math.Max(1.0, (to - from).TotalSeconds);
                int buckets = usableW / 2;
                if (buckets < 20) buckets = 20;
                if (buckets > 240) buckets = 240;

                var fpsWeighted = new double[buckets];
                var frames = new int[buckets];
                var stubMs = new float[buckets];
                var hasGap = new bool[buckets];
                double maxFps = 0;

                for (int i = 0; i < data.Count; i++)
                {
                    FrameLog.Sample s = data[i];
                    double rel = (s.Time - from).TotalSeconds / span;
                    if (rel < 0) continue;
                    int b = (int)(rel * buckets);
                    if (b >= buckets) b = buckets - 1;
                    if (s.Gap) { hasGap[b] = true; continue; }
                    int f = s.Frames > 0 ? s.Frames : 1;
                    fpsWeighted[b] += (double)s.Fps * f;
                    frames[b] += f;
                    if (s.Stutters > 0 && s.WorstMs > stubMs[b]) stubMs[b] = s.WorstMs;
                    if (s.Fps > maxFps) maxFps = s.Fps;
                }

                int scale = (int)(Math.Ceiling(Math.Max(30.0, maxFps) / 30.0) * 30);

                using (var grid = new Pen(Theme.BorderColor))
                {
                    for (int i = 1; i < 4; i++)
                    {
                        int y = rect.Height - rect.Height * i / 4;
                        g.DrawLine(grid, left - 2, y, rect.Width - 4, y);
                    }
                }
                using (var lbl = new Font("Segoe UI", 7F))
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    g.DrawString(scale.ToString(), lbl, brush, 2, 2);
                    g.DrawString((scale / 2).ToString(), lbl, brush, 2, rect.Height / 2 - 6);
                }

                if (data.Count == 0)
                {
                    using (var brush = new SolidBrush(Theme.TextDim))
                    using (var f = new Font("Segoe UI", 9F))
                        g.DrawString(Lang.T("нет данных за этот период", "no data for this period"), f, brush, left + 8, top + usableH / 2);
                    return;
                }

                using (var baseline = new Pen(Theme.BorderColor))
                    g.DrawLine(baseline, left, bottom, left + usableW, bottom);

                float step = (float)usableW / buckets;
                float barW = Math.Max(1f, step - 1f);
                for (int b = 0; b < buckets; b++)
                {
                    float x = left + b * step;
                    if (hasGap[b] && frames[b] == 0)
                    {
                        using (var gapBrush = new SolidBrush(Theme.BorderColor))
                            g.FillRectangle(gapBrush, x, bottom - 3, barW, 3);
                        continue;
                    }
                    if (frames[b] == 0) continue; // в этом интервале данных нет

                    double avg = fpsWeighted[b] / frames[b];
                    int h = (int)(usableH * Math.Min(1.0, scale > 0 ? avg / scale : 0));
                    if (h < 1) h = 1;
                    bool stutter = stubMs[b] > 0;
                    using (var brush = new SolidBrush(stutter ? Theme.Off : Theme.On))
                        g.FillRectangle(brush, x, bottom - h, barW, h);

                    // Микрофриз: вертикальная линия на всю высоту, иначе на графике
                    // он теряется среди ровных кадров
                    if (stutter)
                    {
                        using (var spike = new Pen(Theme.Off))
                            g.DrawLine(spike, x + barW / 2f, top, x + barW / 2f, bottom);
                    }
                }

                // Подписи времени по краям
                using (var f = new Font("Segoe UI", 7F))
                using (var brush = new SolidBrush(Theme.TextDim))
                {
                    g.DrawString(from.ToString("HH:mm"), f, brush, left, rect.Height - 12);
                    string right = to.ToString("HH:mm");
                    SizeF sz = g.MeasureString(right, f);
                    g.DrawString(right, f, brush, rect.Width - sz.Width - 4, rect.Height - 12);
                }
            }
        }
    }
}
