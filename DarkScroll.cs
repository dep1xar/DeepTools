using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // Кастомный тёмный скроллбар вместо нативного (у нативного виден «контур»
    // трека, который не убрать штатно). Оверлей-полоса живёт в родителе панели
    // (не скроллится вместе с контентом), нативную полосу прячем через ShowScrollBar.
    // Фикс: плавный скролл без залипания на медленном колесе + подходит под все темы
    public class DarkScroll : Control
    {
        [DllImport("user32.dll")]
        private static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
        private const int SB_VERT = 1;

        private readonly ScrollableControl target;
        private bool dragging;
        private int dragGrabY;
        private int wheelAccum = 0;

        // Прячем нативную вертикальную полосу, перехватывая сообщения окна панели
        private class NativeHider : NativeWindow
        {
            private bool guard;
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                // WM_NCCALCSIZE / WM_NCPAINT / WM_SIZE
                if (!guard && (m.Msg == 0x83 || m.Msg == 0x85 || m.Msg == 0x05))
                {
                    guard = true;
                    try { ShowScrollBar(Handle, SB_VERT, false); } catch { }
                    guard = false;
                }
            }
        }

        private readonly NativeHider hider = new NativeHider();

        public static DarkScroll Attach(ScrollableControl panel)
        {
            // не плодить дубликаты если уже есть
            if (panel.Parent != null)
            {
                foreach (Control c in panel.Parent.Controls)
                {
                    DarkScroll ds = c as DarkScroll;
                    if (ds != null && ds.target == panel) return ds;
                }
            }
            else
            {
                foreach (Control c in panel.Controls)
                {
                    DarkScroll ds = c as DarkScroll;
                    if (ds != null && ds.target == panel) return ds;
                }
            }
            return new DarkScroll(panel);
        }

        private DarkScroll(ScrollableControl panel)
        {
            target = panel;
            Width = 10;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Default;

            HookParent();
            target.ParentChanged += (s, e) => HookParent();
            target.SizeChanged += (s, e) => Reposition();
            target.LocationChanged += (s, e) => Reposition();
            target.VisibleChanged += (s, e) => { Visible = target.Visible; Reposition(); };
            target.Layout += (s, e) => Reposition();
            target.Scroll += (s, e) => { Reposition(); Invalidate(); };
            target.MouseWheel += OnTargetWheel;

            if (target.IsHandleCreated) AttachHider();
            target.HandleCreated += (s, e) => AttachHider();
        }

        private void AttachHider()
        {
            try { hider.AssignHandle(target.Handle); ShowScrollBar(target.Handle, SB_VERT, false); }
            catch { }
        }

        private void HookParent()
        {
            if (target.Parent == null) return;
            if (Parent != target.Parent)
            {
                if (Parent != null) Parent.Controls.Remove(this);
                target.Parent.Controls.Add(this);
            }
            BringToFront();
            Reposition();
        }

        private void Reposition()
        {
            if (target.Parent == null || Parent != target.Parent) return;
            var vs = target.VerticalScroll;
            bool need = target.Visible && vs.Maximum > vs.LargeChange;
            Visible = need;
            if (!need) return;
            Bounds = new Rectangle(target.Right - Width - 3, target.Top + 2, Width, target.Height - 4);
            BringToFront();
            Invalidate();
        }

        private void GetThumb(out int thumbY, out int thumbH)
        {
            var vs = target.VerticalScroll;
            int track = Height;
            // float-точный расчёт чтобы на медленном скролле не "залипало"
            double max = (double)vs.Maximum + 1; if (max < 1) max = 1;
            double th = Math.Max(28, (double)track * vs.LargeChange / max);
            if (th > track) th = track;
            thumbH = (int)Math.Round(th);
            int scrollable = vs.Maximum - vs.LargeChange + 1; if (scrollable < 1) scrollable = 1;
            int span = track - thumbH; if (span < 1) span = 1;
            // Берём актуальное значение прокрутки: VerticalScroll.Value более точен чем AutoScrollPosition
            // но на некоторых конфигурациях Value лагает — fallback на AutoScrollPosition
            int val = vs.Value;
            try
            {
                int autoVal = -target.AutoScrollPosition.Y;
                // если расхождение большое — берём autoVal (он точнее при инерции)
                if (Math.Abs(autoVal - val) > 2 && autoVal >= 0 && autoVal <= scrollable)
                    val = autoVal;
            }
            catch { }
            thumbY = (int)Math.Round((double)span * val / scrollable);
            if (thumbY < 0) thumbY = 0;
            if (thumbY > span) thumbY = span;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int ty, th; GetThumb(out ty, out th);
            // трек — полупрозрачный, виден на всех темах (светлая/тёмная/neon/rust/graphite)
            using (var tb = new SolidBrush(Color.FromArgb(60, Theme.BorderColor)))
                g.FillRectangle(tb, Width / 2 - 1, 0, 2, Height);
            var r = new Rectangle(1, ty, Width - 2, th);
            // thumb — адаптивный: на светлой теме темнее, на тёмной светлее
            Color thumbCol = dragging ? Theme.Accent : (Theme.IsLight ? Color.FromArgb(140, 110, 120, 140) : Color.FromArgb(160, 140, 150, 175));
            // если акцент слишком близок к фону — добавляем контраст
            if (!dragging && !Theme.IsLight)
                thumbCol = Theme.Lerp(Theme.BgColor, Theme.TextDim, 0.55f);
            using (var path = Theme.RoundedRect(r, (Width - 2) / 2))
            using (var b = new SolidBrush(thumbCol))
                g.FillPath(b, path);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int ty, th; GetThumb(out ty, out th);
            if (e.Y >= ty && e.Y <= ty + th) { dragging = true; dragGrabY = e.Y - ty; Capture = true; }
            else SetFromThumbY(e.Y - th / 2, th);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            int ty, th; GetThumb(out ty, out th);
            SetFromThumbY(e.Y - dragGrabY, th);
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); }

        // Колесо над самим скроллбаром
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandleWheel(e.Delta);
            ((HandledMouseEventArgs)e).Handled = true;
        }

        // Колесо над целью — главный фикс залипания: накапливаем дельту,
        // чтобы медленный скролл (delta 20-60) не терялся через Math.Sign
        private void OnTargetWheel(object sender, MouseEventArgs e)
        {
            HandleWheel(e.Delta);
            // не handled — пусть и панель тоже скроллится, но мы уже синхронизировали
            Reposition();
            Invalidate();
        }

        private void HandleWheel(int delta)
        {
            wheelAccum += delta;
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;
            // 120 = WHEEL_DELTA
            int steps = wheelAccum / 120;
            if (steps == 0) return;
            wheelAccum -= steps * 120;
            // шаг прокрутки — пропорционально SmallChange/LargeChange
            int smallStep = Math.Max(20, target.VerticalScroll.SmallChange);
            // если SmallChange не задан — берём 40
            if (smallStep < 5) smallStep = 40;
            int move = steps * smallStep * lines / 3;
            // инвертируем: колесо вверх = delta +, скролл вверх = value -
            var vs = target.VerticalScroll;
            int mx = vs.Maximum - vs.LargeChange + 1; if (mx < 0) mx = 0;
            int val = vs.Value - move;
            if (val < vs.Minimum) val = vs.Minimum;
            if (val > mx) val = mx;
            try { target.AutoScrollPosition = new Point(-target.AutoScrollPosition.X, val); } catch { }
            Reposition();
            Invalidate();
        }

        private void SetFromThumbY(int thumbY, int thumbH)
        {
            var vs = target.VerticalScroll;
            int span = Height - thumbH; if (span < 1) span = 1;
            if (thumbY < 0) thumbY = 0; if (thumbY > span) thumbY = span;
            int scrollable = vs.Maximum - vs.LargeChange + 1; if (scrollable < 1) scrollable = 1;
            // float-точный перевод
            int val = (int)Math.Round((double)thumbY * scrollable / span);
            try { target.AutoScrollPosition = new Point(-target.AutoScrollPosition.X, val); } catch { }
            Reposition();
            Invalidate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { target.MouseWheel -= OnTargetWheel; } catch { }
            base.OnHandleDestroyed(e);
        }
    }
}
