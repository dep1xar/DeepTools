using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    public class SidebarNavButton : Label
    {
        private bool _active;

        public bool Active
        {
            get { return _active; }
            set
            {
                _active = value;
                UpdateAppearance();
            }
        }

        public SidebarNavButton(string text)
        {
            Text = "  " + text;
            AutoSize = false;
            Size = new Size(190, 38);
            TextAlign = ContentAlignment.MiddleLeft;
            Font = new Font("Segoe UI", 10F);
            BackColor = Color.Transparent;
            ForeColor = Theme.TextDim;
            Cursor = Cursors.Hand;
            Padding = new Padding(10, 0, 0, 0);
            Margin = new Padding(0, 4, 0, 0);
            DoubleBuffered = true;

            MouseEnter += (s, e) => { if (!_active) { ForeColor = Theme.TextMain; BackColor = Theme.NavHoverBg; } };
            MouseLeave += (s, e) => { if (!_active) { ForeColor = Theme.TextDim; BackColor = Color.Transparent; } };
        }

        public void SetActive(bool active)
        {
            Active = active;
        }

        private void UpdateAppearance()
        {
            BackColor = _active ? Theme.NavActiveBg : Color.Transparent;
            ForeColor = _active ? Theme.Accent : Theme.TextDim;
        }
    }

    // Базовый класс для кастомных контролов на прозрачном фоне.
    // Вместо WS_EX_TRANSPARENT (даёт артефакты на скруглённых краях при перерисовке)
    // контрол сам "допрашивает" родителя нарисовать то, что должно быть видно за ним,
    // и только потом рисует себя поверх - это надёжнее и не мажет углы.
    public abstract class TransparentControl : Control
    {
        protected TransparentControl()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                      ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Parent != null)
            {
                e.Graphics.TranslateTransform(-Left, -Top);
                PaintEventArgs pe = new PaintEventArgs(e.Graphics, Parent.ClientRectangle);
                try
                {
                    InvokePaintBackground(Parent, pe);
                    InvokePaint(Parent, pe);
                }
                finally
                {
                    e.Graphics.TranslateTransform(Left, Top);
                }
            }
            else
            {
                base.OnPaintBackground(e);
            }
        }
    }

    // Тумблер-переключатель с плавной анимацией пимпочки и цвета
    public class ToggleSwitch : TransparentControl
    {
        private bool _checked = false;
        public event EventHandler CheckedChanged;

        private readonly System.Windows.Forms.Timer animTimer;
        private float anim = 0f; // 0 = выкл, 1 = вкл (визуальное положение)

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                // При программной установке до показа - без анимации, чтобы
                // восстановленные состояния не «ехали» на старте
                if (IsHandleCreated) StartAnim();
                else { anim = value ? 1f : 0f; Invalidate(); }
            }
        }

        public ToggleSwitch()
        {
            Size = new Size(46, 24);
            Cursor = Cursors.Hand;
            anim = _checked ? 1f : 0f;
            animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            animTimer.Tick += (s, e) => AnimStep();
        }

        private void StartAnim()
        {
            if (!animTimer.Enabled) animTimer.Start();
        }

        private void AnimStep()
        {
            float target = _checked ? 1f : 0f;
            float d = target - anim;
            if (Math.Abs(d) < 0.03f) { anim = target; animTimer.Stop(); }
            else anim += d * 0.35f;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color track = Theme.Lerp(Theme.KeyHover, Theme.Accent, anim);

            using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
            using (var brush = new SolidBrush(track))
            {
                g.FillPath(brush, path);
            }

            int d = Height - 6;
            int xOff = 3, xOn = Width - d - 3;
            int x = xOff + (int)((xOn - xOff) * anim);
            using (var thumb = new SolidBrush(Color.White))
            {
                g.FillEllipse(thumb, x, 3, d, d);
            }
        }

        private GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // Слайдер
    public class NeonSlider : TransparentControl
    {
        public int Minimum = 0;
        public int Maximum = 100;
        private int _value = 50;
        public int Value
        {
            get { return _value; }
            set { _value = Math.Max(Minimum, Math.Min(Maximum, value)); Invalidate(); }
        }
        public event EventHandler ValueChanged;
        private bool dragging = false;

        public NeonSlider()
        {
            Height = 24;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            dragging = true;
            UpdateFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) UpdateFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
        }

        private void UpdateFromMouse(int x)
        {
            int pad = 8;
            float ratio = (float)(x - pad) / (Width - pad * 2);
            ratio = Math.Max(0, Math.Min(1, ratio));
            Value = Minimum + (int)Math.Round(ratio * (Maximum - Minimum));
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = 8;
            int trackY = Height / 2;

            using (var trackPen = new Pen(Theme.KeyHover, 4))
            {
                g.DrawLine(trackPen, pad, trackY, Width - pad, trackY);
            }

            float ratio = (float)(Value - Minimum) / (Maximum - Minimum);
            int fillX = pad + (int)(ratio * (Width - pad * 2));

            using (var fillPen = new Pen(Theme.Accent, 4))
            {
                g.DrawLine(fillPen, pad, trackY, fillX, trackY);
            }

            using (var thumbBrush = new SolidBrush(Color.White))
            using (var thumbBorder = new Pen(Theme.Accent, 2))
            {
                g.FillEllipse(thumbBrush, fillX - 7, trackY - 7, 14, 14);
                g.DrawEllipse(thumbBorder, fillX - 7, trackY - 7, 14, 14);
            }
        }
    }

    // Общая палитра, чтобы не дублировать цвета в каждом файле.
    // Цвета не readonly: Apply() переключает тёмную/светлую тему до построения UI
    public static class Theme
    {
        public static Color BgColor = Color.FromArgb(15, 20, 31);
        public static Color CardColor = Color.FromArgb(22, 28, 43);
        public static Color SidebarColor = Color.FromArgb(18, 23, 36);
        public static Color BorderColor = Color.FromArgb(38, 46, 64);
        public static Color Accent = Color.FromArgb(46, 214, 140);
        public static Color Danger = Color.FromArgb(230, 90, 90);
        public static Color Warning = Color.FromArgb(240, 180, 70);
        public static Color TextMain = Color.FromArgb(232, 236, 243);
        public static Color TextDim = Color.FromArgb(138, 147, 166);
        public static Color InputColor = Color.FromArgb(30, 37, 54);
        public static Color KeyColor = Color.FromArgb(40, 50, 70);
        public static Color KeyHover = Color.FromArgb(50, 65, 90);
        public static Color NavActiveBg = Color.FromArgb(34, 42, 60);
        public static Color NavHoverBg = Color.FromArgb(27, 34, 49);
        public static Color AccentHover = Color.FromArgb(70, 240, 160);
        public static Color DangerHover = Color.FromArgb(220, 50, 50);

        // Текущая тема: нужно контролам, которые красятся системно (скроллбары, комбобоксы)
        public static bool IsLight = false;

        public static void Apply(bool light)
        {
            IsLight = light;
            if (light)
            {
                BgColor = Color.FromArgb(243, 245, 249);
                CardColor = Color.White;
                SidebarColor = Color.FromArgb(232, 236, 243);
                BorderColor = Color.FromArgb(208, 214, 226);
                Accent = Color.FromArgb(20, 165, 105);
                Danger = Color.FromArgb(215, 70, 70);
                Warning = Color.FromArgb(200, 140, 30);
                TextMain = Color.FromArgb(28, 34, 48);
                TextDim = Color.FromArgb(105, 115, 135);
                InputColor = Color.FromArgb(236, 239, 245);
                KeyColor = Color.FromArgb(222, 227, 236);
                KeyHover = Color.FromArgb(205, 212, 225);
                NavActiveBg = Color.FromArgb(216, 224, 236);
                NavHoverBg = Color.FromArgb(228, 233, 242);
                AccentHover = Color.FromArgb(26, 190, 122);
                DangerHover = Color.FromArgb(235, 90, 90);
            }
            else
            {
                BgColor = Color.FromArgb(15, 20, 31);
                CardColor = Color.FromArgb(22, 28, 43);
                SidebarColor = Color.FromArgb(18, 23, 36);
                BorderColor = Color.FromArgb(38, 46, 64);
                Accent = Color.FromArgb(46, 214, 140);
                Danger = Color.FromArgb(230, 90, 90);
                Warning = Color.FromArgb(240, 180, 70);
                TextMain = Color.FromArgb(232, 236, 243);
                TextDim = Color.FromArgb(138, 147, 166);
                InputColor = Color.FromArgb(30, 37, 54);
                KeyColor = Color.FromArgb(40, 50, 70);
                KeyHover = Color.FromArgb(50, 65, 90);
                NavActiveBg = Color.FromArgb(34, 42, 60);
                NavHoverBg = Color.FromArgb(27, 34, 49);
                AccentHover = Color.FromArgb(70, 240, 160);
                DangerHover = Color.FromArgb(220, 50, 50);
            }
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Плавное смешивание двух цветов (t: 0 = a, 1 = b) - для анимаций
        public static Color Lerp(Color a, Color b, float t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            int r = (int)(a.R + (b.R - a.R) * t);
            int g = (int)(a.G + (b.G - a.G) * t);
            int bl = (int)(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }

        public static Panel MakeCard(Control parent, Point loc, Size size)
        {
            var card = new Panel { Location = loc, Size = size, BackColor = CardColor };
            card.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using (var path = RoundedRect(rect, 10))
                using (var brush = new SolidBrush(CardColor))
                using (var pen = new Pen(BorderColor))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                // Тонкий блик по верхней кромке - карточка кажется приподнятой
                using (var hi = new Pen(Color.FromArgb(28, 255, 255, 255)))
                {
                    e.Graphics.DrawLine(hi, 11, 1, card.Width - 12, 1);
                }
            };
            parent.Controls.Add(card);
            return card;
        }
    }

    public class RoundedButton : TransparentControl
    {
        private bool isHovering = false;
        private bool isPressed = false;
        public Color ButtonColor = Theme.Accent;
        public Color HoverColor = Color.FromArgb(70, 240, 160);
        public Color TextColor = Color.Black;
        public int CornerRadius = 12;

        private readonly System.Windows.Forms.Timer hoverTimer;
        private float hoverAnim = 0f; // 0 = обычный, 1 = наведён

        public RoundedButton()
        {
            Size = new Size(120, 36);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            hoverTimer = new System.Windows.Forms.Timer { Interval = 15 };
            hoverTimer.Tick += (s, e) => {
                float target = isHovering ? 1f : 0f;
                float d = target - hoverAnim;
                if (Math.Abs(d) < 0.05f) { hoverAnim = target; hoverTimer.Stop(); }
                else hoverAnim += d * 0.4f;
                Invalidate();
            };
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            isHovering = true;
            if (!hoverTimer.Enabled) hoverTimer.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            isHovering = false;
            isPressed = false;
            if (!hoverTimer.Enabled) hoverTimer.Start();
        }

        // Тактильный отклик: при нажатии кнопка чуть темнеет
        protected override void OnMouseDown(MouseEventArgs e)
        {
            isPressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            isPressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color currentColor = Theme.Lerp(ButtonColor, HoverColor, hoverAnim);
            if (isPressed) currentColor = Theme.Lerp(currentColor, Color.Black, 0.18f);

            // Радиус не больше половины высоты, иначе углы ломаются на маленьких кнопках
            int radius = Math.Min(CornerRadius, (Height - 1) / 2);
            using (var path = Theme.RoundedRect(rect, radius))
            using (var brush = new SolidBrush(currentColor))
            {
                e.Graphics.FillPath(brush, path);
            }

            // TextRenderer вместо Graphics.DrawString: даёт ClearType-сглаживание как у
            // обычных Label (DrawString рисует "пиксельно") и умеет подставлять символы
            // из других шрифтов, когда в основном их нет
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width, Height), TextColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Тултип в стиле приложения: системный ToolTip рисует белую плашку,
    // которая выбивается из тёмной темы. Один хелпер - единый вид везде
    public static class DarkTip
    {
        public static void Set(Control control, string text)
        {
            var tip = new ToolTip { OwnerDraw = true };
            var font = new Font("Segoe UI", 8.5F);

            tip.Popup += (s, e) => {
                Size sz = TextRenderer.MeasureText(tip.GetToolTip(e.AssociatedControl), font);
                e.ToolTipSize = new Size(sz.Width + 16, sz.Height + 10);
            };
            tip.Draw += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                using (var bg = new SolidBrush(Theme.InputColor))
                    e.Graphics.FillRectangle(bg, e.Bounds);
                using (var border = new Pen(Theme.BorderColor))
                    e.Graphics.DrawRectangle(border, rect);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, font, e.Bounds, Theme.TextMain,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };

            tip.SetToolTip(control, text);
        }
    }

    // Строка списка с подсветкой при наведении - как пункты меню трея.
    // Дочерние Label перехватывают мышь, поэтому наведение слушаем и у детей
    public class HoverRow : Panel
    {
        private bool hovered = false;

        public HoverRow()
        {
            DoubleBuffered = true;
            BackColor = Color.Transparent;
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.MouseEnter += (s, ev) => SetHover(true);
            e.Control.MouseLeave += (s, ev) => ReCheckHover();
        }

        protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { ReCheckHover(); base.OnMouseLeave(e); }

        // Уход с дочернего контрола на саму строку - не считается уходом со строки
        private void ReCheckHover()
        {
            SetHover(ClientRectangle.Contains(PointToClient(Cursor.Position)));
        }

        private void SetHover(bool value)
        {
            if (hovered == value) return;
            hovered = value;
            Invalidate(true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (hovered)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = Theme.RoundedRect(rect, 8))
                using (var bg = new SolidBrush(Theme.NavHoverBg))
                {
                    e.Graphics.FillPath(bg, path);
                }
            }
            base.OnPaint(e);
        }
    }
}   