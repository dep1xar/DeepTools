using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Выбор языка при самом первом запуске. Раньше язык угадывался по локали
    // Windows молча - англичанин с русской Windows (или наоборот) получал
    // непонятный интерфейс и даже не знал, где его сменить. Теперь спрашиваем прямо
    public class LanguagePickerForm : Form
    {
        public LanguagePickerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(400, 260);
            BackColor = Theme.BgColor;
            TopMost = true;
            DoubleBuffered = true;

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

            var logo = new Label
            {
                Text = "DeepTools",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(0, 28),
                Size = new Size(400, 36),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(logo);

            // Обе подписи сразу: язык ещё не выбран, Lang.T использовать нельзя
            var prompt = new Label
            {
                Text = "Выбери язык  ·  Choose your language",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F),
                Location = new Point(0, 70),
                Size = new Size(400, 22),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(prompt);

            var ruBtn = MakeLangButton("🇷 Русский", new Point(48, 116));
            ruBtn.Click += (s, e) => Pick("ru");

            var enBtn = MakeLangButton("🇬 English", new Point(208, 116));
            enBtn.Click += (s, e) => Pick("en");

            var hint = new Label
            {
                Text = "Настройки → Язык  ·  Settings → Language",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(0, 214),
                Size = new Size(400, 16),
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(hint);
        }

        private RoundedButton MakeLangButton(string text, Point loc)
        {
            var btn = new RoundedButton
            {
                Text = text,
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.Accent,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = loc,
                Size = new Size(144, 72),
                CornerRadius = 12
            };
            Controls.Add(btn);
            return btn;
        }

        private void Pick(string lang)
        {
            AppConfig.Set("language", lang);
            Lang.IsEn = lang == "en";
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // Мастер первого запуска: короткий гид "что тут есть и с чего начать".
    // Показывается один раз после выбора языка
    public class FirstRunWizardForm : Form
    {
        private readonly Action<string> navigate;
        private Label ramResult;

        public FirstRunWizardForm(Action<string> navigateTo)
        {
            navigate = navigateTo;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(480, 430);
            BackColor = Theme.BgColor;
            ShowInTaskbar = false;
            DoubleBuffered = true;

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
                Text = "👋 " + Lang.T("Добро пожаловать в DeepTools!", "Welcome to DeepTools!"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                Location = new Point(28, 26),
                AutoSize = true
            };
            Controls.Add(title);

            var sub = new Label
            {
                Text = Lang.T("С чего обычно начинают:", "Here is where people usually start:"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(30, 58),
                AutoSize = true
            };
            Controls.Add(sub);

            int y = 92;
            y = AddAction(y, "♻", Lang.T("Очистить мусор", "Clean junk"),
                Lang.T("Temp-файлы, кэши, кэши шейдеров", "Temp files, caches, shader caches"), "cleanup");
            y = AddAction(y, "⚡", Lang.T("Ускорить под игры", "Boost for gaming"),
                Lang.T("План питания, приоритеты, Game DVR", "Power plan, priorities, Game DVR"), "booster");
            y = AddAction(y, "🛡", Lang.T("Отключить телеметрию", "Disable telemetry"),
                Lang.T("Слежка Windows: три пресета, всё обратимо", "Windows tracking: three presets, fully reversible"), "services");
            y = AddAction(y, "⭯", Lang.T("Разобрать автозагрузку", "Review startup apps"),
                Lang.T("Отключи лишнее — Windows стартует быстрее", "Disable the extras — Windows boots faster"), "startup");

            // Единственное действие, которое безопасно сделать прямо из визарда
            var ramBtn = new RoundedButton
            {
                Text = Lang.T("🧹 Освободить RAM сейчас", "🧹 Free RAM now"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(28, y + 6),
                Size = new Size(200, 32)
            };
            ramBtn.Click += (s, e) => {
                ramBtn.Enabled = false;
                var worker = new System.ComponentModel.BackgroundWorker();
                worker.DoWork += (s2, e2) => e2.Result = RamCleaner.Clean();
                worker.RunWorkerCompleted += (s2, e2) => {
                    long freed = e2.Error == null && e2.Result != null ? (long)e2.Result : 0;
                    ramResult.Text = freed > 0
                        ? Lang.T("Освобождено ~", "Freed ~") + freed + Lang.T(" МБ ✓", " MB ✓")
                        : Lang.T("Память уже оптимальна ✓", "Memory already optimal ✓");
                    ramBtn.Enabled = true;
                };
                worker.RunWorkerAsync();
            };
            Controls.Add(ramBtn);

            ramResult = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(238, y + 13),
                AutoSize = true
            };
            Controls.Add(ramResult);

            var startBtn = new RoundedButton
            {
                Text = Lang.T("Понятно, поехали!", "Got it, let's go!"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point((480 - 180) / 2, y + 52),
                Size = new Size(180, 36)
            };
            startBtn.Click += (s, e) => Close();
            Controls.Add(startBtn);

            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => Close();
            Controls.Add(esc);
            CancelButton = esc;
        }

        // Строка визарда: иконка, название, описание и стрелка-переход в раздел
        private int AddAction(int y, string icon, string title, string subtitle, string key)
        {
            var row = new HoverRow { Location = new Point(20, y), Size = new Size(440, 52), Cursor = Cursors.Hand };

            var iconLbl = new Label
            {
                Text = icon,
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 14F),
                Location = new Point(8, 12),
                Size = new Size(32, 28),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            row.Controls.Add(iconLbl);

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(48, 8),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            row.Controls.Add(titleLbl);

            var subLbl = new Label
            {
                Text = subtitle,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(48, 28),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            row.Controls.Add(subLbl);

            var arrow = new Label
            {
                Text = "→",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(404, 14),
                Size = new Size(28, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            row.Controls.Add(arrow);

            // Клик по любой части строки ведёт в раздел и закрывает визард
            EventHandler go = (s, e) => { Close(); if (navigate != null) navigate(key); };
            row.Click += go;
            iconLbl.Click += go;
            titleLbl.Click += go;
            subLbl.Click += go;
            arrow.Click += go;

            Controls.Add(row);
            return y + 56;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
