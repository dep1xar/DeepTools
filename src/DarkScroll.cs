using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // Кастомный тёмный скроллбар вместо нативного (у нативного виден «контур»
    // трека, который не убрать штатно, а на светлых темах он выбивается).
    //
    // Оверлей-полоса живёт в родителе панели (не скроллится вместе с контентом),
    // нативную полосу прячем через ShowScrollBar.
    //
    // Синхронизация с содержимым. Раньше ползунок перерисовывался асинхронно
    // (Invalidate по событию Scroll), а панель - своим WM_PAINT. Два контрола
    // попадали в разные кадры, и ползунок то отставал, то убегал вперёд.
    //
    // Теперь порядок жёсткий: хук на окне панели ловит WM_PAINT уже ПОСЛЕ того,
    // как содержимое отрисовано, и здесь же синхронно (Update) перерисовывает
    // ползунок. Оба всегда в одном кадре. Позицию берём из AutoScrollPosition:
    // VerticalScroll.Value у AutoScroll-панелей обновляется рывками.
    public class DarkScroll : Control
    {
        [DllImport("user32.dll")]
        private static extern int ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);
        private const int SB_VERT = 1;

        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_SIZE = 0x0005;
        private const int WM_PAINT = 0x000F;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;

        // Реестр живых полос: Attach зовётся до того, как панель получит Parent,
        // поэтому поиск по Controls ненадёжен - ведём учёт сами
        private static readonly List<DarkScroll> live = new List<DarkScroll>();

        private readonly ScrollableControl target;
        private readonly NativeHider hider;
        private bool dragging;
        private bool hovered;
        private bool syncing;
        private int dragGrabY;
        private Rectangle lastBounds;
        private int lastThumbY = -1;
        private int lastThumbH = -1;

        // Хук на окно панели: прячет нативную полосу и синхронизирует ползунок
        // с содержимым в одном кадре
        private class NativeHider : NativeWindow
        {
            private readonly DarkScroll owner;
            private bool guard;

            public NativeHider(DarkScroll owner) { this.owner = owner; }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (guard) return;

                int msg = m.Msg;

                if (msg == WM_NCCALCSIZE || msg == WM_NCPAINT || msg == WM_SIZE)
                {
                    guard = true;
                    try { ShowScrollBar(Handle, SB_VERT, false); } catch { }
                    guard = false;
                    return;
                }

                // Содержимое уже нарисовано - ставим ползунок на место синхронно
                if (msg == WM_PAINT || msg == WM_VSCROLL || msg == WM_HSCROLL || msg == WM_MOUSEWHEEL)
                {
                    if (owner == null) return;
                    guard = true;
                    try { owner.SyncNow(); } catch { }
                    guard = false;
                }
            }
        }

        public static DarkScroll Attach(ScrollableControl panel)
        {
            if (panel == null) return null;
            // Заодно подключаем панель к плавной прокрутке: колесо едет с
            // инерцией, а полоса следует за AutoScrollPosition кадр в кадр
            SmoothScroll.Attach(panel);
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
            hider = new NativeHider(this);
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
            target.Scroll += OnTargetScroll;
            target.MouseEnter += OnTargetMouseEnter;
            target.MouseLeave += OnTargetMouseLeave;

            if (target.IsHandleCreated) AttachHider();
            target.HandleCreated += OnTargetHandleCreated;
        }

        private void AttachHider()
        {
            try
            {
                hider.AssignHandle(target.Handle);
                ShowScrollBar(target.Handle, SB_VERT, false);
            }
            catch { }
        }

        private void OnTargetHandleCreated(object sender, EventArgs e) { AttachHider(); }
        private void OnTargetParentChanged(object sender, EventArgs e) { HookParent(); }
        private void OnTargetLayoutChanged(object sender, EventArgs e) { Reposition(); }
        private void OnTargetVisibleChanged(object sender, EventArgs e) { Visible = target.Visible; Reposition(); }
        private void OnTargetScroll(object sender, ScrollEventArgs e) { SyncNow(); }
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
        }

        // Пересчёт и НЕМЕДЛЕННАЯ перерисовка. Вызывается из хука окна панели
        // сразу после её отрисовки - поэтому ползунок и содержимое не расходятся
        private void SyncNow()
        {
            if (syncing || IsDisposed) return;
            syncing = true;
            try
            {
                Reposition();
                if (!Visible) return;

                int ty, th;
                GetThumb(out ty, out th);
                if (ty != lastThumbY || th != lastThumbH)
                {
                    lastThumbY = ty;
                    lastThumbH = th;
                    Invalidate();
                    Update(); // синхронно, без ожидания следующего WM_PAINT
                }
            }
            catch { }
            finally { syncing = false; }
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
            // Подходит под все темы: на светлой - темнее, на тёмных - светлее
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
            lastThumbY = ty;
            lastThumbH = th;

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
            // Пользователь взял ползунок - прекращаем доезд, дальше ведёт мышь
            SmoothScroll.Stop(target);

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
                // Клик по треку - прыжок, затем сразу тащим
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
            // Кнопку отпустили вне полосы - перетаскивание кончилось
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { EndDrag(); return; }

            int ty, th;
            GetThumb(out ty, out th);
            SetFromThumbY(e.Y - dragGrabY, th);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            EndDrag();
        }

        // Захват может пропасть сам (другое окно, подсказка): пока кнопка нажата,
        // ползунок обязан продолжать ехать за мышью, а не «останавливаться»
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && dragging && (Control.MouseButtons & MouseButtons.Left) != 0)
            {
                try { Capture = true; } catch { }
            }
            base.OnMouseCaptureChanged(e);
        }

        private void EndDrag()
        {
            if (!dragging && !Capture) return;
            dragging = false;
            try { Capture = false; } catch { }
            Invalidate();
        }

        // Колесо над самой полосой. Отдаём плавной прокрутке, чтобы полоса и
        // содержимое ехали одинаково мягко и не расходились
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            SmoothScroll.ScrollBy(target, e.Delta);
            HandledMouseEventArgs h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
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
            // Ползунок тянет пользователь - плавная прокрутка подхватывает позицию
            SmoothScroll.SyncFromTarget(target);
            SyncNow();
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
