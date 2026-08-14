using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DeepTools
{
    public class GameBoosterPanel : Panel
    {
        private const int OptRowHeight = 50;
        private const int OptRightReserve = 140;

        private static readonly string[] KnownHeavyApps = new string[]
        {
            "chrome", "msedge", "firefox", "opera", "yandexbrowser",
            "discord", "spotify", "epicgameslauncher", "origin", "uplay",
            "steamwebhelper", "skype", "teams", "telegram", "whatsapp"
        };

        private System.Windows.Forms.Timer detectTimer = new System.Windows.Forms.Timer();
        private Process detectedProcess;
        private Process lastBoostedProcess;

        private Label statusName;
        private Label warningLabel;
        private ToggleSwitch autoBoostToggle;
        private ToggleSwitch overlayToggle;
        private OverlayForm overlayForm;
        private RoundedButton boostBtn;
        private Label statusLabel;

        private ToggleSwitch ultimateToggle;
        private bool ultimateBusy = false;

        // Включение/выключение плана Ultimate Performance. powercfg работает быстро,
        // но на всякий случай защищаемся от повторного клика во время применения
        private void OnUltimateToggle()
        {
            if (ultimateBusy) return;
            ultimateBusy = true;
            bool enable = ultimateToggle.Checked;

            bool ok = enable ? PowerPlan.EnableUltimate() : PowerPlan.RestorePrevious();

            if (ok)
            {
                statusLabel.Text = enable
                    ? Lang.T("План Ultimate Performance активирован, парковка ядер отключена", "Ultimate Performance plan activated, core parking disabled")
                    : Lang.T("Возвращён прежний план питания", "Previous power plan restored");
                statusLabel.ForeColor = Theme.Accent;
            }
            else
            {
                statusLabel.Text = Lang.T("Не удалось изменить план питания", "Failed to change power plan");
                statusLabel.ForeColor = Theme.Warning;
                ultimateToggle.Checked = !enable;
                ultimateToggle.Invalidate();
            }
            ultimateBusy = false;
        }

        private ToggleSwitch dvrToggle;
        private ToggleSwitch gpuPinToggle;
        private RoundedButton freezeBtn;
        // Путь к exe игры, для которой уже проверяли/ставили GpuPreference -
        // чтобы не дёргать реестр на каждый тик детектора (1.5 сек)
        private string lastGpuPinnedPath;

        // Отключение фоновой записи Game DVR / Game Bar - применяется сразу и глобально
        private void OnDvrToggle()
        {
            bool disable = dvrToggle.Checked;
            if (GameOptimizer.SetDvrDisabled(disable))
            {
                statusLabel.Text = disable
                    ? Lang.T("Game DVR отключён - фоновая запись Game Bar больше не ест FPS", "Game DVR disabled - Game Bar background recording no longer eats FPS")
                    : Lang.T("Game DVR снова включён", "Game DVR re-enabled");
                statusLabel.ForeColor = Theme.Accent;
            }
            else
            {
                statusLabel.Text = Lang.T("Не удалось изменить настройки Game DVR", "Failed to change Game DVR settings");
                statusLabel.ForeColor = Theme.Warning;
                dvrToggle.Checked = !disable;
                dvrToggle.Invalidate();
            }
        }

        // Закрепление обнаруженной игры за дискретной GPU (профиль «Высокая
        // производительность» в настройках графики Windows). Игра должна быть
        // перезапущена, чтобы выбор GPU вступил в силу
        private void TryPinGpu(Process proc)
        {
            string path;
            try { path = proc.MainModule.FileName; }
            catch { return; } // 32/64-битное несоответствие или процесс уже вышел

            if (string.IsNullOrEmpty(path) || path == lastGpuPinnedPath) return;
            lastGpuPinnedPath = path;

            if (GameOptimizer.HasGpuPreference(path)) return;
            if (GameOptimizer.SetGpuPreference(path))
            {
                statusLabel.Text = proc.ProcessName + ".exe " +
                    Lang.T("закреплена за дискретной GPU (нужен перезапуск игры)", "pinned to the discrete GPU (restart the game to apply)");
                statusLabel.ForeColor = Theme.Accent;
            }
        }

        private ToggleSwitch crosshairToggle;
        private CrosshairForm crosshairForm;
        private string crossShape = "cross";
        private Color crossColor = Color.FromArgb(46, 214, 140);
        private int crossScale = 2;
        private List<RoundedButton> shapeButtons = new List<RoundedButton>();
        private List<RoundedButton> sizeButtons = new List<RoundedButton>();

        // Пересоздать прицел с текущими настройками (форма/цвет/размер меняются только пересозданием)
        private void ApplyCrosshair()
        {
            if (crosshairForm != null && !crosshairForm.IsDisposed)
            {
                crosshairForm.Close();
                crosshairForm = null;
            }
            if (crosshairToggle.Checked)
            {
                crosshairForm = new CrosshairForm(crossShape, crossColor, crossScale);
                crosshairForm.Show();
            }
            SaveCrosshairConfig();
        }

        private void SaveAndRefreshCrosshair()
        {
            // Если прицел выключен - выбор настроек его сразу включает
            if (!crosshairToggle.Checked)
            {
                crosshairToggle.Checked = true;
                crosshairToggle.Invalidate();
            }
            ApplyCrosshair();
        }

        // Показать/скрыть FPS-оверлей. Форма создаётся заново при каждом включении -
        // так проще, чем следить за состоянием после закрытия
        public void SetOverlayVisible(bool visible)
        {
            if (visible)
            {
                if (overlayForm == null || overlayForm.IsDisposed)
                {
                    overlayForm = new OverlayForm();
                }
                overlayForm.Show();
            }
            else
            {
                if (overlayForm != null && !overlayForm.IsDisposed)
                {
                    overlayForm.Close();
                    overlayForm = null;
                }
            }
            if (overlayToggle.Checked != visible)
            {
                overlayToggle.Checked = visible;
                overlayToggle.Invalidate();
            }
            AppConfig.SetBool("overlay_on", visible);
        }

        public void ToggleOverlay()
        {
            SetOverlayVisible(overlayForm == null || overlayForm.IsDisposed);
        }

        // Пересоздать оверлей с новыми настройками, если он сейчас показан
        public void RefreshOverlay()
        {
            if (overlayForm != null && !overlayForm.IsDisposed)
            {
                SetOverlayVisible(false);
                SetOverlayVisible(true);
            }
        }

        private FlowLayoutPanel heavyList;
        private List<CheckBox> heavyChecks = new List<CheckBox>();
        private List<List<Process>> heavyGroups = new List<List<Process>>();

        public GameBoosterPanel()
        {
            Size = new Size(760, 616);
            AutoScroll = true;
            BackColor = Theme.BgColor;
            NativeMethods.ApplyDarkScrollbar(this);

            detectTimer.Interval = 1500;
            detectTimer.Tick += (s, e) => DetectFullscreenGame();

            // ETW-трейсер Present-кадров: нужен детекту, чтобы отличать
            // borderless-игру от просто развёрнутого окна (браузер и т.п.)
            PresentTracer.Start();

            BuildUi();
            LoadPersistedState();
            RefreshHeavyList();

            detectTimer.Start();
        }

        // Восстановление прицела и оверлея после перезапуска программы
        private void LoadPersistedState()
        {
            crossShape = AppConfig.Get("cross_shape", "cross");
            int argb;
            if (int.TryParse(AppConfig.Get("cross_color", ""), out argb)) crossColor = Color.FromArgb(argb);
            int sc;
            if (int.TryParse(AppConfig.Get("cross_scale", "2"), out sc)) crossScale = Math.Max(1, Math.Min(3, sc));

            if (AppConfig.GetBool("cross_on", false))
            {
                crosshairToggle.Checked = true;
                crosshairToggle.Invalidate();
                ApplyCrosshair();
            }
            if (AppConfig.GetBool("overlay_on", false)) SetOverlayVisible(true);
        }

        private void SaveCrosshairConfig()
        {
            AppConfig.Set("cross_shape", crossShape);
            AppConfig.Set("cross_color", crossColor.ToArgb().ToString());
            AppConfig.Set("cross_scale", crossScale.ToString());
            AppConfig.SetBool("cross_on", crosshairToggle.Checked);
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = "GameBooster",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var gameTimeBtn = new RoundedButton
            {
                Text = Lang.T("Время в играх", "Game time"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(596, 20),
                Size = new Size(140, 32)
            };
            gameTimeBtn.Click += (s, e) => {
                using (var f = new GameTimeForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(gameTimeBtn);

            var detectCard = Theme.MakeCard(this, new Point(24, 60), new Size(712, 130));

            var detectTitle = new Label
            {
                Text = Lang.T("Обнаруженный процесс", "Detected process"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 14),
                AutoSize = true
            };
            detectCard.Controls.Add(detectTitle);

            statusName = new Label
            {
                Text = Lang.T("Игра не обнаружена", "No game detected"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(20, 36),
                AutoSize = true
            };
            detectCard.Controls.Add(statusName);

            warningLabel = new Label
            {
                Text = Lang.T("Игра определяется в полноэкранном и borderless режиме", "The game is detected in fullscreen and borderless mode"),
                ForeColor = Theme.Warning,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 66),
                Size = new Size(420, 32)
            };
            detectCard.Controls.Add(warningLabel);

            boostBtn = new RoundedButton
            {
                Text = Lang.T("Поднять приоритет", "Boost priority"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(480, 34),
                Size = new Size(200, 36),
                Enabled = false
            };
            boostBtn.Click += (s, e) => {
                if (detectedProcess != null) BoostProcess(detectedProcess);
            };
            detectCard.Controls.Add(boostBtn);

            var autoLabel = new Label
            {
                Text = Lang.T("Авто-буст новой игры", "Auto-boost new game"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(480, 78),
                AutoSize = true
            };
            detectCard.Controls.Add(autoLabel);

            autoBoostToggle = new ToggleSwitch { Location = new Point(660, 74), Checked = false };
            detectCard.Controls.Add(autoBoostToggle);

            var overlayLabel = new Label
            {
                Text = Lang.T("FPS-оверлей (FPS, CPU, GPU, RAM в углу экрана)", "FPS overlay (FPS, CPU, GPU, RAM in screen corner)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 100),
                AutoSize = true
            };
            detectCard.Controls.Add(overlayLabel);

            overlayToggle = new ToggleSwitch { Location = new Point(380, 96), Checked = false };
            overlayToggle.CheckedChanged += (s, e) => SetOverlayVisible(overlayToggle.Checked);
            detectCard.Controls.Add(overlayToggle);

            var overlayCfgBtn = new RoundedButton
            {
                Text = "⚙",
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(430, 96),
                Size = new Size(40, 24)
            };
            overlayCfgBtn.Click += (s, e) => {
                using (var f = new OverlayConfigForm())
                {
                    f.SettingsChanged += (s2, e2) => RefreshOverlay();
                    f.ShowDialog(FindForm());
                }
            };
            detectCard.Controls.Add(overlayCfgBtn);

            var winKeyLabel = new Label
            {
                Text = Lang.T("Блокировать Win в игре", "Block Win key in game"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(480, 104),
                AutoSize = true
            };
            detectCard.Controls.Add(winKeyLabel);

            var winKeyToggle = new ToggleSwitch { Location = new Point(660, 100), Checked = WinKeyBlocker.Enabled };
            winKeyToggle.CheckedChanged += (s, e) => { WinKeyBlocker.Enabled = winKeyToggle.Checked; };
            detectCard.Controls.Add(winKeyToggle);

            // Карточка прицела
            var crossCard = Theme.MakeCard(this, new Point(24, 202), new Size(712, 64));

            var crossTitle = new Label
            {
                Text = Lang.T("Прицел", "Crosshair"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(18, 20),
                AutoSize = true
            };
            crossCard.Controls.Add(crossTitle);

            crosshairToggle = new ToggleSwitch { Location = new Point(84, 18), Checked = false };
            crosshairToggle.CheckedChanged += (s, e) => ApplyCrosshair();
            crossCard.Controls.Add(crosshairToggle);

            string[] shapes = { "cross", "dot", "circle", "tshape" };
            string[] shapeLabels = { "✚", "•", "◯", "┬" };
            int sx = 150;
            for (int i = 0; i < shapes.Length; i++)
            {
                string shapeVal = shapes[i];
                var b = new RoundedButton
                {
                    Text = shapeLabels[i],
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Location = new Point(sx, 16),
                    Size = new Size(36, 30)
                };
                b.Click += (s, e) => { crossShape = shapeVal; SaveAndRefreshCrosshair(); };
                crossCard.Controls.Add(b);
                shapeButtons.Add(b);
                sx += 40;
            }

            Color[] colors = {
                Color.FromArgb(46, 214, 140), Color.FromArgb(240, 70, 70),
                Color.FromArgb(80, 170, 255), Color.FromArgb(250, 230, 60), Color.White
            };
            sx = 330;
            for (int i = 0; i < colors.Length; i++)
            {
                Color colorVal = colors[i];
                var b = new RoundedButton
                {
                    Text = "",
                    ButtonColor = colorVal,
                    HoverColor = colorVal,
                    Location = new Point(sx, 19),
                    Size = new Size(24, 24)
                };
                b.Click += (s, e) => { crossColor = colorVal; SaveAndRefreshCrosshair(); };
                crossCard.Controls.Add(b);
                sx += 30;
            }

            string[] sizeLabels = { "S", "M", "L" };
            sx = 500;
            for (int i = 0; i < 3; i++)
            {
                int scaleVal = i + 1;
                var b = new RoundedButton
                {
                    Text = sizeLabels[i],
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Location = new Point(sx, 16),
                    Size = new Size(32, 30)
                };
                b.Click += (s, e) => { crossScale = scaleVal; SaveAndRefreshCrosshair(); };
                crossCard.Controls.Add(b);
                sizeButtons.Add(b);
                sx += 36;
            }

            // Карточка оптимизаций: план питания, Game DVR, дискретная GPU
            const int optRowStep = 50;
            var powerCard = Theme.MakeCard(this, new Point(24, 278), new Size(712, 12 + optRowStep * 3 + 8));

            MakeOptRow(powerCard, 12, "Ultimate Performance",
                Lang.T("Скрытый план питания Windows для максимальной производительности + отключение парковки ядер CPU",
                       "Hidden Windows power plan for maximum performance + CPU core parking disabled"));
            ultimateToggle = new ToggleSwitch { Location = new Point(646, OptRowToggleY(12)), Checked = PowerPlan.IsUltimateActive() };
            ultimateToggle.CheckedChanged += (s, e) => OnUltimateToggle();
            powerCard.Controls.Add(ultimateToggle);

            MakeOptRow(powerCard, 12 + optRowStep, Lang.T("Отключить Game DVR", "Disable Game DVR"),
                Lang.T("Фоновая запись Xbox Game Bar захватывает кадры даже без записи - реальный минус к FPS",
                       "Xbox Game Bar background capture grabs frames even when idle - a real FPS cost"));
            dvrToggle = new ToggleSwitch { Location = new Point(646, OptRowToggleY(12 + optRowStep)), Checked = GameOptimizer.IsDvrDisabled() };
            dvrToggle.CheckedChanged += (s, e) => OnDvrToggle();
            powerCard.Controls.Add(dvrToggle);

            MakeOptRow(powerCard, 12 + optRowStep * 2, Lang.T("Дискретная GPU для игр", "Discrete GPU for games"),
                Lang.T("Обнаруженная игра автоматически закрепляется за мощной видеокартой (для ноутбуков с двумя GPU)",
                       "The detected game is auto-pinned to the powerful GPU (for laptops with dual GPUs)"));
            gpuPinToggle = new ToggleSwitch { Location = new Point(646, OptRowToggleY(12 + optRowStep * 2)), Checked = AppConfig.GetBool("gpu_auto_pin", false) };
            gpuPinToggle.CheckedChanged += (s, e) => {
                AppConfig.SetBool("gpu_auto_pin", gpuPinToggle.Checked);
                lastGpuPinnedPath = null; // чтобы применилось к уже запущенной игре
            };
            powerCard.Controls.Add(gpuPinToggle);

            var heavyCard = Theme.MakeCard(this, new Point(24, 462), new Size(712, 152));

            var heavyTitle = new Label
            {
                Text = Lang.T("Тяжёлый фон", "Heavy background"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(18, 16),
                AutoSize = true
            };
            heavyCard.Controls.Add(heavyTitle);

            var heavyDesc = new Label
            {
                Text = Lang.T("«Заморозить» = пауза без потерь (❄), та же кнопка размораживает. «Завершить» закрывает программу.", "Freeze pauses checked apps with no data loss (❄), same button unfreezes. Kill closes them."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 46),
                Size = new Size(676, 18),
                AutoEllipsis = true
            };
            heavyCard.Controls.Add(heavyDesc);

            var refreshBtn = new RoundedButton
            {
                Text = Lang.T("Обновить", "Refresh"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(360, 10),
                Size = new Size(100, 32)
            };
            refreshBtn.Click += (s, e) => RefreshHeavyList();
            heavyCard.Controls.Add(refreshBtn);

            freezeBtn = new RoundedButton
            {
                Text = Lang.T("Заморозить", "Freeze"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(468, 10),
                Size = new Size(116, 32)
            };
            freezeBtn.Click += (s, e) => FreezeOrResume();
            heavyCard.Controls.Add(freezeBtn);

            var killBtn = new RoundedButton
            {
                Text = Lang.T("Завершить", "Kill"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Location = new Point(592, 10),
                Size = new Size(102, 32)
            };
            killBtn.Click += (s, e) => TerminateSelected();
            heavyCard.Controls.Add(killBtn);

            heavyList = new FlowLayoutPanel
            {
                Location = new Point(18, 66),
                Size = new Size(676, 76),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            heavyCard.Controls.Add(heavyList);
            NativeMethods.ApplyDarkScrollbar(heavyList);

            // ---- Карточка: Адаптивный профиль питания + VRAM Defrag ----
            int smartCardY = heavyCard.Bottom + 12;
            var smartCard = Theme.MakeCard(this, new Point(24, smartCardY), new Size(712, 12 + optRowStep * 2 + 8));

            MakeOptRow(smartCard, 12,
                Lang.T("Адаптивный профиль питания", "Adaptive Power Profile"),
                Lang.T("Ultimate в игре, Balanced в браузере, экономия на простое — автоматически",
                       "Ultimate in-game, Balanced on browser, saver on idle — automatic"));
            var adaptiveToggle = new ToggleSwitch
            {
                Location = new Point(646, OptRowToggleY(12)),
                Checked = AppConfig.GetBool("adaptive_power", false)
            };
            adaptiveToggle.CheckedChanged += (s, e) => {
                AdaptivePowerProfile.Enabled = adaptiveToggle.Checked;
                AppConfig.SetBool("adaptive_power", adaptiveToggle.Checked);
                statusLabel.Text = adaptiveToggle.Checked
                    ? Lang.T("Адаптивный профиль питания включён", "Adaptive power profile enabled")
                    : Lang.T("Адаптивный профиль питания выключен", "Adaptive power profile disabled");
                statusLabel.ForeColor = Theme.Accent;
            };
            smartCard.Controls.Add(adaptiveToggle);

            MakeOptRow(smartCard, 12 + optRowStep,
                Lang.T("Дефрагментация VRAM", "VRAM Defrag"),
                Lang.T("Сбрасывает фрагментированную видеопамять через DirectX — убирает подвисания в открытых мирах",
                       "Flushes fragmented video memory via DirectX — eliminates stuttering in open-world games"));
            var vramBtn = new RoundedButton
            {
                Text = Lang.T("Очистить VRAM", "Defrag VRAM"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(574, OptRowControlY(12 + optRowStep, 28)),
                Size = new Size(120, 28)
            };
            vramBtn.Click += (s, e) => {
                vramBtn.Text = "...";
                vramBtn.Enabled = false;
                var bw = new System.ComponentModel.BackgroundWorker();
                bw.DoWork += (s2, e2) => VramDefrag.Run();
                bw.RunWorkerCompleted += (s2, e2) => {
                    vramBtn.Text = Lang.T("Очистить VRAM", "Defrag VRAM");
                    vramBtn.Enabled = true;
                    statusLabel.Text = VramDefrag.LastResult
                        ? Lang.T("VRAM очищена", "VRAM defragmented")
                        : ("VRAM defrag: " + VramDefrag.LastMessage);
                    statusLabel.ForeColor = VramDefrag.LastResult ? Theme.Accent : Theme.Warning;
                };
                bw.RunWorkerAsync();
            };
            smartCard.Controls.Add(vramBtn);

            // ---- Карточка: RGB-реакция ----
            var rgbCard = Theme.MakeCard(this, new Point(24, smartCard.Bottom + 12), new Size(712, 12 + optRowStep + 8));

            MakeOptRow(rgbCard, 12,
                Lang.T("RGB-реакция (OpenRGB)", "RGB Reactive (OpenRGB)"),
                Lang.T("Цвет подсветки следует температуре CPU/GPU, мигает красным при просадке FPS",
                       "Lighting colour tracks CPU/GPU temp, flashes red on FPS drops"));
            var rgbToggle = new ToggleSwitch
            {
                Location = new Point(646, OptRowToggleY(12)),
                Checked = AppConfig.GetBool("rgb_reactive", false)
            };
            rgbToggle.CheckedChanged += (s, e) => {
                RgbReactive.Enabled = rgbToggle.Checked;
                AppConfig.SetBool("rgb_reactive", rgbToggle.Checked);
                statusLabel.Text = rgbToggle.Checked
                    ? Lang.T("RGB-реакция включена (нужен OpenRGB с HTTP-сервером)", "RGB Reactive on (requires OpenRGB with HTTP server)")
                    : Lang.T("RGB-реакция выключена", "RGB Reactive disabled");
                statusLabel.ForeColor = Theme.Accent;
            };
            rgbCard.Controls.Add(rgbToggle);

            // Строка статуса под последней карточкой — создаётся после rgbCard,
            // потому что её Y считается от rgbCard.Bottom
            statusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, rgbCard.Bottom + 8),
                AutoSize = true
            };
            Controls.Add(statusLabel);
        }

        // Строка карточки оптимизаций: заголовок сверху, описание под ним —
        // фиксированный x=215 ломался на длинных русских названиях
        private void MakeOptRow(Panel card, int y, string title, string desc)
        {
            int contentW = card.Width - 18 - OptRightReserve;

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(18, y + 2),
                AutoSize = true,
                MaximumSize = new Size(contentW, 0)
            };
            card.Controls.Add(titleLbl);

            var descLbl = new Label
            {
                Text = desc,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(18, y + 22),
                AutoSize = true,
                MaximumSize = new Size(contentW, 0)
            };
            card.Controls.Add(descLbl);
        }

        private static int OptRowToggleY(int rowY)
        {
            return rowY + (OptRowHeight - 24) / 2;
        }

        private static int OptRowControlY(int rowY, int controlHeight)
        {
            return rowY + (OptRowHeight - controlHeight) / 2;
        }

        // Одна кнопка на два действия: если что-то заморожено - размораживаем всё,
        // иначе замораживаем отмеченные группы
        private void FreezeOrResume()
        {
            if (BackgroundFreezer.FrozenCount > 0)
            {
                int woken = BackgroundFreezer.ResumeAll();
                statusLabel.Text = Lang.T("Разморожено процессов: ", "Processes unfrozen: ") + woken;
                statusLabel.ForeColor = Theme.Accent;
            }
            else
            {
                int frozenGroups = 0;
                int frozenProcs = 0;
                for (int i = 0; i < heavyChecks.Count; i++)
                {
                    if (!heavyChecks[i].Checked) continue;
                    int done = BackgroundFreezer.Freeze(heavyGroups[i]);
                    if (done > 0) frozenGroups++;
                    frozenProcs += done;
                }

                if (frozenProcs > 0)
                {
                    statusLabel.Text = Lang.T("Заморожено программ: ", "Apps frozen: ") + frozenGroups +
                        " (" + frozenProcs + Lang.T(" процессов). Окна не отвечают, пока заморожены - это нормально", " processes). Windows stay unresponsive while frozen - that's expected");
                    statusLabel.ForeColor = Theme.Accent;
                }
                else
                {
                    statusLabel.Text = Lang.T("Ничего не выбрано или не удалось заморозить", "Nothing selected or failed to freeze");
                    statusLabel.ForeColor = Theme.Warning;
                }
            }
            UpdateFreezeButton();
            RefreshHeavyList();
        }

        private void UpdateFreezeButton()
        {
            freezeBtn.Text = BackgroundFreezer.FrozenCount > 0
                ? Lang.T("Разморозить", "Unfreeze")
                : Lang.T("Заморозить", "Freeze");
            freezeBtn.Invalidate();
        }

        private void DetectFullscreenGame()
        {
            // Трекер сессий живёт своей жизнью: следит за процессом игры,
            // даже когда она свёрнута, и сам finalize'ит сессию после выхода
            GameSessionTracker.Tick();

            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) { ClearDetection(); return; }

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hwnd, out rect)) { ClearDetection(); return; }

            Screen screen;
            try { screen = Screen.FromHandle(hwnd); }
            catch { ClearDetection(); return; }

            Rectangle bounds = screen.Bounds;
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            bool isFullscreen = rect.Left <= bounds.Left + 2 && rect.Top <= bounds.Top + 2 &&
                                 width >= bounds.Width - 4 && height >= bounds.Height - 4;

            uint pid;
            NativeMethods.GetWindowThreadProcessId(hwnd, out pid);

            // Borderless/windowed: геометрия не совпадает с экраном пиксель в
            // пиксель, поэтому смотрим шире - окно занимает большую часть экрана
            // И процесс реально рисует кадры (Present-события из ETW-трейсера)
            if (!isFullscreen)
            {
                bool bigWindow = width >= bounds.Width * 7 / 10 && height >= bounds.Height * 7 / 10;
                if (bigWindow && PresentTracer.IsRendering((int)pid, 15))
                    isFullscreen = true;
            }

            if (!isFullscreen) { WinKeyBlocker.GameActive = false; ClearDetection(); return; }

            try
            {
                Process proc = Process.GetProcessById((int)pid);
                if (IsShellProcess(proc.ProcessName)) { WinKeyBlocker.GameActive = false; ClearDetection(); return; }

                WinKeyBlocker.GameActive = true;
                detectedProcess = proc;
                GameSessionTracker.OnGameDetected(proc);
                statusName.Text = proc.ProcessName + ".exe";
                statusName.ForeColor = Theme.Accent;
                boostBtn.Enabled = true;
                warningLabel.Visible = false;

                if (autoBoostToggle.Checked && (lastBoostedProcess == null || lastBoostedProcess.Id != proc.Id))
                {
                    BoostProcess(proc);
                    lastBoostedProcess = proc;
                }

                if (gpuPinToggle.Checked) TryPinGpu(proc);
            }
            catch
            {
                ClearDetection();
            }
        }

        private void ClearDetection()
        {
            WinKeyBlocker.GameActive = false;
            detectedProcess = null;
            statusName.Text = Lang.T("Игра не обнаружена", "No game detected");
            statusName.ForeColor = Theme.TextDim;
            boostBtn.Enabled = false;
            warningLabel.Visible = true;
        }

        private bool IsShellProcess(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower == "explorer" || lower == "dwm" || lower == "searchhost" ||
                   lower == "shellexperiencehost" || lower == "applicationframehost" ||
                   lower == "textinputhost" || lower == "startmenuexperiencehost";
        }

        private void BoostProcess(Process proc)
        {
            try
            {
                proc.PriorityClass = ProcessPriorityClass.High;
                statusLabel.Text = Lang.T("Приоритет повышен: ", "Priority boosted: ") + proc.ProcessName + ".exe";
                statusLabel.ForeColor = Theme.Accent;
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Не удалось поднять приоритет (нужны права администратора?): ", "Failed to boost priority (administrator rights needed?): ") + ex.Message;
                statusLabel.ForeColor = Theme.Warning;
            }
        }

        private void RefreshHeavyList()
        {
            heavyList.Controls.Clear();
            heavyChecks.Clear();
            heavyGroups.Clear();

            for (int i = 0; i < KnownHeavyApps.Length; i++)
            {
                Process[] found;
                try { found = Process.GetProcessesByName(KnownHeavyApps[i]); }
                catch { continue; }

                if (found.Length == 0) continue;

                List<Process> group = new List<Process>();
                long totalRamMb = 0;
                for (int j = 0; j < found.Length; j++)
                {
                    try
                    {
                        totalRamMb += found[j].WorkingSet64 / 1024 / 1024;
                        group.Add(found[j]);
                    }
                    catch
                    {
                    }
                }

                if (group.Count == 0) continue;

                string displayName = KnownHeavyApps[i] + ".exe";
                if (group.Count > 1) displayName += " (" + group.Count + Lang.T(" процессов)", " processes)");

                bool groupFrozen = false;
                for (int j = 0; j < group.Count; j++)
                {
                    if (BackgroundFreezer.IsFrozen(group[j].Id)) { groupFrozen = true; break; }
                }
                if (groupFrozen) displayName += " ❄";

                // 636 и не шире: иначе с вертикальным скроллбаром (около 17px) появляется
                // горизонтальная прокрутка, а колонка с мегабайтами уезжает за край
                var row = new Panel { Size = new Size(636, 32), BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2) };

                var check = new CheckBox
                {
                    Text = displayName,
                    ForeColor = Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(6, 6),
                    AutoSize = true,
                    Checked = true
                };
                row.Controls.Add(check);

                var ramLbl = new Label
                {
                    Text = totalRamMb + Lang.T(" МБ", " MB"),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.5F),
                    Location = new Point(536, 8),
                    Size = new Size(96, 16),
                    TextAlign = ContentAlignment.MiddleRight,
                    AutoSize = false
                };
                row.Controls.Add(ramLbl);

                heavyList.Controls.Add(row);
                heavyChecks.Add(check);
                heavyGroups.Add(group);
            }

            if (heavyGroups.Count == 0)
            {
                var emptyLbl = new Label
                {
                    Text = Lang.T("Тяжёлых фоновых программ из списка не найдено", "No heavy background apps from the list found"),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(6, 6),
                    AutoSize = true
                };
                heavyList.Controls.Add(emptyLbl);
            }
        }

        private void TerminateSelected()
        {
            int killedGroups = 0;
            int killedProcs = 0;

            for (int i = 0; i < heavyChecks.Count; i++)
            {
                if (!heavyChecks[i].Checked) continue;

                List<Process> group = heavyGroups[i];
                bool anyKilled = false;
                for (int j = 0; j < group.Count; j++)
                {
                    try
                    {
                        group[j].Kill();
                        killedProcs++;
                        anyKilled = true;
                    }
                    catch
                    {
                    }
                }
                if (anyKilled) killedGroups++;
            }

            statusLabel.Text = killedProcs > 0
                ? Lang.T("Завершено программ: ", "Apps closed: ") + killedGroups + " (" + killedProcs + Lang.T(" процессов)", " processes)")
                : Lang.T("Ничего не выбрано или не удалось завершить", "Nothing selected or failed to close");
            statusLabel.ForeColor = killedProcs > 0 ? Theme.Accent : Theme.Warning;
            RefreshHeavyList();
        }
    }
}