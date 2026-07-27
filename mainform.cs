using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    public class MainForm : Form
    {
        private bool isAdmin;
        private Point dragStart;
        private bool draggingForm = false;

        private Panel sidebar;
        private Panel contentArea;

        private HomePanel panelHome;
        private Panel panelCleanup;
        private GameBoosterPanel panelBooster;
        private HealthCheckPanel panelHealth;
        private SystemInfoPanel panelSysInfo;
        private StartupPanel panelStartup;
        private ServicesPanel panelServices;
        private VisualEffectsPanel panelVisual;
        private ClickerPanel panelClicker;
        private ScreenshotsPanel panelScreenshots;
        private ClipboardManagerPanel panelClipboard;
        private NetworkPanel panelNetwork;
        private Panel panelSettings;

        private SidebarNavButton navHome;
        private SidebarNavButton navCleanup;
        private SidebarNavButton navBooster;
        private SidebarNavButton navHealth;
        private SidebarNavButton navSysInfo;
        private SidebarNavButton navStartup;
        private SidebarNavButton navServices;
        private SidebarNavButton navVisual;
        private SidebarNavButton navClicker;
        private SidebarNavButton navScreenshots;
        private SidebarNavButton navClipboard;
        private SidebarNavButton navNetwork;
        private SidebarNavButton navSettings;
        private Panel navIndicator;

        private Label adminStatusLabel;

        private NotifyIcon trayIcon;

        // Захват новой клавиши для хоткея: id действия, которое ждёт нажатия (-1 = никто)
        private int awaitingCaptureId = -1;
        private System.Collections.Generic.Dictionary<int, RoundedButton> hotkeyButtons =
            new System.Collections.Generic.Dictionary<int, RoundedButton>();

        public MainForm(bool isAdminFlag)
        {
            isAdmin = isAdminFlag;

            Text = "DeepTools";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(980, 650);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;

            try { Icon = AppIcon(); } catch { }

            Hotkeys.Load();
            SetupTrayIcon();
            StartTrayMonitor();
            BuildShell();
            ShowPanel(panelHome, navHome);
            StartupGuard.Start();
            MinerGuard.Start();
            TempHistory.Start();
            AutoCleanup.Start();
            KeepAwake.Restore();
            NotesManager.RestoreAll();
            UpdateChecker.CheckInBackground(true, null);
            WinKeyBlocker.Init();
            FormClosed += (s, e) => WinKeyBlocker.Shutdown();

            Load += (s, e) => ApplyRoundedRegion();
            Load += (s, e) => RegisterHotkeys();
            Shown += (s, e) => WhatsNew.ShowIfUpdated(this);
            FormClosing += (s, e) => OnFormClosing(s, e);
            FormClosed += (s, e) => UnregisterHotkeys();
        }

        // Системная тень вокруг borderless-окна: без неё окно выглядит плоско
        // наклеенным на рабочий стол
        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        // Иконка достаётся из самого exe (вшита при компиляции через /win32icon),
        // поэтому работает даже если logo.ico не лежит рядом - из-за этого у
        // пользователей без файла иконки трей был невидимым
        private static Icon AppIcon()
        {
            try
            {
                Icon fromExe = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (fromExe != null) return fromExe;
            }
            catch { }
            try
            {
                string path = System.IO.Path.Combine(Application.StartupPath, "logo.ico");
                if (System.IO.File.Exists(path)) return new Icon(path);
            }
            catch { }
            return SystemIcons.Application;
        }

        // Таймер обновления значка трея: рисуем температуру GPU цифрой.
        // Если NVML недоступен (не NVIDIA) - остаётся обычная иконка
        private System.Windows.Forms.Timer trayStatsTimer;
        private Icon lastTrayIcon;

        private void StartTrayMonitor()
        {
            trayStatsTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            trayStatsTimer.Tick += (s, e) => UpdateTrayIcon();
            trayStatsTimer.Start();
            UpdateTrayIcon();
        }

        private void UpdateTrayIcon()
        {
            int temp = NvmlGpu.GetTemperature();
            if (temp < 0)
            {
                trayIcon.Text = "DeepTools";
                return;
            }

            // Рисуем цифру температуры на значке 16x16
            using (var bmp = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Color bg = temp >= 80 ? Color.FromArgb(200, 60, 60)
                    : temp >= 65 ? Color.FromArgb(200, 150, 40)
                    : Color.FromArgb(20, 90, 60);
                using (var bgBrush = new SolidBrush(bg))
                {
                    g.FillEllipse(bgBrush, 0, 0, 16, 16);
                }

                using (var font = new Font("Segoe UI", temp >= 100 ? 6F : 7F, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(temp.ToString(), font, textBrush, new RectangleF(0, 1, 16, 15), fmt);
                }

                IntPtr hIcon = bmp.GetHicon();
                Icon newIcon = Icon.FromHandle(hIcon);
                trayIcon.Icon = newIcon;
                trayIcon.Text = "DeepTools - GPU " + temp + "°C";

                // Освобождаем предыдущую нарисованную иконку (GetHicon создаёт GDI-объект)
                if (lastTrayIcon != null)
                {
                    NativeMethods.DestroyIcon(lastTrayIcon.Handle);
                    lastTrayIcon.Dispose();
                }
                lastTrayIcon = newIcon;
            }
        }

        private void SetupTrayIcon()
        {
            trayIcon = new NotifyIcon();
            trayIcon.Icon = AppIcon();
            trayIcon.Text = "DeepTools";
            trayIcon.Visible = true;
            TrayNotify.Icon = trayIcon;

            trayIcon.MouseUp += (s, e) => {
                if (e.Button == MouseButtons.Right) ShowTrayMenu();
            };

            trayIcon.DoubleClick += (s, e) => ToggleWindow();
        }

        private void ShowTrayMenu()
        {
            var menuItems = new System.Collections.Generic.List<TrayMenuItem>
            {
                new TrayMenuItem("▢", Lang.T("Показать", "Show"), () => ShowWindow()),
                new TrayMenuItem("📝", Lang.T("Новая заметка", "New note"), () => NotesManager.CreateNew()),
                new TrayMenuItem("☕", Lang.T("Не спать: ", "Keep awake: ") + (KeepAwake.Enabled ? Lang.T("вкл", "on") : Lang.T("выкл", "off")),
                    () => KeepAwake.Enabled = !KeepAwake.Enabled),
                new TrayMenuItem("–", Lang.T("Скрыть", "Hide"), () => HideWindow()),
                new TrayMenuItem("✕", Lang.T("Выход", "Exit"), () => ExitApplication()) { Danger = true }
            };
            TrayMenuForm.Popup(menuItems);
        }

        private void ShowWindow()
        {
            Visible = true;
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void HideWindow()
        {
            Visible = false;
            WindowState = FormWindowState.Minimized;
        }

        private void ToggleWindow()
        {
            if (Visible && WindowState == FormWindowState.Normal)
                HideWindow();
            else
                ShowWindow();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideWindow();
            }
        }

        private void ExitApplication()
        {
            if (panelClicker != null) panelClicker.ReleaseAll();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }

        private void RegisterHotkeys()
        {
            Hotkeys.RegisterAll(this.Handle);
        }

        private void UnregisterHotkeys()
        {
            Hotkeys.UnregisterAll(this.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == NativeMethods.HOTKEY_ID_CLICKER)
                {
                    panelClicker.ToggleRunning();
                }
                else if (id == NativeMethods.HOTKEY_ID_SCREENSHOT)
                {
                    panelScreenshots.CaptureScreen();
                }
                else if (id == NativeMethods.HOTKEY_ID_OVERLAY)
                {
                    panelBooster.ToggleOverlay();
                }
                else if (id == NativeMethods.HOTKEY_ID_REGION)
                {
                    panelScreenshots.CaptureRegion();
                }
            }
            base.WndProc(ref m);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (awaitingCaptureId >= 0)
            {
                Keys key = keyData & Keys.KeyCode;

                // Голые модификаторы не считаем - ждём настоящую клавишу
                if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu) return true;

                int id = awaitingCaptureId;
                awaitingCaptureId = -1;

                if (key != Keys.Escape) // Esc = отмена, оставить как было
                {
                    HotkeyDef busy = Hotkeys.UsedBy(key, id);
                    if (busy != null)
                    {
                        MessageBox.Show(
                            Lang.T("Клавиша уже занята действием «", "This key is already used by \"") + busy.Name + Lang.T("»", "\""),
                            "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        Hotkeys.Set(this.Handle, id, key);
                    }
                }

                RefreshHotkeyUi();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Обновляет кнопки в карточке «Горячие клавиши» и подпись в панели скриншотов
        private void RefreshHotkeyUi()
        {
            foreach (var pair in hotkeyButtons)
            {
                pair.Value.Text = Hotkeys.Get(pair.Key).ToString();
                pair.Value.Invalidate();
            }
            if (panelScreenshots != null)
            {
                panelScreenshots.SetHotkeyDisplay(Hotkeys.Get(NativeMethods.HOTKEY_ID_SCREENSHOT).ToString());
                panelScreenshots.RefreshRegionHotkey();
            }
        }

        private void ApplyRoundedRegion()
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
        }

        private void BuildShell()
        {
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.BgColor };
            titleBar.MouseDown += (s, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) => {
                if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };

            var dot = new Panel { Size = new Size(10, 10), Location = new Point(16, 12), BackColor = Color.Transparent };
            dot.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, 0, 0, 10, 10);
            };
            titleBar.Controls.Add(dot);

            var titleLbl = new Label
            {
                Text = "DeepTools " + AppVersion.Short,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(34, 7),
                AutoSize = true
            };
            titleBar.Controls.Add(titleLbl);

            var closeBtn = new Label
            {
                Text = "\u2715",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F),
                Size = new Size(30, 26),
                Location = new Point(Width - 40, 4),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => HideWindow();
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            var minimizeBtn = new Label
            {
                Text = "\u2013",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Size = new Size(30, 26),
                Location = new Point(Width - 72, 4),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            minimizeBtn.Click += (s, e) => { WindowState = FormWindowState.Minimized; };
            minimizeBtn.MouseEnter += (s, e) => minimizeBtn.ForeColor = Theme.TextMain;
            minimizeBtn.MouseLeave += (s, e) => minimizeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(minimizeBtn);

            Controls.Add(titleBar);

            sidebar = new Panel { Location = new Point(0, 34), Size = new Size(220, 616), BackColor = Theme.SidebarColor };
            Controls.Add(sidebar);

            var brandPanel = new Panel { Location = new Point(0, 0), Size = new Size(220, 80), BackColor = Color.Transparent };
            var brandTitle = new Label
            {
                Text = "DeepTools",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 18),
                AutoSize = true
            };
            var brandSubtitle = new Label
            {
                Text = Lang.T("Умный твикер для Windows", "Smart tweaker for Windows"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 44),
                AutoSize = true
            };
            brandPanel.Controls.Add(brandTitle);
            brandPanel.Controls.Add(brandSubtitle);
            sidebar.Controls.Add(brandPanel);

            var divider = new Panel { Location = new Point(16, 86), Size = new Size(188, 1), BackColor = Theme.BorderColor };
            sidebar.Controls.Add(divider);

            // Навигация сгруппирована по смыслу; кнопки чуть компактнее (33px),
            // чтобы 13 пунктов + 3 заголовка влезли в 616px сайдбара
            navHome = MakeNavItem("⌂   " + Lang.T("Главная", "Home"), 90);

            MakeNavGroupLabel(Lang.T("ОПТИМИЗАЦИЯ", "OPTIMIZATION"), 129);
            navCleanup = MakeNavItem("♻   SmartCleanup", 147);
            navBooster = MakeNavItem("⚡   GameBooster", 182);
            navStartup = MakeNavItem("⭯   " + Lang.T("Автозагрузка", "Startup"), 217);
            navServices = MakeNavItem("⚙   " + Lang.T("Службы", "Services"), 252);
            navVisual = MakeNavItem("✦   " + Lang.T("Визуальные эффекты", "Visual effects"), 287);

            MakeNavGroupLabel(Lang.T("МОНИТОРИНГ", "MONITORING"), 326);
            navHealth = MakeNavItem("❤   Health Check", 344);
            navSysInfo = MakeNavItem("▤   " + Lang.T("Мой ПК", "My PC"), 379);
            navNetwork = MakeNavItem("⇅   " + Lang.T("Сеть", "Network"), 414);

            MakeNavGroupLabel(Lang.T("ИНСТРУМЕНТЫ", "TOOLS"), 453);
            navClicker = MakeNavItem("⊙   Clicker", 471);
            navScreenshots = MakeNavItem("▣   " + Lang.T("Скриншоты", "Screenshots"), 506);
            navClipboard = MakeNavItem("❏   " + Lang.T("Буфер обмена", "Clipboard"), 541);

            navSettings = MakeNavItem("⚙   " + Lang.T("Настройки", "Settings"), 579);

            // Бегущая акцентная полоска: переезжает к активному пункту при
            // переключении раздела вместо мгновенного скачка
            navIndicator = new Panel { Size = new Size(3, 21), Location = new Point(12, -30), BackColor = Theme.Accent };
            sidebar.Controls.Add(navIndicator);
            navIndicator.BringToFront();

            navHome.Click += (s, e) => ShowPanel(panelHome, navHome);
            navSysInfo.Click += (s, e) => ShowPanel(panelSysInfo, navSysInfo);
            navCleanup.Click += (s, e) => ShowPanel(panelCleanup, navCleanup);
            navBooster.Click += (s, e) => ShowPanel(panelBooster, navBooster);
            navHealth.Click += (s, e) => ShowPanel(panelHealth, navHealth);
            navStartup.Click += (s, e) => ShowPanel(panelStartup, navStartup);
            navServices.Click += (s, e) => ShowPanel(panelServices, navServices);
            navVisual.Click += (s, e) => ShowPanel(panelVisual, navVisual);
            navClicker.Click += (s, e) => ShowPanel(panelClicker, navClicker);
            navScreenshots.Click += (s, e) => ShowPanel(panelScreenshots, navScreenshots);
            navClipboard.Click += (s, e) => ShowPanel(panelClipboard, navClipboard);
            navNetwork.Click += (s, e) => ShowPanel(panelNetwork, navNetwork);
            navSettings.Click += (s, e) => ShowPanel(panelSettings, navSettings);

            contentArea = new Panel { Location = new Point(220, 34), Size = new Size(760, 616), BackColor = Theme.BgColor };
            EnableDoubleBuffer(contentArea);
            Controls.Add(contentArea);

            panelHome = new HomePanel();
            panelHome.RequestNavigate += key => {
                if (key == "cleanup") ShowPanel(panelCleanup, navCleanup);
                else if (key == "booster") ShowPanel(panelBooster, navBooster);
                else if (key == "health") ShowPanel(panelHealth, navHealth);
                else if (key == "clicker") ShowPanel(panelClicker, navClicker);
                else if (key == "screenshots") ShowPanel(panelScreenshots, navScreenshots);
                else if (key == "settings") ShowPanel(panelSettings, navSettings);
            };
            contentArea.Controls.Add(panelHome);
            panelHome.Visible = false;

            panelCleanup = new SmartCleanupPanel();
            contentArea.Controls.Add(panelCleanup);
            panelCleanup.Visible = false;

            panelBooster = new GameBoosterPanel();
            contentArea.Controls.Add(panelBooster);
            panelBooster.Visible = false;

            panelHealth = new HealthCheckPanel();
            contentArea.Controls.Add(panelHealth);
            panelHealth.Visible = false;

            panelSysInfo = new SystemInfoPanel();
            contentArea.Controls.Add(panelSysInfo);
            panelSysInfo.Visible = false;

            panelStartup = new StartupPanel();
            contentArea.Controls.Add(panelStartup);
            panelStartup.Visible = false;

            panelServices = new ServicesPanel();
            contentArea.Controls.Add(panelServices);
            panelServices.Visible = false;

            panelVisual = new VisualEffectsPanel();
            contentArea.Controls.Add(panelVisual);
            panelVisual.Visible = false;

            panelClicker = new ClickerPanel();
            contentArea.Controls.Add(panelClicker);
            panelClicker.Visible = false;

            panelScreenshots = new ScreenshotsPanel();
            panelScreenshots.SetHotkeyDisplay(Hotkeys.Get(NativeMethods.HOTKEY_ID_SCREENSHOT).ToString());
            panelScreenshots.RequestHotkeyCapture += (s, e) => {
                awaitingCaptureId = NativeMethods.HOTKEY_ID_SCREENSHOT;
                panelScreenshots.SetHotkeyDisplay(Lang.T("Нажми любую клавишу...", "Press any key..."));
            };
            contentArea.Controls.Add(panelScreenshots);
            panelScreenshots.Visible = false;

            panelClipboard = new ClipboardManagerPanel();
            contentArea.Controls.Add(panelClipboard);
            panelClipboard.Visible = false;

            panelNetwork = new NetworkPanel();
            contentArea.Controls.Add(panelNetwork);
            panelNetwork.Visible = false;

            panelSettings = BuildSettingsPanel();
        }

        private SidebarNavButton MakeNavItem(string text, int y)
        {
            var item = new SidebarNavButton(text)
            {
                Location = new Point(12, y),
                Size = new Size(196, 33)
            };
            sidebar.Controls.Add(item);
            return item;
        }

        // Тонкий заголовок группы в сайдбаре: «ОПТИМИЗАЦИЯ», «МОНИТОРИНГ»...
        private void MakeNavGroupLabel(string text, int y)
        {
            var lbl = new Label
            {
                Text = text,
                ForeColor = Theme.Lerp(Theme.SidebarColor, Theme.TextDim, 0.55f),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 6.8F, FontStyle.Bold),
                Location = new Point(24, y),
                AutoSize = true
            };
            sidebar.Controls.Add(lbl);
        }

        private void ShowPanel(Panel panel, SidebarNavButton navItem)
        {
            panelHome.Visible = false;
            panelCleanup.Visible = false;
            panelBooster.Visible = false;
            panelHealth.Visible = false;
            panelSysInfo.Visible = false;
            panelStartup.Visible = false;
            panelServices.Visible = false;
            panelVisual.Visible = false;
            panelClicker.Visible = false;
            panelScreenshots.Visible = false;
            panelClipboard.Visible = false;
            panelNetwork.Visible = false;
            panelSettings.Visible = false;

            navHome.SetActive(false);
            navCleanup.SetActive(false);
            navBooster.SetActive(false);
            navHealth.SetActive(false);
            navSysInfo.SetActive(false);
            navStartup.SetActive(false);
            navServices.SetActive(false);
            navVisual.SetActive(false);
            navClicker.SetActive(false);
            navScreenshots.SetActive(false);
            navClipboard.SetActive(false);
            navNetwork.SetActive(false);
            navSettings.SetActive(false);

            panel.Visible = true;
            navItem.SetActive(true);
            MoveNavIndicator(navItem);
            AnimatePanelIn(panel);
        }

        // Акцентная полоска плавно переезжает к активному пункту навигации
        private System.Windows.Forms.Timer navIndTimer;
        private int navIndTarget;

        private void MoveNavIndicator(SidebarNavButton navItem)
        {
            navIndTarget = navItem.Top + (navItem.Height - navIndicator.Height) / 2;
            // Первый показ - без анимации, полоска ещё за краем
            if (navIndicator.Top < 0)
            {
                navIndicator.Top = navIndTarget;
                return;
            }
            if (navIndTimer == null)
            {
                navIndTimer = new System.Windows.Forms.Timer { Interval = 12 };
                navIndTimer.Tick += (s, e) => {
                    int d = navIndTarget - navIndicator.Top;
                    if (Math.Abs(d) <= 2) { navIndicator.Top = navIndTarget; navIndTimer.Stop(); return; }
                    navIndicator.Top += d > 0 ? Math.Max(2, d / 3) : Math.Min(-2, d / 3);
                };
            }
            navIndTimer.Start();
        }

        // Лёгкий слайд-въезд панели слева при переключении раздела
        private System.Windows.Forms.Timer slideTimer;
        private Panel slidingPanel;

        private void AnimatePanelIn(Panel panel)
        {
            EnableDoubleBuffer(panel);
            if (slideTimer == null)
            {
                slideTimer = new System.Windows.Forms.Timer { Interval = 12 };
                slideTimer.Tick += (s, e) => {
                    if (slidingPanel == null) { slideTimer.Stop(); return; }
                    int left = slidingPanel.Left;
                    if (left <= 2) { slidingPanel.Left = 0; slideTimer.Stop(); return; }
                    slidingPanel.Left = left - Math.Max(2, left / 2);
                };
            }
            // Если предыдущая анимация не доиграла - фиксируем прошлую панель
            if (slidingPanel != null && slidingPanel != panel) slidingPanel.Left = 0;
            slidingPanel = panel;
            panel.Left = 22;
            slideTimer.Start();
        }

        private static void EnableDoubleBuffer(Control c)
        {
            try
            {
                typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(c, true, null);
            }
            catch { }
        }

        private Panel BuildSettingsPanel()
        {
            var panel = new Panel { Location = new Point(0, 0), Size = new Size(760, 616), BackColor = Theme.BgColor, Visible = false };
            contentArea.Controls.Add(panel);

            var titleLbl = new Label
            {
                Text = Lang.T("Настройки", "Settings"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                Location = new Point(24, 24),
                AutoSize = true
            };
            panel.Controls.Add(titleLbl);

            var adminCard = Theme.MakeCard(panel, new Point(24, 70), new Size(640, 90));

            adminStatusLabel = new Label
            {
                Text = isAdmin
                    ? Lang.T("Права администратора: включены", "Administrator rights: enabled")
                    : Lang.T("Права администратора: не включены", "Administrator rights: not enabled"),
                ForeColor = isAdmin ? Theme.Accent : Theme.Warning,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 16),
                AutoSize = true
            };
            adminCard.Controls.Add(adminStatusLabel);

            var adminDesc = new Label
            {
                Text = Lang.T(
                    "Нужны для очистки Prefetch, служб, автозагрузки в HKLM, завершения процессов и смены приоритета",
                    "Required for Prefetch cleanup, services, HKLM startup entries, killing processes and changing priority"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 40),
                Size = new Size(440, 34),
                AutoSize = false
            };
            adminCard.Controls.Add(adminDesc);

            var requestAdminBtn = new RoundedButton
            {
                Text = Lang.T("Запросить права", "Request rights"),
                TextColor = Theme.BgColor,
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(480, 28),
                Size = new Size(140, 32),
                Visible = !isAdmin
            };
            requestAdminBtn.Click += (s, e) => {
                if (Program.TryRelaunchAsAdmin())
                {
                    Application.Exit();
                }
            };
            adminCard.Controls.Add(requestAdminBtn);

            // Карточка внешнего вида: тема и язык
            var lookCard = Theme.MakeCard(panel, new Point(24, 176), new Size(640, 150));

            var lookTitle = new Label
            {
                Text = Lang.T("Внешний вид и язык", "Appearance & language"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 16),
                AutoSize = true
            };
            lookCard.Controls.Add(lookTitle);

            var lookDesc = new Label
            {
                Text = Lang.T("Изменения применяются после перезапуска программы", "Changes take effect after restarting the app"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 40),
                AutoSize = true
            };
            lookCard.Controls.Add(lookDesc);

            var themeLabel = new Label
            {
                Text = Lang.T("Тема:", "Theme:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(20, 74),
                AutoSize = true
            };
            lookCard.Controls.Add(themeLabel);

            bool isLight = AppConfig.Get("theme", "dark") == "light";
            var darkBtn = MakeChoiceButton(lookCard, Lang.T("Тёмная", "Dark"), new Point(90, 70), !isLight);
            var lightBtn = MakeChoiceButton(lookCard, Lang.T("Светлая", "Light"), new Point(200, 70), isLight);
            darkBtn.Click += (s, e) => {
                AppConfig.Set("theme", "dark");
                StyleChoice(darkBtn, true); StyleChoice(lightBtn, false);
                OfferRestart();
            };
            lightBtn.Click += (s, e) => {
                AppConfig.Set("theme", "light");
                StyleChoice(darkBtn, false); StyleChoice(lightBtn, true);
                OfferRestart();
            };

            var langLabel = new Label
            {
                Text = Lang.T("Язык:", "Language:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(20, 112),
                AutoSize = true
            };
            lookCard.Controls.Add(langLabel);

            var ruBtn = MakeChoiceButton(lookCard, "Русский", new Point(90, 108), !Lang.IsEn);
            var enBtn = MakeChoiceButton(lookCard, "English", new Point(200, 108), Lang.IsEn);
            ruBtn.Click += (s, e) => {
                AppConfig.Set("language", "ru");
                StyleChoice(ruBtn, true); StyleChoice(enBtn, false);
                OfferRestart();
            };
            enBtn.Click += (s, e) => {
                AppConfig.Set("language", "en");
                StyleChoice(ruBtn, false); StyleChoice(enBtn, true);
                OfferRestart();
            };

            // Карточка: автозапуск и быстрые действия питания
            var sysCard = Theme.MakeCard(panel, new Point(24, 340), new Size(640, 190));

            var sysTitle = new Label
            {
                Text = Lang.T("Автозапуск и питание", "Startup & power"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 14),
                AutoSize = true
            };
            sysCard.Controls.Add(sysTitle);

            var autoStartLabel = new Label
            {
                Text = Lang.T("Запускать DeepTools вместе с Windows", "Launch DeepTools with Windows"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 48),
                AutoSize = true
            };
            sysCard.Controls.Add(autoStartLabel);

            var autoStartToggle = new ToggleSwitch { Location = new Point(580, 44), Checked = AutoStart.Enabled };
            autoStartToggle.CheckedChanged += (s, e) => { AutoStart.Enabled = autoStartToggle.Checked; };
            sysCard.Controls.Add(autoStartToggle);

            var fastStartLabel = new Label
            {
                Text = Lang.T("Быстрый запуск Windows (гибридное завершение)", "Windows Fast Startup (hybrid shutdown)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 84),
                AutoSize = true
            };
            sysCard.Controls.Add(fastStartLabel);

            var fastStartToggle = new ToggleSwitch { Location = new Point(580, 80), Checked = PowerTools.FastStartupEnabled };
            fastStartToggle.CheckedChanged += (s, e) => { PowerTools.FastStartupEnabled = fastStartToggle.Checked; };
            sysCard.Controls.Add(fastStartToggle);

            var biosBtn = new RoundedButton
            {
                Text = Lang.T("Перезагрузка в BIOS/UEFI", "Restart to BIOS/UEFI"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(20, 128),
                Size = new Size(220, 34)
            };
            biosBtn.Click += (s, e) => {
                if (MessageBox.Show(
                    Lang.T("Перезагрузить компьютер сейчас и войти в BIOS/UEFI?", "Restart the PC now and enter BIOS/UEFI?"),
                    "DeepTools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    PowerTools.RestartToFirmware();
            };
            sysCard.Controls.Add(biosBtn);

            var restartExplorerBtn = new RoundedButton
            {
                Text = Lang.T("Перезапустить проводник", "Restart Explorer"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(252, 128),
                Size = new Size(220, 34)
            };
            restartExplorerBtn.Click += (s, e) => PowerTools.RestartExplorer();
            sysCard.Controls.Add(restartExplorerBtn);

            // Карточка: экран и сон. Карточка ниже видимой области - панель скроллится
            panel.AutoScroll = true;
            var pwCard = Theme.MakeCard(panel, new Point(24, 546), new Size(640, 172));

            var pwTitle = new Label
            {
                Text = Lang.T("Экран и сон", "Screen & sleep"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 14),
                AutoSize = true
            };
            pwCard.Controls.Add(pwTitle);

            var awakeLabel = new Label
            {
                Text = Lang.T("☕ Не спать: не гасить экран и не уходить в сон", "☕ Keep awake: don't turn off the screen or sleep"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 48),
                AutoSize = true
            };
            pwCard.Controls.Add(awakeLabel);

            var awakeToggle = new ToggleSwitch { Location = new Point(580, 44), Checked = KeepAwake.Enabled };
            awakeToggle.CheckedChanged += (s, e) => { KeepAwake.Enabled = awakeToggle.Checked; };
            pwCard.Controls.Add(awakeToggle);

            var monLabel = new Label
            {
                Text = Lang.T("Отключать экран через", "Turn off the screen after"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 92),
                AutoSize = true
            };
            pwCard.Controls.Add(monLabel);
            var monBox = MakeTimeoutBox(pwCard, new Point(456, 88));

            var sleepLabel = new Label
            {
                Text = Lang.T("Переводить в спящий режим через", "Put the PC to sleep after"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 130),
                AutoSize = true
            };
            pwCard.Controls.Add(sleepLabel);
            var sleepBox = MakeTimeoutBox(pwCard, new Point(456, 126));

            // Текущие значения читаются powercfg'ом - в фоне, чтобы не тормозить открытие
            var pwWorker = new System.ComponentModel.BackgroundWorker();
            pwWorker.DoWork += (s, e) => { e.Result = new[] { PowerTimeouts.GetMonitorMinutes(), PowerTimeouts.GetSleepMinutes() }; };
            pwWorker.RunWorkerCompleted += (s, e) =>
            {
                if (pwCard.IsDisposed) return;
                var cur = (int[])e.Result;
                FillTimeoutBox(monBox, cur[0], v => PowerTimeouts.SetMonitorMinutes(v));
                FillTimeoutBox(sleepBox, cur[1], v => PowerTimeouts.SetSleepMinutes(v));
            };
            pwWorker.RunWorkerAsync();

            // Карточка: горячие клавиши. Клик по клавише -> захват нового нажатия
            var hkCard = Theme.MakeCard(panel, new Point(24, 734), new Size(640, 216));

            var hkTitle = new Label
            {
                Text = Lang.T("Горячие клавиши", "Hotkeys"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 14),
                AutoSize = true
            };
            hkCard.Controls.Add(hkTitle);

            var hkHint = new Label
            {
                Text = Lang.T("Клик по клавише — затем нажми новую. Esc — отмена", "Click a key, then press a new one. Esc cancels"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.25F),
                Location = new Point(20, 36),
                AutoSize = true
            };
            hkCard.Controls.Add(hkHint);

            int hkY = 64;
            foreach (HotkeyDef def in Hotkeys.All)
            {
                var actLabel = new Label
                {
                    Text = def.Name,
                    ForeColor = Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(20, hkY + 5),
                    AutoSize = true
                };
                hkCard.Controls.Add(actLabel);

                var keyBtn = new RoundedButton
                {
                    Text = def.Current.ToString(),
                    ButtonColor = Theme.InputColor,
                    HoverColor = Theme.KeyHover,
                    TextColor = Theme.Accent,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Location = new Point(508, hkY),
                    Size = new Size(112, 28)
                };
                int defId = def.Id; // копия для замыкания
                keyBtn.Click += (s, e) =>
                {
                    awaitingCaptureId = defId;
                    RefreshHotkeyUi(); // сбрасываем возможный прошлый «Нажми...» на других кнопках
                    keyBtn.Text = Lang.T("Нажми...", "Press...");
                    keyBtn.Invalidate();
                };
                hkCard.Controls.Add(keyBtn);
                hotkeyButtons[def.Id] = keyBtn;

                hkY += 36;
            }

            return panel;
        }

        private ComboBox MakeTimeoutBox(Panel parent, Point loc)
        {
            var box = new ComboBox
            {
                Location = loc,
                Size = new Size(160, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F),
                Enabled = false
            };
            box.Items.Add("…");
            box.SelectedIndex = 0;
            parent.Controls.Add(box);
            return box;
        }

        // Заполняет выпадашку вариантами, выбирает текущий и вешает применение.
        // Если текущее значение нестандартное (например 7 мин) - добавляем его в список
        private void FillTimeoutBox(ComboBox box, int current, Action<int> apply)
        {
            var values = new System.Collections.Generic.List<int>(PowerTimeouts.Choices);
            if (current >= 0 && !values.Contains(current))
            {
                values.Add(current);
                values.Sort();
            }

            box.Items.Clear();
            foreach (int v in values) box.Items.Add(PowerTimeouts.Format(v));
            box.SelectedIndex = current >= 0 ? values.IndexOf(current) : 0;
            box.Enabled = true;
            box.SelectedIndexChanged += (s, e) =>
            {
                if (box.SelectedIndex >= 0 && box.SelectedIndex < values.Count)
                    apply(values[box.SelectedIndex]);
            };
        }

        private RoundedButton MakeChoiceButton(Panel parent, string text, Point loc, bool selected)
        {
            var btn = new RoundedButton
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = loc,
                Size = new Size(100, 28)
            };
            StyleChoice(btn, selected);
            parent.Controls.Add(btn);
            return btn;
        }

        private void StyleChoice(RoundedButton btn, bool selected)
        {
            btn.ButtonColor = selected ? Theme.Accent : Theme.InputColor;
            btn.HoverColor = selected ? Theme.AccentHover : Theme.KeyHover;
            btn.TextColor = selected ? Theme.BgColor : Theme.TextDim;
            btn.Invalidate();
        }

        private void OfferRestart()
        {
            DialogResult r = MessageBox.Show(
                Lang.T("Перезапустить DeepTools сейчас, чтобы применить изменения?", "Restart DeepTools now to apply changes?"),
                "DeepTools",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
            {
                Process.Start(Application.ExecutablePath);
                ExitApplication();
            }
        }
    }
}