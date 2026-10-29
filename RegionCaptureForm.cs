using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Скриншот области: полноэкранное окно поверх всего с замороженным снимком экрана.
    // Тянешь рамку -> выделенная область яркая, остальное затемнено.
    // После выделения - панель инструментов: стрелка, рамка, текст.
    // Enter/двойной клик = сохранить, Esc = отмена (на шаге рисования - убрать последнее, потом отмена).
    public class RegionCaptureForm : Form
    {
        private enum Tool { None, Arrow, Rect, Text }

        private class Annotation
        {
            public Tool Kind;
            public Point From;
            public Point To;
            public string Text;
        }

        private Bitmap screenShot;
        private Rectangle virtualBounds;

        private bool selecting = false;
        private bool hasSelection = false;
        private Point selStart;
        private Rectangle selection = Rectangle.Empty;

        private Tool currentTool = Tool.None;
        private bool drawingAnnotation = false;
        private Annotation draft;
        private List<Annotation> annotations = new List<Annotation>();

        private TextBox textInput;

        // Результат: что вернуть вызывающему
        public Bitmap ResultImage;

        public RegionCaptureForm()
        {
            virtualBounds = SystemInformation.VirtualScreen;

            // Замораживаем экран одним снимком - по нему и выделяем
            screenShot = new Bitmap(virtualBounds.Width, virtualBounds.Height);
            using (Graphics g = Graphics.FromImage(screenShot))
            {
                g.CopyFromScreen(virtualBounds.Location, Point.Empty, virtualBounds.Size);
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;

            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;
            MouseDoubleClick += (s, e) => { if (hasSelection) Finish(); };
            KeyDown += OnKey;
            // Без фокуса окно не получит Esc - забираем его явно
            Shown += (s, e) => { try { Activate(); Focus(); } catch { } };

            textInput = new TextBox
            {
                Visible = false,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                BackColor = Color.FromArgb(30, 37, 54),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Width = 220
            };
            textInput.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter)
                {
                    CommitText();
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    textInput.Visible = false;
                    e.SuppressKeyPress = true;
                }
            };
            Controls.Add(textInput);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (screenShot != null && ResultImage != screenShot) screenShot.Dispose();
            base.OnFormClosed(e);
        }

        // Esc и Enter ловим на уровне формы, а не только через KeyDown:
        // KeyDown не приходит, если фокус на дочернем контроле, и окно
        // «не закрывалось по Esc». Esc закрывает сразу - раньше первое
        // нажатие снимало последнюю аннотацию, второе - выделение, и только
        // третье закрывало окно, из-за чего казалось, что Esc не работает
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (textInput != null && textInput.Visible) return false; // пусть TextBox сам скроет ввод
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                if (textInput != null && textInput.Visible) return false; // Enter подтвердит текст
                if (hasSelection) { Finish(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            // Esc/Enter обрабатывает ProcessCmdKey; здесь только инструменты
            if (e.KeyCode == Keys.A) SetTool(Tool.Arrow);
            else if (e.KeyCode == Keys.R) SetTool(Tool.Rect);
            else if (e.KeyCode == Keys.T) SetTool(Tool.Text);
        }

        private void SetTool(Tool t)
        {
            if (!hasSelection) return;
            currentTool = t;
            Cursor = t == Tool.None ? Cursors.Default : Cursors.Cross;
            Invalidate();
        }

        private Rectangle ToolbarRect()
        {
            // Панель инструментов под выделением (или над, если не влезает)
            int w = 232, h = 40;
            int x = Math.Min(selection.Right - w, virtualBounds.Width - w - 8);
            x = Math.Max(8, x);
            int y = selection.Bottom + 8;
            if (y + h > virtualBounds.Height - 8) y = selection.Top - h - 8;
            if (y < 8) y = 8;
            return new Rectangle(x, y, w, h);
        }

        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            if (hasSelection)
            {
                // Клик по панели инструментов?
                Rectangle tb = ToolbarRect();
                if (tb.Contains(e.Location))
                {
                    int idx = (e.X - tb.X) / 58;
                    if (idx == 0) SetTool(Tool.Arrow);
                    else if (idx == 1) SetTool(Tool.Rect);
                    else if (idx == 2) SetTool(Tool.Text);
                    else if (idx == 3) Finish();
                    return;
                }

                if (currentTool == Tool.Text && selection.Contains(e.Location))
                {
                    textInput.Location = e.Location;
                    textInput.Tag = e.Location;
                    textInput.Text = "";
                    textInput.Visible = true;
                    textInput.BringToFront();
                    textInput.Focus();
                    return;
                }

                if ((currentTool == Tool.Arrow || currentTool == Tool.Rect) && selection.Contains(e.Location))
                {
                    drawingAnnotation = true;
                    draft = new Annotation { Kind = currentTool, From = e.Location, To = e.Location };
                    return;
                }
                return;
            }

            selecting = true;
            selStart = e.Location;
            selection = new Rectangle(e.Location, Size.Empty);
            Invalidate();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (selecting)
            {
                selection = MakeRect(selStart, e.Location);
                Invalidate();
            }
            else if (drawingAnnotation && draft != null)
            {
                draft.To = e.Location;
                Invalidate();
            }
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            if (selecting)
            {
                selecting = false;
                if (selection.Width > 4 && selection.Height > 4)
                {
                    hasSelection = true;
                }
                else
                {
                    selection = Rectangle.Empty;
                }
                Invalidate();
            }
            else if (drawingAnnotation && draft != null)
            {
                drawingAnnotation = false;
                draft.To = e.Location;
                annotations.Add(draft);
                draft = null;
                Invalidate();
            }
        }

        private void CommitText()
        {
            if (!textInput.Visible || textInput.Text.Trim() == "") { textInput.Visible = false; return; }
            annotations.Add(new Annotation
            {
                Kind = Tool.Text,
                From = (Point)textInput.Tag,
                Text = textInput.Text
            });
            textInput.Visible = false;
            Invalidate();
        }

        private Rectangle MakeRect(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        // Собираем итоговую картинку: вырезанная область + аннотации поверх
        private void Finish()
        {
            if (selection.Width < 5 || selection.Height < 5) return;

            var result = new Bitmap(selection.Width, selection.Height);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.DrawImage(screenShot, new Rectangle(0, 0, selection.Width, selection.Height), selection, GraphicsUnit.Pixel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                foreach (Annotation a in annotations)
                {
                    DrawAnnotation(g, a, -selection.X, -selection.Y);
                }
            }
            ResultImage = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DrawAnnotation(Graphics g, Annotation a, int offX, int offY)
        {
            Color annColor = Color.FromArgb(240, 70, 70);
            if (a.Kind == Tool.Arrow)
            {
                using (var pen = new Pen(annColor, 3))
                {
                    pen.CustomEndCap = new AdjustableArrowCap(5, 6);
                    g.DrawLine(pen, a.From.X + offX, a.From.Y + offY, a.To.X + offX, a.To.Y + offY);
                }
            }
            else if (a.Kind == Tool.Rect)
            {
                using (var pen = new Pen(annColor, 3))
                {
                    Rectangle r = MakeRect(a.From, a.To);
                    r.Offset(offX, offY);
                    g.DrawRectangle(pen, r);
                }
            }
            else if (a.Kind == Tool.Text)
            {
                using (var font = new Font("Segoe UI", 12F, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(a.Text, font);
                    var bgRect = new RectangleF(a.From.X + offX - 2, a.From.Y + offY - 2, sz.Width + 4, sz.Height + 4);
                    using (var bg = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                        g.FillRectangle(bg, bgRect);
                    using (var brush = new SolidBrush(Color.White))
                        g.DrawString(a.Text, font, brush, a.From.X + offX, a.From.Y + offY);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            // Замороженный экран
            g.DrawImage(screenShot, 0, 0);

            // Затемнение всего, кроме выделения
            using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                if (selection.IsEmpty)
                {
                    g.FillRectangle(dim, ClientRectangle);
                }
                else
                {
                    g.FillRectangle(dim, 0, 0, Width, selection.Top);
                    g.FillRectangle(dim, 0, selection.Bottom, Width, Height - selection.Bottom);
                    g.FillRectangle(dim, 0, selection.Top, selection.Left, selection.Height);
                    g.FillRectangle(dim, selection.Right, selection.Top, Width - selection.Right, selection.Height);
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Аннотации
            foreach (Annotation a in annotations) DrawAnnotation(g, a, 0, 0);
            if (draft != null) DrawAnnotation(g, draft, 0, 0);

            if (!selection.IsEmpty)
            {
                using (var borderPen = new Pen(Color.FromArgb(46, 214, 140), 2))
                {
                    g.DrawRectangle(borderPen, selection);
                }

                // Размер выделения
                string sizeText = selection.Width + " × " + selection.Height;
                using (var font = new Font("Segoe UI", 9F, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(sizeText, font);
                    float tx = selection.Left;
                    float ty = selection.Top - sz.Height - 4;
                    if (ty < 4) ty = selection.Top + 4;
                    using (var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                        g.FillRectangle(bg, tx, ty, sz.Width + 6, sz.Height + 2);
                    using (var brush = new SolidBrush(Color.White))
                        g.DrawString(sizeText, font, brush, tx + 3, ty + 1);
                }
            }

            // Панель инструментов
            if (hasSelection)
            {
                Rectangle tb = ToolbarRect();
                using (var path = Theme.RoundedRect(tb, 8))
                using (var bg = new SolidBrush(Color.FromArgb(235, 22, 28, 43)))
                using (var border = new Pen(Color.FromArgb(60, 70, 90)))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(border, path);
                }

                string[] labels = {
                    Lang.T("Стрелка", "Arrow") + " (A)",
                    Lang.T("Рамка", "Box") + " (R)",
                    Lang.T("Текст", "Text") + " (T)",
                    "✔ OK"
                };
                using (var font = new Font("Segoe UI", 7.5F, FontStyle.Bold))
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var cell = new Rectangle(tb.X + i * 58, tb.Y, 58, tb.Height);
                        bool active = (i == 0 && currentTool == Tool.Arrow)
                            || (i == 1 && currentTool == Tool.Rect)
                            || (i == 2 && currentTool == Tool.Text);
                        Color textColor = i == 3 ? Color.FromArgb(46, 214, 140)
                            : active ? Color.FromArgb(46, 214, 140) : Color.FromArgb(220, 226, 235);
                        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                        using (var brush = new SolidBrush(textColor))
                            g.DrawString(labels[i], font, brush, cell, fmt);
                    }
                }
            }
            else if (selection.IsEmpty)
            {
                string hint = Lang.T("Выдели область мышкой  •  Esc - отмена", "Drag to select an area  •  Esc - cancel");
                using (var font = new Font("Segoe UI", 11F, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(hint, font);
                    float hx = (Width - sz.Width) / 2;
                    using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0)))
                        g.FillRectangle(bg, hx - 10, 34, sz.Width + 20, sz.Height + 10);
                    using (var brush = new SolidBrush(Color.White))
                        g.DrawString(hint, font, brush, hx, 39);
                }
            }
        }
    }
}
