using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Плитка быстрого перехода на главной: иконка, название, описание, ховер
    public class QuickCard : TransparentControl
    {
        private bool hovered = false;
        public string IconText = "";
        public string Title = "";
        public string Subtitle = "";

        public QuickCard()
        {
            Size = new Size(230, 96);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(rect, 14))
            using (var bg = new SolidBrush(hovered ? Theme.NavActiveBg : Theme.CardColor))
            using (var border = new Pen(hovered ? Theme.Accent : Theme.BorderColor))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            // TextRenderer вместо DrawString: чёткий текст (ClearType) и подстановка
            // недостающих символов из других шрифтов
            using (var iconFont = new Font("Segoe UI", 16F))
            {
                TextRenderer.DrawText(g, IconText, iconFont,
                    new Rectangle(10, 10, 44, 36), hovered ? Theme.Accent : Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }

            using (var titleFont = new Font("Segoe UI", 10F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, Title, titleFont,
                    new Rectangle(14, 46, Width - 28, 20), Theme.TextMain,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            using (var subFont = new Font("Segoe UI", 8F))
            {
                TextRenderer.DrawText(g, Subtitle, subFont,
                    new Rectangle(14, 68, Width - 28, 24), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
        }
    }

    // Главная страница: приветствие, живой мониторинг и быстрые переходы по разделам
    public class HomePanel : Panel
    {
        public event Action<string> RequestNavigate;

        private Label cpuValue;
        private Label ramValue;
        private Label cpuTempValue;
        private Label gpuTempValue;
        private System.Windows.Forms.Timer statsTimer;
        private WidgetForm widget;

        public HomePanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            BuildUi();

            statsTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            statsTimer.Tick += (s, e) => RefreshStats();
            statsTimer.Start();
        }

        private void BuildUi()
        {
            // Приветственная карточка
            var heroCard = Theme.MakeCard(this, new Point(24, 24), new Size(712, 120));

            var heroDot = new Panel { Size = new Size(16, 16), Location = new Point(24, 26), BackColor = Color.Transparent };
            heroDot.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var glow = new SolidBrush(Color.FromArgb(60, Theme.Accent)))
                    e.Graphics.FillEllipse(glow, -4, -4, 24, 24);
                using (var b = new SolidBrush(Theme.Accent))
                    e.Graphics.FillEllipse(b, 0, 0, 16, 16);
            };
            heroCard.Controls.Add(heroDot);

            var heroTitle = new Label
            {
                Text = "DeepTools " + AppVersion.Short,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
                Location = new Point(52, 16),
                AutoSize = true
            };
            heroCard.Controls.Add(heroTitle);

            var heroSub = new Label
            {
                Text = Lang.T("Чистка, ускорение и мониторинг компьютера в одном месте", "Cleanup, speedup and monitoring in one place"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(54, 58),
                AutoSize = true
            };
            heroCard.Controls.Add(heroSub);

            var heroHint = new Label
            {
                Text = Lang.T("Программа живёт в трее: закрытие окна не выключает её. ", "Lives in the tray: closing the window does not exit. ")
                       + Hotkeys.Get(NativeMethods.HOTKEY_ID_CLICKER) + Lang.T(" - кликер, ", " - clicker, ")
                       + Hotkeys.Get(NativeMethods.HOTKEY_ID_SCREENSHOT) + Lang.T(" - скриншот", " - screenshot"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(54, 88),
                AutoSize = true
            };
            heroCard.Controls.Add(heroHint);

            // Виджет мониторинга поверх всех окон
            var widgetLabel = new Label
            {
                Text = Lang.T("Виджет на экран", "Desktop widget"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(540, 24),
                AutoSize = true
            };
            heroCard.Controls.Add(widgetLabel);

            var widgetToggle = new ToggleSwitch { Location = new Point(648, 20), Checked = AppConfig.GetBool("widget_visible", false) };
            widgetToggle.CheckedChanged += (s, e) => SetWidgetVisible(widgetToggle.Checked);
            heroCard.Controls.Add(widgetToggle);

            if (AppConfig.GetBool("widget_visible", false)) SetWidgetVisible(true);

            // Живой мониторинг
            MakeStatTile(new Point(24, 160), Lang.T("Загрузка CPU", "CPU load"), out cpuValue);
            MakeStatTile(new Point(206, 160), Lang.T("Загрузка RAM", "RAM load"), out ramValue);
            MakeStatTile(new Point(388, 160), Lang.T("Темп. CPU", "CPU temp"), out cpuTempValue);
            MakeStatTile(new Point(570, 160), Lang.T("Темп. GPU", "GPU temp"), out gpuTempValue);

            // Быстрые переходы
            var quickTitle = new Label
            {
                Text = Lang.T("Быстрые действия", "Quick actions"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(24, 268),
                AutoSize = true
            };
            Controls.Add(quickTitle);

            // Иконки только из базовой части юникода (BMP): эмодзи вроде 🧹 или 🚀
            // GDI-отрисовка превращает в пустые квадраты
            MakeQuickCard(new Point(24, 300), "♻",
                Lang.T("Очистить мусор", "Clean junk"),
                Lang.T("Temp-файлы и кэш", "Temp files and cache"), "cleanup");
            MakeQuickCard(new Point(265, 300), "☄",
                "GameBooster",
                Lang.T("Буст игр и Ultimate Performance", "Game boost and Ultimate Performance"), "booster");
            MakeQuickCard(new Point(506, 300), "❤",
                "Health Check",
                Lang.T("Датчики, диски, бенчмарк", "Sensors, disks, benchmark"), "health");
            MakeQuickCard(new Point(24, 406), "⚡",
                Lang.T("Автокликер", "Autoclicker"),
                Lang.T("Мышь и клавиатура, ", "Mouse and keyboard, ") + Hotkeys.Get(NativeMethods.HOTKEY_ID_CLICKER), "clicker");
            MakeQuickCard(new Point(265, 406), "◉",
                Lang.T("Скриншоты", "Screenshots"),
                Lang.T("Снимок экрана по ", "Capture screen with ") + Hotkeys.Get(NativeMethods.HOTKEY_ID_SCREENSHOT), "screenshots");
            MakeQuickCard(new Point(506, 406), "⚙",
                Lang.T("Настройки", "Settings"),
                Lang.T("Тема, язык, права", "Theme, language, rights"), "settings");

            var ramBtn = new RoundedButton
            {
                Text = Lang.T("🧹 Освободить RAM", "🧹 Free RAM"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(24, 516),
                Size = new Size(190, 34)
            };
            ramBtn.Click += (s, e) => FreeRam();
            Controls.Add(ramBtn);

            ramResultLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(226, 524),
                Size = new Size(500, 18),
                AutoEllipsis = true
            };
            Controls.Add(ramResultLabel);
        }

        private Label ramResultLabel;

        private void FreeRam()
        {
            ramResultLabel.ForeColor = Theme.TextDim;
            ramResultLabel.Text = Lang.T("Освобождаю память...", "Freeing memory...");
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => e.Result = RamCleaner.Clean();
            worker.RunWorkerCompleted += (s, e) => {
                long freed = e.Error == null && e.Result != null ? (long)e.Result : 0;
                ramResultLabel.ForeColor = Theme.Accent;
                ramResultLabel.Text = freed > 0
                    ? Lang.T("Освобождено ~", "Freed ~") + freed + Lang.T(" МБ", " MB")
                    : Lang.T("Память уже оптимальна", "Memory already optimal");
            };
            worker.RunWorkerAsync();
        }

        private void MakeStatTile(Point loc, string title, out Label valueLabel)
        {
            var card = Theme.MakeCard(this, loc, new Size(166, 84));

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(14, 12),
                AutoSize = true
            };
            card.Controls.Add(titleLbl);

            valueLabel = new Label
            {
                Text = "—",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(12, 36),
                AutoSize = true
            };
            card.Controls.Add(valueLabel);
        }

        private void MakeQuickCard(Point loc, string icon, string title, string subtitle, string key)
        {
            var card = new QuickCard
            {
                Location = loc,
                IconText = icon,
                Title = title,
                Subtitle = subtitle
            };
            card.Click += (s, e) => { if (RequestNavigate != null) RequestNavigate(key); };
            Controls.Add(card);
        }

        public void SetWidgetVisible(bool visible)
        {
            AppConfig.SetBool("widget_visible", visible);
            if (visible)
            {
                if (widget == null || widget.IsDisposed) widget = new WidgetForm();
                widget.Show();
            }
            else
            {
                if (widget != null && !widget.IsDisposed) { widget.Close(); widget = null; }
            }
        }

        private void RefreshStats()
        {
            if (!Visible) return;
            cpuValue.Text = SystemStats.CpuLoad;
            ramValue.Text = SystemStats.RamLoad;
            cpuTempValue.Text = SystemStats.CpuTemp;
            gpuTempValue.Text = SystemStats.GpuTemp;
        }
    }
}
