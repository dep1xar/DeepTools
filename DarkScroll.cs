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
    public class DarkScroll : Control
    {
        [DllImport("user32.dll")]
        private static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
        private const int SB_VERT = 1;

        private readonly ScrollableControl target;
        private bool dragging;
        private int dragGrabY;

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
            target.Scroll += (s, e) => Invalidate();

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

        // PLACEHOLDER_REST
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
            long max = vs.Maximum + 1; if (max < 1) max = 1;
            thumbH = (int)Math.Max(28, (long)track * vs.LargeChange / max);
            if (thumbH > track) thumbH = track;
            int scrollable = vs.Maximum - vs.LargeChange + 1; if (scrollable < 1) scrollable = 1;
            int span = track - thumbH;
            thumbY = (int)((long)span * vs.Value / scrollable);
            if (thumbY < 0) thumbY = 0;
            if (thumbY > span) thumbY = span;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int ty, th; GetThumb(out ty, out th);
            using (var tb = new SolidBrush(Theme.Lerp(Theme.BgColor, Theme.TextDim, 0.06f)))
                g.FillRectangle(tb, Width / 2 - 1, 0, 2, Height);
            var r = new Rectangle(1, ty, Width - 2, th);
            using (var path = Theme.RoundedRect(r, (Width - 2) / 2))
            using (var b = new SolidBrush(dragging ? Theme.TextDim : Theme.Lerp(Theme.BgColor, Theme.TextDim, 0.5f)))
                g.FillPath(b, path);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int ty, th; GetThumb(out ty, out th);
            if (e.Y >= ty && e.Y <= ty + th) { dragging = true; dragGrabY = e.Y - ty; }
            else SetFromThumbY(e.Y - th / 2, th);
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            int ty, th; GetThumb(out ty, out th);
            SetFromThumbY(e.Y - dragGrabY, th);
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var vs = target.VerticalScroll;
            int mx = vs.Maximum - vs.LargeChange + 1; if (mx < 0) mx = 0;
            int val = vs.Value - Math.Sign(e.Delta) * 60;
            if (val < 0) val = 0; if (val > mx) val = mx;
            try { target.AutoScrollPosition = new Point(0, val); } catch { }
            Reposition();
        }

        private void SetFromThumbY(int thumbY, int thumbH)
        {
            var vs = target.VerticalScroll;
            int span = Height - thumbH; if (span < 1) span = 1;
            if (thumbY < 0) thumbY = 0; if (thumbY > span) thumbY = span;
            int scrollable = vs.Maximum - vs.LargeChange + 1; if (scrollable < 1) scrollable = 1;
            int val = (int)((long)thumbY * scrollable / span);
            try { target.AutoScrollPosition = new Point(0, val); } catch { }
            Reposition();
        }
    }
}
