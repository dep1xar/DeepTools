using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace DeepTools
{
    // Версия политики. Если текст политики меняется по существу - увеличь
    // версию: при следующем запуске согласие спросят заново.
    public static class PrivacyPolicy
    {
        public const string Version = "2026-11-07";

        public static bool IsAccepted
        {
            get { return AppConfig.Get("privacy_accepted", "") == Version; }
        }

        // Показываем экран согласия до любых сетевых запросов и до UAC.
        public static bool EnsureAccepted()
        {
            if (IsAccepted) return true;

            using (var form = new PrivacyConsentForm())
                return form.ShowDialog() == DialogResult.OK;
        }

        public static void OpenPolicy(IWin32Window owner)
        {
            using (var form = new PolicyForm())
            {
                if (owner != null) form.ShowDialog(owner);
                else form.ShowDialog();
            }
        }

        public static string Text()
        {
            // PRIVACY.md встроена в exe при сборке - политика доступна даже
            // без интернета и без файла рядом с программой.
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("DeepTools.PRIVACY.md"))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                            return reader.ReadToEnd();
                    }
                }
            }
            catch { }

            try
            {
                string local = Path.Combine(Application.StartupPath, "PRIVACY.md");
                if (File.Exists(local)) return File.ReadAllText(local, Encoding.UTF8);
            }
            catch { }

            return Lang.T(
                "Не удалось открыть встроенный текст политики. Переустанови DeepTools или обратись к автору.",
                "Could not open the built-in policy text. Reinstall DeepTools or contact the author.");
        }
    }

    // Чекбокс в стиле приложения: системный CheckBox на тёмном фоне выглядит
    // чужеродно и плохо читается.
    public class ConsentCheck : Control
    {
        private bool _checked;
        private bool hovered;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
            }
        }

        public ConsentCheck()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            ForeColor = Theme.TextMain;
            Font = new Font("Segoe UI", 9.2F);
            Cursor = Cursors.Hand;
            Height = 24;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); }

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
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int box = 18;
            int y = (Height - box) / 2;
            var rect = new Rectangle(1, y, box, box);

            using (var path = Theme.RoundedRect(rect, 5))
            using (var fill = new SolidBrush(_checked ? Theme.Accent : Theme.InputColor))
            using (var pen = new Pen(hovered ? Theme.Accent : Theme.BorderColor))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            if (_checked)
            {
                using (var font = new Font("Segoe UI", 10F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "✓", font,
                        new Rectangle(rect.X, rect.Y - 1, rect.Width, rect.Height),
                        Theme.BgColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding);
                }
            }

            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(28, 0, Width - 28, Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    public class PrivacyConsentForm : Form
    {
        private readonly ConsentCheck acceptCheck;
        private readonly RoundedButton continueBtn;

        public PrivacyConsentForm()
        {
            Text = "DeepTools";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 366);
            BackColor = Theme.BgColor;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Load += (s, e) =>
            {
                var path = new GraphicsPath();
                int r = 14, d = r * 2;
                var rect = new Rectangle(0, 0, Width, Height);
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            };

            var dot = new Label
            {
                Text = "●",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 14F),
                Location = new Point(26, 22),
                Size = new Size(20, 24)
            };
            Controls.Add(dot);

            var title = new Label
            {
                Text = Lang.T("Конфиденциальность", "Privacy"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                Location = new Point(50, 20),
                AutoSize = true
            };
            Controls.Add(title);

            var sub = new Label
            {
                Text = Lang.T("Прежде чем начать", "Before you start"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(28, 58),
                AutoSize = true
            };
            Controls.Add(sub);

            var body = new Label
            {
                Text = Lang.T(
                    "DeepTools — локальная программа для настройки Windows. Она работает на вашем компьютере и не требует учётной записи.\r\n\r\n" +
                    "Перед началом примите политику конфиденциальности: в ней описано, какие данные остаются на компьютере, а какие программа не собирает.",
                    "DeepTools is a local Windows tweaking app. It runs on your computer and does not require an account.\r\n\r\n" +
                    "Before you start, accept the Privacy Policy: it explains what data stays on your computer and what the app does not collect."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(28, 88),
                Size = new Size(504, 100),
                AutoSize = false
            };
            Controls.Add(body);

            acceptCheck = new ConsentCheck
            {
                Text = Lang.T(
                    "Я принимаю политику конфиденциальности",
                    "I agree to the Privacy Policy"),
                Location = new Point(28, 202),
                Size = new Size(504, 26)
            };
            acceptCheck.CheckedChanged += (s, e) => UpdateContinueButton();
            Controls.Add(acceptCheck);

            var hint = new Label
            {
                Text = Lang.T(
                    "Без принятия политики программа не запустится.",
                    "The app will not start without accepting the policy."),
                ForeColor = Theme.Lerp(Theme.BgColor, Theme.TextDim, 0.8f),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.8F),
                Location = new Point(28, 238),
                Size = new Size(504, 24),
                AutoSize = false
            };
            Controls.Add(hint);

            var policyBtn = new RoundedButton
            {
                Text = Lang.T("Открыть политику", "Open policy"),
                ButtonColor = Theme.InputColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextDim,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(28, 298),
                Size = new Size(180, 32)
            };
            policyBtn.Click += (s, e) => PrivacyPolicy.OpenPolicy(this);
            Controls.Add(policyBtn);

            var exitBtn = new RoundedButton
            {
                Text = Lang.T("Выйти", "Exit"),
                ButtonColor = Theme.InputColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextDim,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(216, 298),
                Size = new Size(100, 32)
            };
            exitBtn.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(exitBtn);

            continueBtn = new RoundedButton
            {
                Text = Lang.T("Продолжить", "Continue"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.TextDim,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(352, 296),
                Size = new Size(180, 36),
                Enabled = false
            };
            continueBtn.Click += (s, e) =>
            {
                AppConfig.Set("privacy_accepted", PrivacyPolicy.Version);
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(continueBtn);

            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(esc);
            CancelButton = esc;
        }

        private void UpdateContinueButton()
        {
            bool enabled = acceptCheck.Checked;
            continueBtn.Enabled = enabled;
            continueBtn.Cursor = enabled ? Cursors.Hand : Cursors.Default;
            continueBtn.ButtonColor = enabled ? Theme.Accent : Theme.KeyColor;
            continueBtn.TextColor = enabled ? Theme.BgColor : Theme.TextDim;
            continueBtn.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    public class PolicyForm : Form
    {
        public PolicyForm()
        {
            Text = "DeepTools";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(720, 620);
            BackColor = Theme.BgColor;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            Load += (s, e) =>
            {
                var path = new GraphicsPath();
                int r = 14, d = r * 2;
                var rect = new Rectangle(0, 0, Width, Height);
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            };

            var title = new Label
            {
                Text = Lang.T("Политика конфиденциальности", "Privacy Policy"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(24, 18),
                AutoSize = true
            };
            Controls.Add(title);

            var text = new TextBox
            {
                Text = PrivacyPolicy.Text(),
                Multiline = true,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.CardColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5F),
                Location = new Point(24, 52),
                Size = new Size(672, 500)
            };
            Controls.Add(text);

            var closeBtn = new RoundedButton
            {
                Text = Lang.T("Закрыть", "Close"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(584, 566),
                Size = new Size(112, 32)
            };
            closeBtn.Click += (s, e) => Close();
            Controls.Add(closeBtn);

            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => Close();
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
