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
    // текст своими средствами.
    //
    // Почему без дочерней панели-«простыни»: диапазон прокрутки задаётся
    // через AutoScrollMinSize, а строки рисуются прямо на панели со сдвигом
    // на AutoScrollPosition. Раньше внутри лежала панель, которой на каждой
    // раскладке присваивались новые Bounds - а это сбрасывает
    // AutoScrollPosition в 0. Отсюда было «листнул, и сразу вернуло наверх»,
    // плюс петля: появление/исчезновение полосы меняло ClientSize, снова
    // вызывало раскладку и снова сбрасывало прокрутку.
    public class ClipboardHistoryView : Panel
    {
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
            TabStop = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

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

            UpdateScrollRange();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void ClearSelection()
        {
            if (selectedIndex == -1) return;
            selectedIndex = -1;
            selectedId = null;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        // Диапазон прокрутки. Позицию сохраняем: смена AutoScrollMinSize может
        // сбросить её, а пользователь в этот момент читает середину списка
        private void UpdateScrollRange()
        {
            int keep = -AutoScrollPosition.Y;
            int height = items.Count == 0 ? 0 : items.Count * rowHeight;

            var size = new Size(0, height);
            if (AutoScrollMinSize != size) AutoScrollMinSize = size;

            if (keep > 0)
            {
                try { AutoScrollPosition = new Point(0, keep); } catch { }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // Никаких Bounds у детей и никакого сброса прокрутки - только перерисовка
            Invalidate();
        }

        // Смещение содержимого: AutoScrollPosition отрицательный при прокрутке
        private int ScrollY
        {
            get
            {
                int v = -AutoScrollPosition.Y;
                return v < 0 ? 0 : v;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (items.Count == 0)
            {
                string msg = Lang.T("История пуста - скопируй что-нибудь", "History is empty - copy something");
                using (var f = new Font("Segoe UI", 10F))
                using (var b = new SolidBrush(Theme.TextDim))
                {
                    SizeF sz = g.MeasureString(msg, f);
                    g.DrawString(msg, f, b,
                        Math.Max(0, (ClientSize.Width - sz.Width) / 2f),
                        Math.Max(0, (ClientSize.Height - sz.Height) / 2f));
                }
                return;
            }

            int scroll = ScrollY;
            int width = ClientSize.Width;

            using (var titleFont = new Font("Segoe UI", 9.5F))
            using (var metaFont = new Font("Segoe UI", 8F))
            {
                // Рисуем только то, что реально попало в видимую область -
                // на длинной истории это экономит время
                int first = Math.Max(0, scroll / rowHeight);
                int last = Math.Min(items.Count - 1, (scroll + ClientSize.Height) / rowHeight);

                for (int i = first; i <= last; i++)
                {
                    ClipboardEntry it = items[i];
                    int y = i * rowHeight - scroll;
                    var row = new Rectangle(6, y + 2, width - 12, rowHeight - 4);
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
                    int markHeight = Math.Max(4, row.Height - 20);
                    using (var b = new SolidBrush(mark))
                    using (var path = Theme.RoundedRect(new Rectangle(row.X + 6, row.Y + 10, 4, markHeight), 2))
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

        // Координата мыши -> индекс строки (с учётом прокрутки)
        private int IndexAt(int mouseY)
        {
            if (items.Count == 0) return -1;
            int contentY = mouseY + ScrollY;
            int i = contentY / rowHeight;
            if (i < 0 || i >= items.Count) return -1;
            return i;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // Клавиатурная навигация работает только когда список в фокусе
            try { if (!Focused) Focus(); } catch { }

            int i = IndexAt(e.Y);
            if (i < 0) return;

            selectedIndex = i;
            selectedId = items[i].Id;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Y);
            if (i != hoverIndex)
            {
                hoverIndex = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIndex != -1)
            {
                hoverIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = IndexAt(e.Y);
            if (i < 0) return;

            selectedIndex = i;
            selectedId = items[i].Id;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            if (ItemActivated != null) ItemActivated(this, EventArgs.Empty);
        }

        // Клавиатура: стрелки и Enter работают, когда список в фокусе
        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.PageUp ||
                keyData == Keys.PageDown || keyData == Keys.Enter || keyData == Keys.Home ||
                keyData == Keys.End)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (items.Count == 0) return;

            int i = selectedIndex;
            switch (e.KeyCode)
            {
                case Keys.Up: i = i <= 0 ? 0 : i - 1; break;
                case Keys.Down: i = i < 0 ? 0 : Math.Min(items.Count - 1, i + 1); break;
                case Keys.PageUp: i = Math.Max(0, (i < 0 ? 0 : i) - PageRows()); break;
                case Keys.PageDown: i = Math.Min(items.Count - 1, (i < 0 ? 0 : i) + PageRows()); break;
                case Keys.Home: i = 0; break;
                case Keys.End: i = items.Count - 1; break;
                case Keys.Enter:
                    if (ItemActivated != null) ItemActivated(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                default: return;
            }

            selectedIndex = i;
            selectedId = items[i].Id;
            EnsureVisible(i);
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            e.Handled = true;
        }

        private int PageRows()
        {
            return Math.Max(1, ClientSize.Height / rowHeight);
        }

        // Прокрутить так, чтобы строка была видна
        private void EnsureVisible(int index)
        {
            int scroll = ScrollY;
            int top = index * rowHeight;
            int bottom = top + rowHeight;
            int view = ClientSize.Height;

            if (top < scroll)
            {
                try { AutoScrollPosition = new Point(0, Math.Max(0, top)); } catch { }
            }
            else if (bottom > scroll + view)
            {
                try { AutoScrollPosition = new Point(0, Math.Max(0, bottom - view + 2)); } catch { }
            }
        }
    }
}
