using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Виджет мониторинга на рабочий стол: компактная плашка CPU/RAM/температуры,
    // всегда поверх окон, перетаскивается мышью, позиция запоминается.
    // Данные берёт из SystemStats (обновляет HealthCheckPanel)
    public class WidgetForm : Form
    {
        private System.Windows.Forms.Timer refreshTimer;

        public WidgetForm()
        {
            Text = "DeepTools Widget";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(190, 96);
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            TopMost = true;

            // Восстановление позиции (по умолчанию - правый верх)
            int x, y;
            int.TryParse(AppConfig.Get("widget_x", "-1"), out x);
            int.TryParse(AppConfig.Get("widget_y", "-1"), out y);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (x < wa.Left || x > wa.Right - 50 || y < wa.Top || y > wa.Bottom - 50)
            {
                x = wa.Right - Width - 12;
                y = wa.Top + 12;
            }
            Location = new Point(x, y);

            // Виджет таскается целиком. Точка захвата считается от курсора, иначе
            // окно «догоняет» мышь рывками; позиция сохраняется по отпусканию
            WindowDrag.Attach(this, this, () => {
                AppConfig.Set("widget_x", Location.X.ToString());
                AppConfig.Set("widget_y", Location.Y.ToString());
            });

            Load += (s, e) => ApplyRoundedRegion();

            refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            refreshTimer.Tick += (s, e) => Invalidate();
            refreshTimer.Start();
        }

        // Виджет не должен красть фокус у игр и окон
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        private void ApplyRoundedRegion()
        {
            var path = new GraphicsPath();
            int r = 10, d = r * 2;
            var rect = new Rectangle(0, 0, Width, Height);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bgRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(bgRect, 10))
            using (var bgBrush = new SolidBrush(Theme.SidebarColor))
            using (var borderPen = new Pen(Theme.BorderColor))
            {
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);
            }

            using (var dotBrush = new SolidBrush(Theme.Accent))
            {
                g.FillEllipse(dotBrush, 10, 10, 8, 8);
            }
            using (var titleFont = new Font("Segoe UI", 8F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "DeepTools", titleFont, new Point(24, 6), Theme.TextDim, TextFormatFlags.NoPadding);
            }

            using (var labelFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (var valueFont = new Font("Segoe UI", 8.5F))
            {
                TextRenderer.DrawText(g, "CPU", labelFont, new Point(12, 28), Theme.TextDim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, SystemStats.CpuLoad + "  " + SystemStats.CpuTemp, valueFont, new Point(52, 28), Theme.TextMain, TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, "GPU", labelFont, new Point(12, 48), Theme.TextDim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, SystemStats.GpuLoad + "  " + SystemStats.GpuTemp, valueFont, new Point(52, 48), Theme.TextMain, TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, "RAM", labelFont, new Point(12, 68), Theme.TextDim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, SystemStats.RamLoad, valueFont, new Point(52, 68), Theme.TextMain, TextFormatFlags.NoPadding);
            }
        }
    }
}
