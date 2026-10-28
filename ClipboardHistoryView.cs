using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Список истории буфера обмена с собственной отрисовкой.
    //
    // Почему не ListBox: у него системный скроллбар, к которому нельзя
    // прицепить общий DarkScroll (ListBox не ScrollableControl), и он рисует
    // текст своими средствами. Здесь строки рисуем сами на внутренней
    // «простыне» нужной высоты - панель получает настоящий AutoScroll,
    // а сверху работает тот же DarkScroll, что и во всех остальных разделах.
    public class ClipboardHistoryView : Panel
    {
        private readonly Panel canvas;
        private List<ClipboardEntry> items = new List<ClipboardEntry>();
        private readonly int rowHeight = 48;
        private int selectedIndex = -1;
        private int hoverIndex = -1;
        private string selectedId;

        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;

        public ClipboardHistoryView()
        {
            BackColor = Theme.SidebarColor;
            AutoScroll = true;
            DoubleBuffered = true;

            canvas = new Panel();
            canvas.BackColor = Theme.SidebarColor;
            canvas.Paint += OnCanvasPaint;
            canvas.MouseDown += OnCanvasMouseDown;
            canvas.MouseMove += OnCanvasMouseMove;
            canvas.MouseLeave += OnCanvasMouseLeave;
            canvas.MouseDoubleClick += OnCanvasDoubleClick;
            Controls.Add(canvas);

            NativeMethods.ApplyDarkScrollbar(this);
            DarkScroll.Attach(this);
        }

        public int SelectedIndex { get { return selectedIndex; } }

        public ClipboardEntry SelectedItem
        {
            get
            {
                if (selectedIndex < 0 || selectedIndex >= items.Count) return null;
                return items[selectedIndex];
            }
        }

        // Полная замена содержимого с сохранением выбранного элемента по Id,
        // чтобы обновление истории не сбрасывало выбор пользователю
        public void SetItems(List<ClipboardEntry> newItems)
        {
            ClipboardEntry current = SelectedItem;
            string keepId = current != null ? current.Id : selectedId;

            items = newItems ?? new List<ClipboardEntry>();
            selectedIndex = -1;
            if (!string.IsNullOrEmpty(keepId))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Id == keepId) { selectedIndex = i; break; }
                }
            }
            selectedId = selectedIndex >= 0 ? items[selectedIndex].Id : null;

            LayoutCanvas();
            canvas.Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void ClearSelection()
        {
            if (selectedIndex == -1) return;
            selectedIndex = -1;
            selectedId = null;
            canvas.Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutCanvas();
            canvas.Invalidate();
        }

        private void LayoutCanvas()
        {
            int w = Math.Max(1, ClientSize.Width);
            int h = items.Count == 0
                ? Math.Max(1, ClientSize.Height)
                : Math.Max(1, items.Count * rowHeight);
            canvas.Bounds = new Rectangle(0, 0, w, h);
        }

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (items.Count == 0)
            {
                string msg = Lang.T("История пуста — скопируй что-нибудь", "History is empty — copy something");
                using (var f = new Font("Segoe UI", 10F))
                using (var b = new SolidBrush(Theme.TextDim))
                {
                    SizeF sz = g.MeasureString(msg, f);
                    g.DrawString(msg, f, b,
                        Math.Max(0, (canvas.Width - sz.Width) / 2f),
                        Math.Max(0, (canvas.Height - sz.Height) / 2f));
                }
                return;
            }

            int w = canvas.Width;
            using (var titleFont = new Font("Segoe UI", 9.5F))
            using (var metaFont = new Font("Segoe UI", 8F))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ClipboardEntry it = items[i];
                    var row = new Rectangle(6, i * rowHeight + 2, w - 12, rowHeight - 4);
                    bool sel = i == selectedIndex;
                    bool hov = i == hoverIndex;

                    if (sel || hov)
                    {
                        using (var path = Theme.RoundedRect(row, 8))
                        using (var b = new SolidBrush(sel ? Theme.NavActiveBg : Theme.NavHoverBg))
                            g.FillPath(b, path);
                    }

                    // Маркер типа записи
                    Color mark = it.Type == "image" ? Theme.Warning : Theme.Accent;
                    int markTop = row.Y + 10;
                    int markHeight = Math.Max(4, row.Height - 20);
                    using (var b = new SolidBrush(mark))
                    using (var path = Theme.RoundedRect(new Rectangle(row.X + 6, markTop, 4, markHeight), 2))
                        g.FillPath(b, path);

                    int textLeft = row.X + 18;
                    int textWidth = Math.Max(10, row.Width - 24);

                    string meta = it.Timestamp.ToString("HH:mm:ss") + "  ·  "
                        + (it.Type == "image" ? Lang.T("картинка", "image") : Lang.T("текст", "text"));
                    if (it.IsPinned) meta = Lang.T("закреплено", "pinned") + "  ·  " + meta;

                    TextRenderer.DrawText(g, it.DisplayText, titleFont,
                        new Rectangle(textLeft, row.Y + 5, textWidth, 20), Theme.TextMain,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

                    TextRenderer.DrawText(g, meta, metaFont,
                        new Rectangle(textLeft, row.Y + 25, textWidth, 16), Theme.TextDim,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            }
        }

        private int IndexAt(int y)
        {
            if (items.Count == 0) return -1;
            int i = y / rowHeight;
            if (i < 0 || i >= items.Count) return -1;
            return i;
        }

        private void OnCanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = IndexAt(e.Y);
            if (i < 0) return;

            selectedIndex = i;
            selectedId = items[i].Id;
            canvas.Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i != hoverIndex)
            {
                hoverIndex = i;
                canvas.Invalidate();
            }
        }

        private void OnCanvasMouseLeave(object sender, EventArgs e)
        {
            if (hoverIndex != -1)
            {
                hoverIndex = -1;
                canvas.Invalidate();
            }
        }

        private void OnCanvasDoubleClick(object sender, MouseEventArgs e)
        {
            int i = IndexAt(e.Y);
            if (i < 0) return;

            selectedIndex = i;
            selectedId = items[i].Id;
            canvas.Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            if (ItemActivated != null) ItemActivated(this, EventArgs.Empty);
        }
    }
}
