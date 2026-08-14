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
        private System.Windows.Forms.Timer statsAnimTimer;
        private WidgetForm widget;

        // Плавная анимация цифр мониторинга: значение не прыгает 37 -> 62,
        // а быстро «докручивается», как спидометр
        private class StatAnim
        {
            public Label Label;
            public float Shown = -1;  // -1 = число ещё не показывали, первый раз без анимации
            public float Target = -1;
            public string Suffix = "";
            public bool Numeric = false;
            public bool IsTemp = false;
        }

        private readonly System.Collections.Generic.List<StatAnim> statAnims =
            new System.Collections.Generic.List<StatAnim>();

        public HomePanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            BuildUi();

            statAnims.Add(new StatAnim { Label = cpuValue });
            statAnims.Add(new StatAnim { Label = ramValue });
            statAnims.Add(new StatAnim { Label = cpuTempValue, IsTemp = true });
            statAnims.Add(new StatAnim { Label = gpuTempValue, IsTemp = true });

            statsTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            statsTimer.Tick += (s, e) => RefreshStats();
            statsTimer.Start();

            statsAnimTimer = new System.Windows.Forms.Timer { Interval = 30 };
            statsAnimTimer.Tick += (s, e) => AnimateStats();
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
            ramBtn.Click += (s, e) => FreeRam(false);
            Controls.Add(ramBtn);

            // Автоочистка RAM по расписанию: тумблер + выбор интервала.
            // Таймер живёт в HomePanel, а панель создаётся при старте и не умирает -
            // так что расписание работает, даже когда окно свёрнуто в трей
            ramAutoToggle = new ToggleSwitch { Location = new Point(230, 520), Checked = AppConfig.GetBool("ram_auto_on", false) };
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
                Location = new Point(284, 524),
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
                    Location = new Point(ix, 518),
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

            ramResultLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(24, 560),
                Size = new Size(690, 18),
                AutoEllipsis = true
            };
            Controls.Add(ramResultLabel);

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
            if (WinKeyBlocker.GameActive) return;
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

            SetStatTarget(statAnims[0], SystemStats.CpuLoad);
            SetStatTarget(statAnims[1], SystemStats.RamLoad);
            SetStatTarget(statAnims[2], SystemStats.CpuTemp);
            SetStatTarget(statAnims[3], SystemStats.GpuTemp);

            if (!statsAnimTimer.Enabled) statsAnimTimer.Start();
        }

        // Разбирает "62°C" / "37%" на число и суффикс. Не число ("—") - показываем как есть
        private static void ParseStat(string raw, out float value, out string suffix, out bool ok)
        {
            value = 0; suffix = ""; ok = false;
            if (string.IsNullOrEmpty(raw)) return;
            int i = 0;
            while (i < raw.Length && char.IsDigit(raw[i])) i++;
            if (i == 0) return;
            float v;
            if (!float.TryParse(raw.Substring(0, i), out v)) return;
            value = v;
            suffix = raw.Substring(i);
            ok = true;
        }

        private void SetStatTarget(StatAnim stat, string raw)
        {
            float value; string suffix; bool ok;
            ParseStat(raw, out value, out suffix, out ok);

            if (!ok)
            {
                stat.Numeric = false;
                stat.Shown = -1;
                stat.Label.Text = raw;
                stat.Label.ForeColor = Theme.TextMain;
                return;
            }

            stat.Numeric = true;
            stat.Suffix = suffix;
            stat.Target = value;
            if (stat.Shown < 0) // первое значение - сразу, без «раскрутки» с нуля
            {
                stat.Shown = value;
                ApplyStat(stat);
            }
        }

        private void AnimateStats()
        {
            bool anyMoving = false;
            foreach (StatAnim stat in statAnims)
            {
                if (!stat.Numeric || stat.Shown < 0) continue;
                float d = stat.Target - stat.Shown;
                if (Math.Abs(d) < 0.5f)
                {
                    if (stat.Shown != stat.Target) { stat.Shown = stat.Target; ApplyStat(stat); }
                    continue;
                }
                anyMoving = true;
                stat.Shown += d * 0.25f;
                ApplyStat(stat);
            }
            if (!anyMoving) statsAnimTimer.Stop();
        }

        private void ApplyStat(StatAnim stat)
        {
            int shown = (int)Math.Round(stat.Shown);
            stat.Label.Text = shown + stat.Suffix;

            // Подсветка по серьёзности: температуры и загрузка краснеют по-разному
            if (stat.IsTemp)
                stat.Label.ForeColor = shown >= 80 ? Theme.Danger : (shown >= 65 ? Theme.Warning : Theme.TextMain);
            else
                stat.Label.ForeColor = shown >= 90 ? Theme.Danger : (shown >= 75 ? Theme.Warning : Theme.TextMain);
        }
    }
}
