using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    // История FPS по конкретной игре: каждая записанная сессия - точка на графике.
    // Видно деградацию системы: если полгода назад CS выдавала 200 FPS, а теперь 140 -
    // линия наглядно ползёт вниз. Открывается по клику 📈 из окна Game Time
    public class FpsHistoryForm : Form
    {
        private class Session
        {
            public DateTime When;
            public int AvgFps;
            public int LowFps;
        }

        private readonly List<Session> sessions;
        private readonly string gameName;
        private Point dragStart;
        private bool draggingForm = false;

        public FpsHistoryForm(string game)
        {
            gameName = game;
            sessions = LoadSessions(game);

            Text = "DeepTools FPS History";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(560, 400);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

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
            titleBar.MouseDown += (s, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) => {
                if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = "📈 " + gameName + ".exe",
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

            // Вердикт тренда: сравниваем средний FPS первой и последней трети сессий
            var trendLbl = new Label
            {
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(18, 46),
                Size = new Size(524, 20)
            };
            Controls.Add(trendLbl);

            if (sessions.Count < 2)
            {
                trendLbl.Text = Lang.T("Мало сессий с замером FPS — поиграй ещё, график появится сам",
                                       "Not enough sessions with FPS data — play more, the chart will appear");
            }
            else
            {
                int third = Math.Max(1, sessions.Count / 3);
                double early = 0, late = 0;
                for (int i = 0; i < third; i++) early += sessions[i].AvgFps;
                for (int i = sessions.Count - third; i < sessions.Count; i++) late += sessions[i].AvgFps;
                early /= third;
                late /= third;

                double change = early > 0 ? (late - early) / early * 100.0 : 0;
                if (change <= -8)
                {
                    trendLbl.Text = Lang.T("⚠ FPS просел на ", "⚠ FPS dropped by ") + Math.Abs(change).ToString("0") + "%"
                        + Lang.T(" — возможна деградация системы (пыль, термопаста, фон)", " — possible system degradation (dust, paste, background apps)");
                    trendLbl.ForeColor = Theme.Warning;
                }
                else if (change >= 8)
                {
                    trendLbl.Text = Lang.T("FPS вырос на ", "FPS improved by ") + change.ToString("0") + "%" + Lang.T(" — твики работают 🎉", " — your tweaks are working 🎉");
                    trendLbl.ForeColor = Theme.Accent;
                }
                else
                {
                    trendLbl.Text = Lang.T("FPS стабилен — система в порядке", "FPS is stable — the system is fine");
                    trendLbl.ForeColor = Theme.Accent;
                }
            }

            var card = Theme.MakeCard(this, new Point(16, 74), new Size(528, 282));
            var graph = new FpsGraph(sessions) { Location = new Point(12, 12), Size = new Size(504, 258) };
            card.Controls.Add(graph);

            var legendLbl = new Label
            {
                Text = Lang.T("● средний FPS за сессию      ● 1% low", "● session avg FPS      ● 1% low"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 366),
                AutoSize = true
            };
            Controls.Add(legendLbl);

            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => Close();
            Controls.Add(esc);
            CancelButton = esc;
        }

        // Сессии игры с валидным FPS, по возрастанию даты
        private static List<Session> LoadSessions(string game)
        {
            var result = new List<Session>();
            try
            {
                if (File.Exists(GameSessionTracker.LogPath))
                {
                    string[] lines = File.ReadAllLines(GameSessionTracker.LogPath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] parts = lines[i].Split('|');
                        if (parts.Length < 7) continue;
                        if (!string.Equals(parts[0], game, StringComparison.OrdinalIgnoreCase)) continue;

                        DateTime when;
                        int fps, low;
                        if (!DateTime.TryParseExact(parts[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) continue;
                        if (!int.TryParse(parts[6], out fps) || fps <= 0) continue;
                        if (parts.Length < 8 || !int.TryParse(parts[7], out low)) low = -1;

                        result.Add(new Session { When = when, AvgFps = fps, LowFps = low });
                    }
                }
            }
            catch { }
            result.Sort((a, b) => a.When.CompareTo(b.When));
            return result;
        }

        // График: сессии слева направо по времени, средний FPS - акцентная линия,
        // 1% low - тонкая жёлтая
        private class FpsGraph : Panel
        {
            private readonly List<Session> data;

            public FpsGraph(List<Session> sessions)
            {
                data = sessions;
                DoubleBuffered = true;
                BackColor = Theme.SidebarColor;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                if (data.Count == 0)
                {
                    TextRenderer.DrawText(g, Lang.T("Нет данных", "No data"),
                        new Font("Segoe UI", 10F), ClientRectangle, Theme.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }

                int padL = 40, padR = 12, padT = 12, padB = 24;
                int plotW = Width - padL - padR;
                int plotH = Height - padT - padB;

                // Диапазон Y: от 0 (или чуть ниже минимума) до max с запасом
                int maxFps = 30;
                foreach (Session s in data) if (s.AvgFps > maxFps) maxFps = s.AvgFps;
                maxFps = (int)(maxFps * 1.15) + 5;

                // Сетка: 4 горизонтальные линии с подписями
                using (var gridPen = new Pen(Theme.BorderColor))
                using (var axisFont = new Font("Segoe UI", 7.5F))
                {
                    for (int i = 0; i <= 4; i++)
                    {
                        int y = padT + plotH * i / 4;
                        g.DrawLine(gridPen, padL, y, Width - padR, y);
                        int val = maxFps - maxFps * i / 4;
                        TextRenderer.DrawText(g, val.ToString(), axisFont,
                            new Rectangle(0, y - 8, padL - 6, 16), Theme.TextDim,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }

                    // Подписи дат: первая и последняя сессия
                    TextRenderer.DrawText(g, data[0].When.ToString("dd.MM"), axisFont,
                        new Point(padL, Height - padB + 6), Theme.TextDim, TextFormatFlags.NoPadding);
                    string lastDate = data[data.Count - 1].When.ToString("dd.MM");
                    Size sz = TextRenderer.MeasureText(lastDate, axisFont);
                    TextRenderer.DrawText(g, lastDate, axisFont,
                        new Point(Width - padR - sz.Width, Height - padB + 6), Theme.TextDim, TextFormatFlags.NoPadding);
                }

                // Координата точки i
                Func<int, int, System.Drawing.Point> pt = (i, fps) => new System.Drawing.Point(
                    padL + (data.Count == 1 ? plotW / 2 : plotW * i / (data.Count - 1)),
                    padT + plotH - (int)((float)fps / maxFps * plotH));

                // 1% low - тонкая линия под основной
                using (var lowPen = new Pen(Color.FromArgb(160, Theme.Warning), 1.5f))
                {
                    System.Drawing.Point? prev = null;
                    for (int i = 0; i < data.Count; i++)
                    {
                        if (data[i].LowFps <= 0) { prev = null; continue; }
                        var p = pt(i, data[i].LowFps);
                        if (prev.HasValue) g.DrawLine(lowPen, prev.Value, p);
                        prev = p;
                    }
                }

                // Средний FPS - основная линия с точками
                using (var pen = new Pen(Theme.Accent, 2f))
                using (var dot = new SolidBrush(Theme.Accent))
                {
                    for (int i = 0; i < data.Count; i++)
                    {
                        var p = pt(i, data[i].AvgFps);
                        if (i > 0) g.DrawLine(pen, pt(i - 1, data[i - 1].AvgFps), p);
                        g.FillEllipse(dot, p.X - 3, p.Y - 3, 6, 6);
                    }
                }
            }
        }
    }
}
