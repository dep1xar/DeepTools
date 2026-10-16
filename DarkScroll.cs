using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // Кастомный тёмный скроллбар вместо нативного (у нативного виден «контур»
    // трека, который не убрать штатно, а на светлых темах он вообще выбивается).
    //
    // Оверлей-полоса живёт в родителе панели (не скроллится вместе с контентом),
    // нативную полосу прячем через ShowScrollBar.
    //
    // Позиция ползунка берётся из AutoScrollPosition, а НЕ из VerticalScroll.Value:
    // у AutoScroll-панелей Value обновляется рывками и на медленном колесе
    // «залипает» — страница едет, а ползунок стоит. AutoScrollPosition всегда
    // отражает реальное смещение, поэтому ползунок ходит плавно.
    public class DarkScroll : Control
    {
        [DllImport("user32.dll")]
        private static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
        private const int SB_VERT = 1;
        private const int WHEEL_DELTA = 120;

        // Реестр живых полос: Attach зовётся до того, как панель получит Parent,
        // поэтому поиск по Controls ненадёжен — ведём учёт сами
        private static readonly List<DarkScroll> live = new List<DarkScroll>();

        private readonly ScrollableControl target;
        private bool dragging;
        private bool hovered;
        private int dragGrabY;
        private int wheelAccum;
        private Rectangle lastBounds;

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
            if (panel == null) return null;
            for (int i = 0; i < live.Count; i++)
            {
                DarkScroll ds = live[i];
                if (ds.target == panel && !ds.IsDisposed) return ds;
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

            live.Add(this);

            HookParent();
            target.ParentChanged += OnTargetParentChanged;
            target.SizeChanged += OnTargetLayoutChanged;
            target.LocationChanged += OnTargetLayoutChanged;
            target.VisibleChanged += OnTargetVisibleChanged;
            target.Layout += OnTargetLayoutChanged;
            // Scroll — единственный источник правды для перерисовки ползунка.
            // Колесо мы НЕ перехватываем: панель скроллится сама, нам нужно лишь
            // успевать перерисовывать ползунок (перехват давал двойную прокрутку).
            target.Scroll += OnTargetScroll;
            target.MouseEnter += OnTargetMouseEnter;
            target.MouseLeave += OnTargetMouseLeave;

            if (target.IsHandleCreated) AttachHider();
            target.HandleCreated += OnTargetHandleCreated;
        }

        private void AttachHider()
        {
            try { hider.AssignHandle(target.Handle); ShowScrollBar(target.Handle, SB_VERT, false); }
            catch { }
        }

        private void OnTargetHandleCreated(object sender, EventArgs e) { AttachHider(); }
        private void OnTargetParentChanged(object sender, EventArgs e) { HookParent(); }
        private void OnTargetLayoutChanged(object sender, EventArgs e) { Reposition(); }
        private void OnTargetVisibleChanged(object sender, EventArgs e) { Visible = target.Visible; Reposition(); }
        private void OnTargetScroll(object sender, ScrollEventArgs e) { Reposition(); Invalidate(); }
        private void OnTargetMouseEnter(object sender, EventArgs e) { hovered = true; Invalidate(); }
        private void OnTargetMouseLeave(object sender, EventArgs e) { hovered = false; Invalidate(); }

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
            if (target == null || IsDisposed) return;
            if (target.Parent == null || Parent != target.Parent) return;

            ScrollProperties vs = target.VerticalScroll;
            bool need = target.Visible && vs.Maximum > vs.LargeChange;
            if (Visible != need) Visible = need;
            if (!need) return;

            var bounds = new Rectangle(target.Right - Width - 3, target.Top + 2, Width, target.Height - 4);
            if (bounds != lastBounds)
            {
                lastBounds = bounds;
                Bounds = bounds;
                BringToFront();
            }
            Invalidate();
        }

        // Реальное смещение прокрутки: у AutoScroll-панелей истина здесь,
        // VerticalScroll.Value на плавном колесе отстаёт
        private int ScrollOffset(ScrollProperties vs, int scrollable)
        {
            int val;
            if (target.AutoScroll)
            {
                try { val = -target.AutoScrollPosition.Y; }
                catch { val = vs.Value; }
            }
            else
            {
                val = vs.Value;
            }
            if (val < 0) val = 0;
            if (val > scrollable) val = scrollable;
            return val;
        }

        private void GetThumb(out int thumbY, out int thumbH)
        {
            ScrollProperties vs = target.VerticalScroll;
            int track = Height;

            double max = (double)vs.Maximum + 1; if (max < 1) max = 1;
            double th = Math.Max(28.0, (double)track * vs.LargeChange / max);
            if (th > track) th = track;
            thumbH = (int)Math.Round(th);

            int scrollable = vs.Maximum - vs.LargeChange + 1; if (scrollable < 1) scrollable = 1;
            int span = track - thumbH; if (span < 1) span = 1;

            int val = ScrollOffset(vs, scrollable);
            // float-точный пересчёт: целочисленное деление давало «залипание»
            // на длинном контенте, где один пиксель трека = много пикселей контента
            thumbY = (int)Math.Round((double)span * val / scrollable);
            if (thumbY < 0) thumbY = 0;
            if (thumbY > span) thumbY = span;
        }

        private Color ThumbColor()
        {
            if (dragging) return Theme.Accent;
            // Подходит под все темы: на светлой — темнее, на тёмных — светлее.
            // При наведении чуть контрастнее, чтобы полоса читалась
            float k = (hovered || IsMouseOver) ? 0.72f : 0.5f;
            return Theme.Lerp(Theme.BgColor, Theme.TextDim, k);
        }

        private bool IsMouseOver
        {
            get { try { return ClientRectangle.Contains(PointToClient(Cursor.Position)); } catch { return false; } }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Полоса-оверлей лежит поверх панели: заливаем фоном панели,
            // иначе на скруглениях и при перерисовке остаётся мусор
            Color bg = target != null ? target.BackColor : Theme.BgColor;
            using (var b = new SolidBrush(bg))
                e.Graphics.FillRectangle(b, ClientRectangle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int ty, th;
            GetThumb(out ty, out th);

            // Трек: полупрозрачный, читается и на светлой, и на тёмной теме
            using (var tb = new SolidBrush(Color.FromArgb(55, Theme.BorderColor)))
                g.FillRectangle(tb, Width / 2 - 1, 0, 2, Height);

            var r = new Rectangle(1, ty, Width - 2, th);
            int radius = Math.Max(2, Math.Min((Width - 2) / 2, th / 2));
            using (var path = Theme.RoundedRect(r, radius))
            using (var b = new SolidBrush(ThumbColor()))
                g.FillPath(b, path);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int ty, th;
            GetThumb(out ty, out th);
            if (e.Y >= ty && e.Y <= ty + th)
            {
                dragging = true;
                dragGrabY = e.Y - ty;
                Capture = true;
            }
            else
            {
                // Клик по треку — прыжок, затем сразу тащим
                dragging = true;
                dragGrabY = th / 2;
                SetFromThumbY(e.Y - dragGrabY, th);
                Capture = true;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging)
            {
                Invalidate(); // hover-подсветка
                return;
            }
            int ty, th;
            GetThumb(out ty, out th);
            SetFromThumbY(e.Y - dragGrabY, th);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Capture = false;
            Invalidate();
        }

        // Колесо над самой полосой. Накапливаем дельту: «медленное» колесо
        // (дельта меньше 120) через Math.Sign терялось целиком
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandleWheel(e.Delta);
            HandledMouseEventArgs h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        private void HandleWheel(int delta)
        {
            wheelAccum += delta;
            int steps = wheelAccum / WHEEL_DELTA;
            if (steps == 0) return;
            wheelAccum -= steps * WHEEL_DELTA;

            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;

            ScrollProperties vs = target.VerticalScroll;
            int mx = vs.Maximum - vs.LargeChange + 1; if (mx < 0) mx = 0;
            int step = (vs.SmallChange > 0 ? vs.SmallChange : 40) * lines;
            if (step < 10) step = 40;

            int val = ScrollOffset(vs, mx) - steps * step;
            if (val < 0) val = 0;
            if (val > mx) val = mx;
            SetScrollValue(val);
        }

        private void SetFromThumbY(int thumbY, int thumbH)
        {
            ScrollProperties vs = target.VerticalScroll;
            int span = Height - thumbH; if (span < 1) span = 1;
            if (thumbY < 0) thumbY = 0;
            if (thumbY > span) thumbY = span;
            int scrollable = vs.Maximum - vs.LargeChange + 1; if (scrollable < 1) scrollable = 1;
            int val = (int)Math.Round((double)thumbY * scrollable / span);
            SetScrollValue(val);
        }

        private void SetScrollValue(int val)
        {
            try
            {
                // Сохраняем горизонтальное смещение, меняем только вертикаль
                int x = -target.AutoScrollPosition.X;
                target.AutoScrollPosition = new Point(x, val);
            }
            catch { }
            Reposition();
            Invalidate();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Detach();
            base.OnHandleDestroyed(e);
        }

        private void Detach()
        {
            live.Remove(this);
            try
            {
                target.ParentChanged -= OnTargetParentChanged;
                target.SizeChanged -= OnTargetLayoutChanged;
                target.LocationChanged -= OnTargetLayoutChanged;
                target.VisibleChanged -= OnTargetVisibleChanged;
                target.Layout -= OnTargetLayoutChanged;
                target.Scroll -= OnTargetScroll;
                target.MouseEnter -= OnTargetMouseEnter;
                target.MouseLeave -= OnTargetMouseLeave;
                target.HandleCreated -= OnTargetHandleCreated;
            }
            catch { }
        }
    }
}
