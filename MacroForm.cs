using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DeepTools
{
    // Запись и воспроизведение макросов.
    // Запись: низкоуровневые хуки мыши и клавиатуры (WH_MOUSE_LL / WH_KEYBOARD_LL)
    // ловят нажатия/отпускания клавиш и кнопок мыши с позицией курсора и таймингами.
    // Движения мыши не пишем (иначе тысячи событий) - перед кликом курсор
    // просто ставится в записанную точку.
    // Воспроизведение: поток шлёт события через mouse_event/keybd_event с паузами.
    // Управление хоткеями: F6 - старт/стоп записи, F7 - старт/стоп воспроизведения.
    // Форма сама регистрирует эти хоткеи и обрабатывает их в своём WndProc,
    // поэтому MainForm трогать не нужно.
    public class MacroForm : Form
    {
        // ---- Низкоуровневые хуки ----
        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;

        private const int VK_F6 = 0x75;
        private const int VK_F7 = 0x76;

        // Держим ссылки на делегаты, иначе GC соберёт и хук упадёт
        private LowLevelProc keyboardProc;
        private LowLevelProc mouseProc;
        private IntPtr keyboardHook = IntPtr.Zero;
        private IntPtr mouseHook = IntPtr.Zero;

        // ---- Одно событие макроса ----
        private class MacroEvent
        {
            public char Kind;   // 'K' - клавиша, 'M' - мышь
            public int Delay;   // мс паузы перед этим событием
            public int Vk;      // код клавиши (для 'K')
            public bool Down;   // нажатие или отпускание
            public int Button;  // 0=левая, 1=правая, 2=средняя (для 'M')
            public int X;
            public int Y;
        }

        private List<MacroEvent> events = new List<MacroEvent>();
        private Stopwatch recordSw;
        private long lastEventMs;
        private bool recording = false;

        private volatile bool playing = false;
        private Thread playThread;

        // ---- UI ----
        private RoundedButton recordBtn;
        private RoundedButton playBtn;
        private RoundedButton stopBtn;
        private Label statusLabel;
        private Label eventsLabel;
        private TextBox repeatBox;
        private ToggleSwitch loopToggle;
        private TextBox nameBox;
        private FlowLayoutPanel savedList;

        private Point dragStart;
        private bool draggingForm = false;

        public MacroForm()
        {
            Text = "DeepTools Macros";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(520, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            // Не поверх окон: чтобы можно было свернуться в игру и записать действия,
            // а окно макросов не перекрывало игру. Управление идёт по F6/F7
            TopMost = false;

            keyboardProc = KeyboardCallback;
            mouseProc = MouseCallback;

            BuildUi();
            Load += (s, e) => { ApplyRoundedRegion(); RegisterHotkeys(); RefreshSavedList(); };
            FormClosing += (s, e) => Cleanup();
        }

        private void ApplyRoundedRegion()
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
        }

        private void BuildUi()
        {
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.BgColor };
            titleBar.MouseDown += (s, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) => {
                if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("🎬 Макросы", "🎬 Macros"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            };
            titleBar.Controls.Add(titleLbl);

            var closeBtn = new Label
            {
                Text = "✕",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F),
                Size = new Size(30, 26),
                Location = new Point(Width - 42, 7),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => Close();
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            // Карточка управления
            var ctrlCard = Theme.MakeCard(this, new Point(16, 52), new Size(488, 140));

            recordBtn = new RoundedButton
            {
                Text = Lang.T("⏺ Запись (F6)", "⏺ Record (F6)"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 16),
                Size = new Size(150, 40)
            };
            recordBtn.Click += (s, e) => ToggleRecord();
            ctrlCard.Controls.Add(recordBtn);

            playBtn = new RoundedButton
            {
                Text = Lang.T("▶ Играть (F7)", "▶ Play (F7)"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(176, 16),
                Size = new Size(150, 40)
            };
            playBtn.Click += (s, e) => TogglePlay();
            ctrlCard.Controls.Add(playBtn);

            stopBtn = new RoundedButton
            {
                Text = Lang.T("⏹ Стоп", "⏹ Stop"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(336, 16),
                Size = new Size(136, 40)
            };
            stopBtn.Click += (s, e) => { StopRecord(); StopPlay(); };
            ctrlCard.Controls.Add(stopBtn);

            statusLabel = new Label
            {
                Text = Lang.T("Готов. Нажми «Запись» и выполни действия.", "Ready. Press Record and perform actions."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 66),
                Size = new Size(456, 18),
                AutoEllipsis = true
            };
            ctrlCard.Controls.Add(statusLabel);

            var repeatLabel = new Label
            {
                Text = Lang.T("Повторов:", "Repeats:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(16, 98),
                AutoSize = true
            };
            ctrlCard.Controls.Add(repeatLabel);

            repeatBox = new TextBox
            {
                Text = "1",
                Location = new Point(88, 94),
                Size = new Size(48, 24),
                Font = new Font("Segoe UI", 10F),
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                MaxLength = 5
            };
            repeatBox.KeyPress += (s, e) => { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
            ctrlCard.Controls.Add(repeatBox);

            var loopLabel = new Label
            {
                Text = Lang.T("∞ Бесконечно", "∞ Infinite"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(150, 98),
                AutoSize = true
            };
            ctrlCard.Controls.Add(loopLabel);

            loopToggle = new ToggleSwitch { Location = new Point(258, 94), Checked = false };
            ctrlCard.Controls.Add(loopToggle);

            eventsLabel = new Label
            {
                Text = Lang.T("Событий: 0", "Events: 0"),
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(340, 98),
                AutoSize = true
            };
            ctrlCard.Controls.Add(eventsLabel);

            // Карточка-гид: как записать и воспроизвести
            var guideCard = Theme.MakeCard(this, new Point(16, 204), new Size(488, 116));

            var guideTitle = new Label
            {
                Text = Lang.T("Как пользоваться", "How to use"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            guideCard.Controls.Add(guideTitle);

            var guideSteps = new Label
            {
                Text = Lang.T(
                    "1.  Нажми  F6  (или «Запись»)  →  сверни это окно\n" +
                    "2.  Сделай действия мышью и клавиатурой\n" +
                    "3.  Нажми  F6  ещё раз  →  запись остановится\n" +
                    "4.  Нажми  F7  (или «Играть»)  →  макрос повторится",
                    "1.  Press  F6  (or \"Record\")  →  minimize this window\n" +
                    "2.  Do your mouse and keyboard actions\n" +
                    "3.  Press  F6  again  →  recording stops\n" +
                    "4.  Press  F7  (or \"Play\")  →  the macro repeats"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 36),
                Size = new Size(460, 76)
            };
            guideCard.Controls.Add(guideSteps);

            // Карточка сохранённых макросов
            var savedCard = Theme.MakeCard(this, new Point(16, 330), new Size(488, 224));

            var savedTitle = new Label
            {
                Text = Lang.T("Макрос:", "Macro:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(16, 16),
                AutoSize = true
            };
            savedCard.Controls.Add(savedTitle);

            nameBox = new TextBox
            {
                Location = new Point(80, 12),
                Size = new Size(250, 26),
                Font = new Font("Segoe UI", 10F),
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle
            };
            savedCard.Controls.Add(nameBox);

            var saveBtn = new RoundedButton
            {
                Text = Lang.T("Сохранить", "Save"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(340, 10),
                Size = new Size(132, 30)
            };
            saveBtn.Click += (s, e) => SaveCurrent();
            savedCard.Controls.Add(saveBtn);

            savedList = new FlowLayoutPanel
            {
                Location = new Point(10, 48),
                Size = new Size(468, 168),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            savedCard.Controls.Add(savedList);
            NativeMethods.ApplyDarkScrollbar(savedList);

            var hint = new Label
            {
                Text = Lang.T("Движения мыши не записываются — только клики в их точках и клавиши.",
                              "Mouse movement is not recorded — only clicks at their points and keys."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(18, 562),
                Size = new Size(484, 30)
            };
            Controls.Add(hint);
        }

        // ---- Хоткеи (собственные, на своём Handle) ----
        // Клавиша регионального скриншота глобально занята главной формой (по
        // умолчанию F6). Пока открыто окно макросов, снимаем её и забираем F6/F7
        // себе под запись/воспроизведение, а при закрытии возвращаем главной форме
        private void RegisterHotkeys()
        {
            if (Owner != null)
            {
                try { NativeMethods.UnregisterHotKey(Owner.Handle, NativeMethods.HOTKEY_ID_REGION); } catch { }
            }
            NativeMethods.RegisterHotKey(Handle, NativeMethods.HOTKEY_ID_MACRO_REC, 0, (uint)Keys.F6);
            NativeMethods.RegisterHotKey(Handle, NativeMethods.HOTKEY_ID_MACRO_PLAY, 0, (uint)Keys.F7);
        }

        private void UnregisterHotkeys()
        {
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.HOTKEY_ID_MACRO_REC);
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.HOTKEY_ID_MACRO_PLAY);
            // Возвращаем главной форме её клавишу регионального скриншота
            // (могла быть переназначена в Настройках, поэтому берём из Hotkeys)
            if (Owner != null)
            {
                try { NativeMethods.RegisterHotKey(Owner.Handle, NativeMethods.HOTKEY_ID_REGION, 0, (uint)Hotkeys.Get(NativeMethods.HOTKEY_ID_REGION)); } catch { }
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == NativeMethods.HOTKEY_ID_MACRO_REC) ToggleRecord();
                else if (id == NativeMethods.HOTKEY_ID_MACRO_PLAY) TogglePlay();
            }
            base.WndProc(ref m);
        }

        // ---- Запись ----
        private void ToggleRecord()
        {
            if (recording) StopRecord();
            else StartRecord();
        }

        private void StartRecord()
        {
            if (playing) StopPlay();
            events.Clear();
            recordSw = Stopwatch.StartNew();
            lastEventMs = 0;
            recording = true;

            IntPtr hMod = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, hMod, 0);
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, hMod, 0);

            recordBtn.Text = Lang.T("⏺ Стоп записи (F6)", "⏺ Stop rec (F6)");
            recordBtn.Invalidate();
            statusLabel.Text = Lang.T("● Идёт запись... F6 чтобы остановить", "● Recording... F6 to stop");
            statusLabel.ForeColor = Theme.Danger;
            UpdateEventsLabel();
        }

        private void StopRecord()
        {
            if (!recording) return;
            recording = false;
            UnhookAll();

            recordBtn.Text = Lang.T("⏺ Запись (F6)", "⏺ Record (F6)");
            recordBtn.Invalidate();
            statusLabel.Text = Lang.T("Записано событий: ", "Recorded events: ") + events.Count;
            statusLabel.ForeColor = Theme.Accent;
            UpdateEventsLabel();
        }

        private void UnhookAll()
        {
            if (keyboardHook != IntPtr.Zero) { try { UnhookWindowsHookEx(keyboardHook); } catch { } keyboardHook = IntPtr.Zero; }
            if (mouseHook != IntPtr.Zero) { try { UnhookWindowsHookEx(mouseHook); } catch { } mouseHook = IntPtr.Zero; }
        }

        private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && recording)
            {
                int msg = wParam.ToInt32();
                int vk = Marshal.ReadInt32(lParam); // vkCode - первое поле KBDLLHOOKSTRUCT

                // F6/F7 - управление, их не записываем
                if (vk != VK_F6 && vk != VK_F7)
                {
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                    if (down || up) AddEvent(new MacroEvent { Kind = 'K', Vk = vk, Down = down });
                }
            }
            return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && recording)
            {
                int msg = wParam.ToInt32();
                int x = Marshal.ReadInt32(lParam, 0); // POINT.x - первое поле MSLLHOOKSTRUCT
                int y = Marshal.ReadInt32(lParam, 4); // POINT.y

                int button = -1;
                bool down = false;
                switch (msg)
                {
                    case WM_LBUTTONDOWN: button = 0; down = true; break;
                    case WM_LBUTTONUP: button = 0; down = false; break;
                    case WM_RBUTTONDOWN: button = 1; down = true; break;
                    case WM_RBUTTONUP: button = 1; down = false; break;
                    case WM_MBUTTONDOWN: button = 2; down = true; break;
                    case WM_MBUTTONUP: button = 2; down = false; break;
                }
                if (button >= 0) AddEvent(new MacroEvent { Kind = 'M', Button = button, Down = down, X = x, Y = y });
            }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        // Хук-колбэк работает на потоке UI (LL-хуки требуют насос сообщений),
        // поэтому можно безопасно трогать список и обновлять подпись
        private void AddEvent(MacroEvent ev)
        {
            long now = recordSw.ElapsedMilliseconds;
            ev.Delay = (int)(now - lastEventMs);
            lastEventMs = now;
            events.Add(ev);
            UpdateEventsLabel();
        }

        private void UpdateEventsLabel()
        {
            eventsLabel.Text = Lang.T("Событий: ", "Events: ") + events.Count;
        }

        // ---- Воспроизведение ----
        private void TogglePlay()
        {
            if (playing) StopPlay();
            else StartPlay();
        }

        private void StartPlay()
        {
            if (recording) StopRecord();
            if (events.Count == 0)
            {
                statusLabel.Text = Lang.T("Нечего воспроизводить - сначала запиши макрос", "Nothing to play - record a macro first");
                statusLabel.ForeColor = Theme.Warning;
                return;
            }

            bool loop = loopToggle.Checked;
            int repeats = 1;
            int.TryParse(repeatBox.Text, out repeats);
            if (repeats < 1) repeats = 1;

            playing = true;
            playBtn.Text = Lang.T("▶ Стоп (F7)", "▶ Stop (F7)");
            playBtn.Invalidate();
            statusLabel.Text = Lang.T("▶ Воспроизведение... F7 чтобы остановить", "▶ Playing... F7 to stop");
            statusLabel.ForeColor = Theme.Accent;

            var snapshot = new List<MacroEvent>(events);
            playThread = new Thread(() => PlayLoop(snapshot, loop, repeats));
            playThread.IsBackground = true;
            playThread.Start();
        }

        private void PlayLoop(List<MacroEvent> seq, bool loop, int repeats)
        {
            int done = 0;
            while (playing && (loop || done < repeats))
            {
                for (int i = 0; i < seq.Count; i++)
                {
                    if (!playing) break;
                    MacroEvent ev = seq[i];

                    // Пауза перед событием (режем очень длинные, чтобы стоп был отзывчивым)
                    int wait = ev.Delay;
                    while (wait > 0 && playing)
                    {
                        int chunk = Math.Min(wait, 50);
                        Thread.Sleep(chunk);
                        wait -= chunk;
                    }
                    if (!playing) break;

                    Inject(ev);
                }
                done++;
            }

            playing = false;
            if (!IsDisposed)
            {
                try
                {
                    BeginInvoke((Action)(() => {
                        playBtn.Text = Lang.T("▶ Играть (F7)", "▶ Play (F7)");
                        playBtn.Invalidate();
                        statusLabel.Text = Lang.T("Воспроизведение завершено", "Playback finished");
                        statusLabel.ForeColor = Theme.TextDim;
                    }));
                }
                catch { }
            }
        }

        private void Inject(MacroEvent ev)
        {
            if (ev.Kind == 'K')
            {
                if (ev.Down) NativeMethods.KeyDown((Keys)ev.Vk);
                else NativeMethods.KeyUp((Keys)ev.Vk);
            }
            else // 'M'
            {
                NativeMethods.SetCursorPos(ev.X, ev.Y);
                if (ev.Button == 0) { if (ev.Down) NativeMethods.MouseLeftDown(); else NativeMethods.MouseLeftUp(); }
                else if (ev.Button == 1) { if (ev.Down) NativeMethods.MouseRightDown(); else NativeMethods.MouseRightUp(); }
                else if (ev.Button == 2) { if (ev.Down) NativeMethods.MouseMiddleDown(); else NativeMethods.MouseMiddleUp(); }
            }
        }

        private void StopPlay()
        {
            if (!playing) return;
            playing = false;
            playBtn.Text = Lang.T("▶ Играть (F7)", "▶ Play (F7)");
            playBtn.Invalidate();
            statusLabel.Text = Lang.T("Остановлено", "Stopped");
            statusLabel.ForeColor = Theme.TextDim;
        }

        // ---- Сохранение/загрузка ----
        private static string MacrosDir
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools"),
                    "macros");
            }
        }

        private void SaveCurrent()
        {
            if (events.Count == 0)
            {
                statusLabel.Text = Lang.T("Нечего сохранять - запиши макрос", "Nothing to save - record a macro first");
                statusLabel.ForeColor = Theme.Warning;
                return;
            }

            string name = (nameBox.Text ?? "").Trim();
            if (name == "") name = "macro_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            name = SanitizeName(name);

            try
            {
                if (!Directory.Exists(MacrosDir)) Directory.CreateDirectory(MacrosDir);
                var lines = new List<string>();
                foreach (MacroEvent ev in events)
                {
                    if (ev.Kind == 'K')
                        lines.Add("K|" + ev.Delay + "|" + ev.Vk + "|" + (ev.Down ? 1 : 0));
                    else
                        lines.Add("M|" + ev.Delay + "|" + ev.Button + "|" + (ev.Down ? 1 : 0) + "|" + ev.X + "|" + ev.Y);
                }
                File.WriteAllLines(Path.Combine(MacrosDir, name + ".dtm"), lines.ToArray());

                statusLabel.Text = Lang.T("Сохранено: ", "Saved: ") + name;
                statusLabel.ForeColor = Theme.Accent;
                nameBox.Text = "";
                RefreshSavedList();
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Не удалось сохранить: ", "Failed to save: ") + ex.Message;
                statusLabel.ForeColor = Theme.Warning;
            }
        }

        private void LoadMacro(string path)
        {
            try
            {
                var loaded = new List<MacroEvent>();
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] p = lines[i].Split('|');
                    if (p.Length < 4) continue;
                    int delay; int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out delay);
                    if (p[0] == "K")
                    {
                        int vk; int.TryParse(p[2], out vk);
                        loaded.Add(new MacroEvent { Kind = 'K', Delay = delay, Vk = vk, Down = p[3] == "1" });
                    }
                    else if (p[0] == "M" && p.Length >= 6)
                    {
                        int btn, x, y;
                        int.TryParse(p[2], out btn);
                        int.TryParse(p[4], out x);
                        int.TryParse(p[5], out y);
                        loaded.Add(new MacroEvent { Kind = 'M', Delay = delay, Button = btn, Down = p[3] == "1", X = x, Y = y });
                    }
                }
                events = loaded;
                UpdateEventsLabel();
                statusLabel.Text = Lang.T("Загружено: ", "Loaded: ") + Path.GetFileNameWithoutExtension(path) + " (" + events.Count + ")";
                statusLabel.ForeColor = Theme.Accent;
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Не удалось загрузить: ", "Failed to load: ") + ex.Message;
                statusLabel.ForeColor = Theme.Warning;
            }
        }

        private void RefreshSavedList()
        {
            savedList.Controls.Clear();

            string[] files;
            try
            {
                if (!Directory.Exists(MacrosDir)) { ShowEmptySaved(); return; }
                files = Directory.GetFiles(MacrosDir, "*.dtm");
            }
            catch { ShowEmptySaved(); return; }

            if (files.Length == 0) { ShowEmptySaved(); return; }
            Array.Sort(files);

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                string name = Path.GetFileNameWithoutExtension(path);

                var row = new Panel { Size = new Size(444, 34), BackColor = Color.Transparent, Margin = new Padding(2, 1, 0, 1) };

                var nameLbl = new Label
                {
                    Text = name,
                    ForeColor = Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Location = new Point(6, 8),
                    Size = new Size(280, 18),
                    AutoEllipsis = true
                };
                row.Controls.Add(nameLbl);

                var loadBtn = new RoundedButton
                {
                    Text = Lang.T("Загрузить", "Load"),
                    ButtonColor = Theme.KeyColor,
                    HoverColor = Theme.KeyHover,
                    TextColor = Theme.TextMain,
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    Location = new Point(292, 4),
                    Size = new Size(84, 26)
                };
                loadBtn.Click += (s, e) => LoadMacro(path);
                row.Controls.Add(loadBtn);

                var delBtn = new RoundedButton
                {
                    Text = "✕",
                    ButtonColor = Theme.KeyColor,
                    HoverColor = Theme.DangerHover,
                    TextColor = Theme.TextMain,
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    Location = new Point(382, 4),
                    Size = new Size(40, 26)
                };
                delBtn.Click += (s, e) => {
                    try { File.Delete(path); } catch { }
                    RefreshSavedList();
                };
                row.Controls.Add(delBtn);

                savedList.Controls.Add(row);
            }
        }

        private void ShowEmptySaved()
        {
            var empty = new Label
            {
                Text = Lang.T("Сохранённых макросов нет", "No saved macros"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(6, 6),
                AutoSize = true
            };
            savedList.Controls.Add(empty);
        }

        private static string SanitizeName(string name)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in bad) name = name.Replace(c, '_');
            if (name.Length > 60) name = name.Substring(0, 60);
            return name;
        }

        private void Cleanup()
        {
            recording = false;
            playing = false;
            UnhookAll();
            UnregisterHotkeys();
        }
    }
}
