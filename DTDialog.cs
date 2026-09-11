using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Диалог в стиле приложения вместо системного MessageBox: тот рисует белое
    // окно Win32, которое выбивается из тёмной темы. API повторяет MessageBox.Show,
    // так что замена по всему коду - механическая
    public static class DTDialog
    {
        public static DialogResult Show(string text)
        {
            return Show(null, text, "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Show(string text, string caption)
        {
            return Show(null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        {
            return Show(null, text, caption, buttons, MessageBoxIcon.None);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(null, text, caption, buttons, icon);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using (var form = new DTDialogForm(text, caption, buttons, icon))
            {
                return owner != null ? form.ShowDialog(owner) : form.ShowDialog();
            }
        }
    }

    internal class DTDialogForm : Form
    {
        private const int W = 420;
        private const int TextW = 356;

        public DTDialogForm(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            TopMost = true;
            Width = W;

            // Иконка: только BMP-символы, эмодзи в GDI превращаются в квадраты
            string iconText;
            Color iconColor;
            switch (icon)
            {
                case MessageBoxIcon.Warning: iconText = "⚠"; iconColor = Theme.Warning; break;
                case MessageBoxIcon.Error: iconText = "✕"; iconColor = Theme.Danger; break;
                case MessageBoxIcon.Question: iconText = "?"; iconColor = Theme.Accent; break;
                case MessageBoxIcon.Information: iconText = "i"; iconColor = Theme.Accent; break;
                default: iconText = null; iconColor = Theme.Accent; break;
            }

            int textLeft = 24;
            if (iconText != null)
            {
                var iconLbl = new Label
                {
                    Text = iconText,
                    ForeColor = iconColor,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                    Location = new Point(20, 20),
                    Size = new Size(32, 32),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                Controls.Add(iconLbl);
                textLeft = 60;
            }

            var captionLbl = new Label
            {
                Text = caption,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                Location = new Point(textLeft, 24),
                AutoSize = true
            };
            Controls.Add(captionLbl);

            var bodyFont = new Font("Segoe UI", 9.5F);
            Size measured = TextRenderer.MeasureText(text, bodyFont, new Size(TextW, 0),
                TextFormatFlags.WordBreak);

            var bodyLbl = new Label
            {
                Text = text,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = bodyFont,
                Location = new Point(24, 60),
                Size = new Size(TextW + 16, measured.Height + 6)
            };
            Controls.Add(bodyLbl);

            int btnY = 60 + bodyLbl.Height + 18;

            // Кнопки справа налево: главная (Accent) - крайняя правая
            int bx = W - 24;
            switch (buttons)
            {
                case MessageBoxButtons.YesNo:
                    bx = AddButton(bx, Lang.T("Да", "Yes"), DialogResult.Yes, true, btnY);
                    bx = AddButton(bx, Lang.T("Нет", "No"), DialogResult.No, false, btnY);
                    CancelButtonResult(DialogResult.No);
                    break;
                case MessageBoxButtons.YesNoCancel:
                    bx = AddButton(bx, Lang.T("Да", "Yes"), DialogResult.Yes, true, btnY);
                    bx = AddButton(bx, Lang.T("Нет", "No"), DialogResult.No, false, btnY);
                    bx = AddButton(bx, Lang.T("Отмена", "Cancel"), DialogResult.Cancel, false, btnY);
                    CancelButtonResult(DialogResult.Cancel);
                    break;
                case MessageBoxButtons.OKCancel:
                    bx = AddButton(bx, "OK", DialogResult.OK, true, btnY);
                    bx = AddButton(bx, Lang.T("Отмена", "Cancel"), DialogResult.Cancel, false, btnY);
                    CancelButtonResult(DialogResult.Cancel);
                    break;
                default:
                    bx = AddButton(bx, "OK", DialogResult.OK, true, btnY);
                    CancelButtonResult(DialogResult.OK);
                    break;
            }

            Height = btnY + 34 + 22;

            Load += (s, e) =>
            {
                var path = new GraphicsPath();
                int r = 12, d = r * 2;
                var rect = new Rectangle(0, 0, Width, Height);
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            };
        }

        // Добавляет кнопку правым краем в bx, возвращает левый край для следующей
        private int AddButton(int bx, string text, DialogResult result, bool primary, int y)
        {
            var btn = new RoundedButton
            {
                Text = text,
                ButtonColor = primary ? Theme.Accent : Theme.KeyColor,
                HoverColor = primary ? Theme.AccentHover : Theme.KeyHover,
                TextColor = primary ? Theme.BgColor : Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(92, 32)
            };
            btn.Location = new Point(bx - btn.Width, y);
            btn.Click += (s, e) => { DialogResult = result; Close(); };
            Controls.Add(btn);
            return btn.Left - 10;
        }

        // Esc закрывает диалог с "безопасным" результатом (Нет/Отмена)
        private void CancelButtonResult(DialogResult result)
        {
            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => { DialogResult = result; Close(); };
            Controls.Add(esc);
            CancelButton = esc;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
