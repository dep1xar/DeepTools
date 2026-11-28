using System;
using System.Collections.Generic;
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
            Size = new Size(230, 110);
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
                    new Rectangle(14, 54, Width - 28, 20), Theme.TextMain,
                    TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            using (var subFont = new Font("Segoe UI", 8F))
            {
                TextRenderer.DrawText(g, Subtitle, subFont,
                    new Rectangle(14, 78, Width - 28, 26), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
        }
    }

    // Плитка мониторинга: заголовок, крупное значение и график за последние
    // ~80 секунд. Карточка нейтральная, как остальные, линия - акцент темы
    public class StatTile : TransparentControl
    {
        private const int HistorySize = 40;
        private readonly List<float> history = new List<float>();
        private string valueText = "-";

        public string Title = "";

        public StatTile()
        {
            Size = new Size(166, 140);
        }

        public void Push(float value, string text)
        {
            valueText = text;
            if (value >= 0)
            {
                history.Add(Math.Max(0f, Math.Min(100f, value)));
                if (history.Count > HistorySize) history.RemoveAt(0);
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(rect, 14))
            using (var bg = new SolidBrush(Theme.CardColor))
            using (var border = new Pen(Theme.BorderColor))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            using (var titleFont = new Font("Segoe UI", 8.5F))
            {
                TextRenderer.DrawText(g, Title, titleFont,
                    new Rectangle(14, 11, Width - 28, 18), Theme.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }

            using (var valueFont = new Font("Segoe UI", 17F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, valueText, valueFont,
                    new Rectangle(12, 32, Width - 24, 34), Theme.TextMain,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }

            DrawSpark(g, new Rectangle(12, 82, Width - 24, Height - 94));
        }

        // Линия с заливкой под ней, фиксированный масштаб 0..100
        private void DrawSpark(Graphics g, Rectangle area)
        {
            if (area.Width < 4 || area.Height < 4) return;

            using (var grid = new Pen(Color.FromArgb(28, Theme.TextDim)))
            {
                int mid = area.Top + area.Height / 2;
                g.DrawLine(grid, area.Left, mid, area.Right, mid);
            }

            if (history.Count < 2) return;

            float stepX = area.Width / (float)(HistorySize - 1);
            int offset = HistorySize - history.Count;
            var points = new PointF[history.Count];
            for (int i = 0; i < history.Count; i++)
            {
                float x = area.Left + (offset + i) * stepX;
                float y = area.Bottom - history[i] / 100f * area.Height;
                points[i] = new PointF(x, y);
            }

            using (var path = new GraphicsPath())
            {
                path.AddLine(points[0].X, area.Bottom, points[0].X, points[0].Y);
                for (int i = 1; i < points.Length; i++) path.AddLine(points[i - 1], points[i]);
                path.AddLine(points[points.Length - 1].X, points[points.Length - 1].Y,
                    points[points.Length - 1].X, area.Bottom);
                path.CloseFigure();

                using (var fill = new LinearGradientBrush(
                    new Rectangle(area.Left, area.Top, Math.Max(1, area.Width), Math.Max(1, area.Height)),
                    Color.FromArgb(70, Theme.Accent), Color.FromArgb(4, Theme.Accent), 90f))
                    g.FillPath(fill, path);
            }

            using (var pen = new Pen(Theme.Accent, 1.8f))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, points);
            }
        }
    }

    // Главная страница: приветствие, живой мониторинг и быстрые переходы по разделам
    public class HomePanel : Panel
    {
        public event Action<string> RequestNavigate;

        private StatTile cpuTile;
        private StatTile ramTile;
        private StatTile cpuTempTile;
        private StatTile gpuTempTile;
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
            // Живой мониторинг: значение и график нагрузки за последние ~80 секунд
            cpuTile = new StatTile()
            { Title = Lang.T("Загрузка CPU", "CPU load"), Location = new Point(24, 24) };
            Controls.Add(cpuTile);

            ramTile = new StatTile()
            { Title = Lang.T("Загрузка RAM", "RAM load"), Location = new Point(206, 24) };
            Controls.Add(ramTile);

            cpuTempTile = new StatTile()
            { Title = Lang.T("Темп. CPU", "CPU temp"), Location = new Point(388, 24) };
            Controls.Add(cpuTempTile);

            gpuTempTile = new StatTile()
            { Title = Lang.T("Темп. GPU", "GPU temp"), Location = new Point(570, 24) };
            Controls.Add(gpuTempTile);

            if (AppConfig.GetBool("widget_visible", false)) SetWidgetVisible(true);

            // Быстрые переходы
            var quickTitle = new Label
            {
                Text = Lang.T("Быстрые действия", "Quick actions"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(24, 192),
                AutoSize = true
            };
            Controls.Add(quickTitle);

            // Иконки только из базовой части юникода (BMP): эмодзи вроде 🧹 или 🚀
            // GDI-отрисовка превращает в пустые квадраты
            MakeQuickCard(new Point(24, 224), "♻",
                Lang.T("Очистить мусор", "Clean junk"),
                Lang.T("Temp-файлы и кэш", "Temp files and cache"), "cleanup");
            MakeQuickCard(new Point(265, 224), "☄",
                "GameBooster",
                Lang.T("Буст игр и Ultimate Performance", "Game boost and Ultimate Performance"), "booster");
            MakeQuickCard(new Point(506, 224), "❤",
                "Health Check",
                Lang.T("Датчики и диски", "Sensors and disks"), "health");
            MakeQuickCard(new Point(24, 348), "⚡",
                Lang.T("Автокликер", "Autoclicker"),
                Lang.T("Мышь и клавиатура, ", "Mouse and keyboard, ") + Hotkeys.Get(NativeMethods.HOTKEY_ID_CLICKER), "clicker");
            MakeQuickCard(new Point(265, 348), "◉",
                Lang.T("Скриншоты", "Screenshots"),
                Lang.T("Снимок экрана по ", "Capture screen with ") + Hotkeys.Get(NativeMethods.HOTKEY_ID_SCREENSHOT), "screenshots");
            MakeQuickCard(new Point(506, 348), "⚙",
                Lang.T("Настройки", "Settings"),
                Lang.T("Тема, язык, права", "Theme, language, rights"), "settings");

            var ramBtn = new RoundedButton
            {
                Text = Lang.T("🧹 Освободить RAM", "🧹 Free RAM"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(24, 482),
                Size = new Size(190, 34)
            };
            ramBtn.Click += (s, e) => FreeRam(false);
            Controls.Add(ramBtn);

            // Автоочистка RAM по расписанию: тумблер + выбор интервала.
            // Таймер живёт в HomePanel, а панель создаётся при старте и не умирает -
            // так что расписание работает, даже когда окно свёрнуто в трей
            ramAutoToggle = new ToggleSwitch { Location = new Point(230, 486), Checked = AppConfig.GetBool("ram_auto_on", false) };
            ramAutoToggle.CheckedChanged += (s, e) => {
                AppConfig.SetBool("ram_auto_on", ramAutoToggle.Checked);
                ApplyAutoCleanTimer();
            };
            Controls.Add(ramAutoToggle);

            var ramAutoLabel = new Label
            {
                Text = Lang.T("Автоочистка каждые:", "Auto-clean every:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(284, 490),
                AutoSize = true
            };
            Controls.Add(ramAutoLabel);

            int[] intervals = { 5, 10, 30, 60 };
            int ix = 420;
            for (int i = 0; i < intervals.Length; i++)
            {
                int minutesVal = intervals[i];
                var b = new RoundedButton
                {
                    Text = minutesVal + Lang.T("м", "m"),
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                    Location = new Point(ix, 484),
                    Size = new Size(42, 28),
                    Tag = minutesVal
                };
                b.Click += (s, e) => {
                    AppConfig.Set("ram_auto_min", minutesVal.ToString());
                    // Выбор интервала сразу включает автоочистку, если она была выключена
                    if (!ramAutoToggle.Checked)
                    {
                        ramAutoToggle.Checked = true;
                        ramAutoToggle.Invalidate();
                        AppConfig.SetBool("ram_auto_on", true);
                    }
                    HighlightIntervalButtons();
                    ApplyAutoCleanTimer();
                };
                Controls.Add(b);
                intervalButtons.Add(b);
                ix += 46;
            }
            HighlightIntervalButtons();

            // Виджет мониторинга переехал из убранной шапки: компактно в правом
            // нижнем углу, чтобы не терять доступ к функции
            var widgetLabel = new Label
            {
                Text = Lang.T("Виджет", "Widget"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(614, 490),
                AutoSize = true
            };
            Controls.Add(widgetLabel);

            var widgetToggle = new ToggleSwitch { Location = new Point(662, 484), Checked = AppConfig.GetBool("widget_visible", false) };
            widgetToggle.CheckedChanged += (s, e) => SetWidgetVisible(widgetToggle.Checked);
            Controls.Add(widgetToggle);

            ramResultLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(24, 526),
                Size = new Size(690, 18),
                AutoEllipsis = true
            };
            Controls.Add(ramResultLabel);

            // Подсказка из убранной шапки: окно можно закрыть, программа останется
            // в трее, а хоткеи работают всегда
            var hintLabel = new Label
            {
                Text = Lang.T("Программа живёт в трее: закрытие окна не выключает её. ", "Lives in the tray: closing the window does not exit. ")
                       + Hotkeys.Get(NativeMethods.HOTKEY_ID_CLICKER) + Lang.T(" - кликер, ", " - clicker, ")
                       + Hotkeys.Get(NativeMethods.HOTKEY_ID_SCREENSHOT) + Lang.T(" - скриншот", " - screenshot"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 560),
                AutoSize = true
            };
            Controls.Add(hintLabel);

            autoCleanTimer = new System.Windows.Forms.Timer();
            autoCleanTimer.Tick += (s, e) => AutoCleanTick();
            ApplyAutoCleanTimer();
        }

        private Label ramResultLabel;
        private ToggleSwitch ramAutoToggle;
        private System.Windows.Forms.Timer autoCleanTimer;
        private readonly System.Collections.Generic.List<RoundedButton> intervalButtons =
            new System.Collections.Generic.List<RoundedButton>();
        private bool ramCleaning = false;

        private int AutoCleanMinutes()
        {
            int min;
            if (!int.TryParse(AppConfig.Get("ram_auto_min", "10"), out min)) min = 10;
            return Math.Max(1, min);
        }

        private void HighlightIntervalButtons()
        {
            int current = AutoCleanMinutes();
            foreach (RoundedButton b in intervalButtons)
            {
                bool selected = (int)b.Tag == current;
                b.ButtonColor = selected ? Theme.Accent : Theme.InputColor;
                b.HoverColor = selected ? Theme.AccentHover : Theme.KeyHover;
                b.TextColor = selected ? Theme.BgColor : Theme.TextDim;
                b.Invalidate();
            }
        }

        private void ApplyAutoCleanTimer()
        {
            autoCleanTimer.Stop();
            if (ramAutoToggle.Checked)
            {
                autoCleanTimer.Interval = AutoCleanMinutes() * 60000;
                autoCleanTimer.Start();
            }
        }

        private void AutoCleanTick()
        {
            // Во время игры не трогаем память: EmptyWorkingSet выдернет страницы
            // из-под игры, и она заикнётся - подождём следующего тика
            if (GameActivity.IsGameActive) return;
            if (ramCleaning) return;
            FreeRam(true);
        }

        private void FreeRam(bool auto)
        {
            if (ramCleaning) return;
            ramCleaning = true;
            if (!auto)
            {
                ramResultLabel.ForeColor = Theme.TextDim;
                ramResultLabel.Text = Lang.T("Освобождаю память...", "Freeing memory...");
            }
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => e.Result = RamCleaner.Clean();
            worker.RunWorkerCompleted += (s, e) => {
                ramCleaning = false;
                long freed = e.Error == null && e.Result != null ? (long)e.Result : 0;
                ramResultLabel.ForeColor = Theme.Accent;
                string result = freed > 0
                    ? Lang.T("Освобождено ~", "Freed ~") + freed + Lang.T(" МБ", " MB")
                    : Lang.T("Память уже оптимальна", "Memory already optimal");
                if (auto)
                    result = Lang.T("Автоочистка в ", "Auto-clean at ") + DateTime.Now.ToString("HH:mm") + ": " + result;
                ramResultLabel.Text = result;
            };
            worker.RunWorkerAsync();
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
            cpuTile.Push(SystemStats.CpuLoadNum, SystemStats.CpuLoad);
            ramTile.Push(SystemStats.RamLoadNum, SystemStats.RamLoad);
            cpuTempTile.Push(SystemStats.CpuTempNum, SystemStats.CpuTemp);
            gpuTempTile.Push(SystemStats.GpuTempNum, SystemStats.GpuTemp);
        }
    }
}
