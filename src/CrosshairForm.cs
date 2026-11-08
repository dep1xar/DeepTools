using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // Накладной прицел: маленькое прозрачное окно точно по центру экрана,
    // мышь проходит сквозь (WS_EX_TRANSPARENT), фокус не крадёт.
    // Формы: крест, точка, круг, T-образный
    public class CrosshairForm : Form
    {
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public const int SIZE = 64;

        private string shape;
        private Color color;
        private int scale;

        public CrosshairForm(string shapeName, Color crossColor, int crossScale)
        {
            shape = shapeName;
            color = crossColor;
            scale = Math.Max(1, Math.Min(3, crossScale));

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(SIZE, SIZE);
            DoubleBuffered = true;

            // Прозрачный фон окна: всё цвета Magenta не рисуется
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Location = new Point(screen.Width / 2 - SIZE / 2, screen.Height / 2 - SIZE / 2);

            Load += (s, e) => {
                int style = GetWindowLong(Handle, GWL_EXSTYLE);
                SetWindowLong(Handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            // Без сглаживания: полупрозрачные пиксели на краях смешиваются с Magenta-фоном
            // и перестают вырезаться TransparencyKey - получается розовая кайма
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            int cx = SIZE / 2;
            int cy = SIZE / 2;
            int len = 8 * scale;      // длина луча креста
            int gap = 3 * scale;      // зазор в центре
            float thick = 2f * scale; // толщина линий

            using (var pen = new Pen(color, thick))
            using (var outline = new Pen(Color.Black, thick + 2))
            {
                if (shape == "dot")
                {
                    int r = 2 * scale;
                    using (var ob = new SolidBrush(Color.Black))
                        g.FillEllipse(ob, cx - r - 1, cy - r - 1, r * 2 + 2, r * 2 + 2);
                    using (var b = new SolidBrush(color))
                        g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                }
                else if (shape == "circle")
                {
                    int r = 6 * scale;
                    g.DrawEllipse(outline, cx - r, cy - r, r * 2, r * 2);
                    g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                    int dr = scale;
                    using (var b = new SolidBrush(color))
                        g.FillEllipse(b, cx - dr, cy - dr, dr * 2, dr * 2);
                }
                else if (shape == "tshape")
                {
                    // T-образный: лучи влево, вправо и вниз (без верхнего)
                    g.DrawLine(outline, cx - gap - len, cy, cx - gap, cy);
                    g.DrawLine(outline, cx + gap, cy, cx + gap + len, cy);
                    g.DrawLine(outline, cx, cy + gap, cx, cy + gap + len);
                    g.DrawLine(pen, cx - gap - len, cy, cx - gap, cy);
                    g.DrawLine(pen, cx + gap, cy, cx + gap + len, cy);
                    g.DrawLine(pen, cx, cy + gap, cx, cy + gap + len);
                }
                else // "cross" по умолчанию
                {
                    g.DrawLine(outline, cx - gap - len, cy, cx - gap, cy);
                    g.DrawLine(outline, cx + gap, cy, cx + gap + len, cy);
                    g.DrawLine(outline, cx, cy - gap - len, cx, cy - gap);
                    g.DrawLine(outline, cx, cy + gap, cx, cy + gap + len);
                    g.DrawLine(pen, cx - gap - len, cy, cx - gap, cy);
                    g.DrawLine(pen, cx + gap, cy, cx + gap + len, cy);
                    g.DrawLine(pen, cx, cy - gap - len, cx, cy - gap);
                    g.DrawLine(pen, cx, cy + gap, cx, cy + gap + len);
                }
            }
        }
    }
}
