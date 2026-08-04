using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // 🏆 Пасхалка в честь победы WW team на турнире по CS. GG WP!
    public class WwEasterEggForm : Form
    {
        private readonly Timer confettiTimer;
        private readonly List<Confetto> confetti = new List<Confetto>();
        private int ticks = 0;

        private class Confetto
        {
            public float X, Y, Vy, Sway, Phase, Size;
            public Color Color;
        }

        public WwEasterEggForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(430, 310);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            TopMost = true;

            Load += (s, e) =>
            {
                var path = new GraphicsPath();
                int r = 14, d = r * 2;
                var rect = new Rectangle(0, 0, Width, Height);
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            };

            var cup = new Label
            {
                Text = "🏆",
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 34F),
                Location = new Point((430 - 70) / 2, 26),
                Size = new Size(70, 62),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(cup);

            var title = new Label
            {
                Text = "WW TEAM",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                Location = new Point(0, 96),
                Size = new Size(430, 44),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = Lang.T("ЧЕМПИОНЫ ТУРНИРА ПО CS", "CS TOURNAMENT CHAMPIONS"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(0, 142),
                Size = new Size(430, 24),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(subtitle);

            var gg = new Label
            {
                Text = Lang.T("Разнесли всех. GG WP 🎉", "Wiped everyone. GG WP 🎉"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(0, 172),
                Size = new Size(430, 20),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(gg);

            var okBtn = new RoundedButton
            {
                Text = Lang.T("Уважение 🫡", "Respect 🫡"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Color.FromArgb(10, 20, 15),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(150, 36),
                Location = new Point((430 - 150) / 2, 226)
            };
            okBtn.Click += (s, e) => Close();
            Controls.Add(okBtn);

            // Конфетти в цветах CT и T, куда без них
            var rnd = new Random();
            Color[] palette = {
                Theme.Accent,
                Color.FromArgb(94, 152, 217),   // CT-синий
                Color.FromArgb(222, 155, 53),   // T-жёлтый
                Color.White
            };
            for (int i = 0; i < 90; i++)
            {
                confetti.Add(new Confetto
                {
                    X = rnd.Next(10, 420),
                    Y = -rnd.Next(0, 320),
                    Vy = 1.8f + (float)rnd.NextDouble() * 2.6f,
                    Sway = 0.6f + (float)rnd.NextDouble() * 1.5f,
                    Phase = (float)rnd.NextDouble() * 6.28f,
                    Size = 4 + rnd.Next(4),
                    Color = palette[rnd.Next(palette.Length)]
                });
            }
            confettiTimer = new Timer { Interval = 33 };
            confettiTimer.Tick += (s, e) =>
            {
                ticks++;
                foreach (Confetto c in confetti)
                {
                    c.Y += c.Vy;
                    c.X += (float)Math.Sin(c.Phase + ticks * 0.08f) * c.Sway;
                    // Пусть сыпется, пока окно открыто - праздник же
                    if (c.Y > Height + 10) { c.Y = -10; c.X = rnd.Next(10, 420); }
                }
                Invalidate();
            };
            confettiTimer.Start();
            FormClosed += (s, e) => confettiTimer.Dispose();

            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => Close();
            Controls.Add(esc);
            CancelButton = esc;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Confetto c in confetti)
            {
                if (c.Y < -10 || c.Y > Height + 10) continue;
                using (var b = new SolidBrush(Color.FromArgb(200, c.Color)))
                    e.Graphics.FillEllipse(b, c.X, c.Y, c.Size, c.Size);
            }
            using (var pen = new Pen(Theme.Accent))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
