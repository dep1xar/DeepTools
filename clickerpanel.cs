using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    public class MousePicker : TransparentControl
    {
        public bool RightSelected = false;
        public bool Dimmed = false;
        public event EventHandler SelectionChanged;

        public MousePicker()
        {
            Size = new Size(100, 130);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            bool clickedRight = e.X > Width / 2;
            RightSelected = clickedRight;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle bodyRect = new Rectangle(4, 4, Width - 8, Height - 8);
            int buttonHeight = bodyRect.Height * 40 / 100;

            Color leftColor = Dimmed
                ? Theme.KeyColor
                : (RightSelected ? Theme.KeyColor : Theme.Accent);
            Color rightColor = Dimmed
                ? Theme.KeyColor
                : (RightSelected ? Theme.Accent : Theme.KeyColor);

            using (GraphicsPath body = Theme.RoundedRect(bodyRect, 22))
            {
                Region oldClip = g.Clip;
                g.SetClip(body, CombineMode.Replace);

                Rectangle leftZone = new Rectangle(bodyRect.X, bodyRect.Y, bodyRect.Width / 2, buttonHeight);
                Rectangle rightZone = new Rectangle(bodyRect.X + bodyRect.Width / 2, bodyRect.Y, bodyRect.Width - bodyRect.Width / 2, buttonHeight);
                Rectangle lowerZone = new Rectangle(bodyRect.X, bodyRect.Y + buttonHeight, bodyRect.Width, bodyRect.Height - buttonHeight);

                using (var b1 = new SolidBrush(leftColor)) g.FillRectangle(b1, leftZone);
                using (var b2 = new SolidBrush(rightColor)) g.FillRectangle(b2, rightZone);
                using (var b3 = new SolidBrush(Theme.InputColor)) g.FillRectangle(b3, lowerZone);

                g.Clip = oldClip;
            }

            using (var linePen = new Pen(Theme.BgColor, 2))
            {
                g.DrawLine(linePen, bodyRect.X + bodyRect.Width / 2, bodyRect.Y + 4, bodyRect.X + bodyRect.Width / 2, bodyRect.Y + buttonHeight - 2);
                g.DrawLine(linePen, bodyRect.X + 4, bodyRect.Y + buttonHeight, bodyRect.X + bodyRect.Width - 4, bodyRect.Y + buttonHeight);
            }

            Rectangle wheelRect = new Rectangle(bodyRect.X + bodyRect.Width / 2 - 4, bodyRect.Y + 6, 8, 14);
            using (var wheelBrush = new SolidBrush(Theme.TextDim))
            {
                g.FillRectangle(wheelBrush, wheelRect);
            }

            using (GraphicsPath outline = Theme.RoundedRect(bodyRect, 22))
            using (var outlinePen = new Pen(Theme.BorderColor, 2))
            {
                g.DrawPath(outlinePen, outline);
            }
        }
    }

    public class ClickerPanel : Panel
    {
        private class KeyBtn
        {
            public Keys Key;
            public string Label;
            public RoundedButton Btn;
        }

        private bool running = false;
        private bool isKeyboardMode = false;
        private bool holdMode = false;     // false = спамить, true = зажимать
        private bool holdActive = false;   // сейчас что-то физически зажато
        private Keys selectedKey = Keys.W;
        private System.Windows.Forms.Timer clickTimer = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer holdRepeatTimer = new System.Windows.Forms.Timer();

        private RoundedButton spamModeBtn;
        private RoundedButton holdModeBtn;

        private Label statusLabel;
        private Panel statusDot;
        private ToggleSwitch powerToggle;
        private MousePicker mousePicker;
        private Label leftBtnLabel;
        private Label rightBtnLabel;
        private Label selectionLabel;

        private Label intervalValueLabel;
        private TextBox intervalEditBox;
        private NeonSlider slider;
        private Label cpsNumber;

        private List<KeyBtn> keyButtons = new List<KeyBtn>();

        // Режим нескольких клавиш: спамим сразу все выбранные (например e, w, r)
        private bool multiKeyMode = false;
        private List<Keys> multiKeys = new List<Keys>();
        private ToggleSwitch multiKeyToggle;

        private string selectedKeyLabel = "W";
        private long sessionClicks = 0;
        private long totalClicks = 0;
        private Label sessionClicksLabel;
        private Label totalClicksLabel;

        public ClickerPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            long.TryParse(AppConfig.Get("clicker_total_clicks", "0"), out totalClicks);

            clickTimer.Tick += (s, e) => DoClick();
            holdRepeatTimer.Interval = 30; // как системный автоповтор клавиатуры
            holdRepeatTimer.Tick += (s, e) => HoldTick();

            BuildUi();
            UpdateModeButtons();
        }

        // Отпустить всё зажатое (вызывается при выходе из программы)
        public void ReleaseAll()
        {
            holdRepeatTimer.Enabled = false;
            if (holdActive) HoldUp();
            SaveTotalClicks();
        }

        // Сохраняем счётчик при выключении кликера, а не на каждом клике,
        // чтобы не дёргать запись конфига сотни раз в секунду
        private void SaveTotalClicks()
        {
            AppConfig.Set("clicker_total_clicks", totalClicks.ToString());
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("⚡ Автокликер", "⚡ Autoclicker"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 12),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var macrosBtn = new RoundedButton
            {
                Text = Lang.T("🎬 Макросы", "🎬 Macros"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(596, 14),
                Size = new Size(140, 32)
            };
            macrosBtn.Click += (s, e) => {
                using (var f = new MacroForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(macrosBtn);

            var statusCard = Theme.MakeCard(this, new Point(24, 52), new Size(712, 88));

            statusDot = new Panel { Size = new Size(14, 14), Location = new Point(20, 17), BackColor = Color.Transparent };
            statusDot.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Color c = running ? Theme.Accent : Theme.TextDim;
                using (var glow = new SolidBrush(Color.FromArgb(60, c)))
                    e.Graphics.FillEllipse(glow, -3, -3, 20, 20);
                using (var b = new SolidBrush(c))
                    e.Graphics.FillEllipse(b, 0, 0, 14, 14);
            };
            statusCard.Controls.Add(statusDot);

            // Верхний ряд: статус слева, включение справа
            statusLabel = new Label
            {
                Text = Lang.T("ОСТАНОВЛЕН", "STOPPED"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(42, 13),
                AutoSize = true
            };
            statusCard.Controls.Add(statusLabel);

            var powerLabel = new Label
            {
                Text = Lang.T("Включить", "Enable"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(500, 14),
                AutoSize = true
            };
            statusCard.Controls.Add(powerLabel);

            powerToggle = new ToggleSwitch { Location = new Point(575, 12), Checked = false };
            powerToggle.CheckedChanged += (s, e) => SetRunning(powerToggle.Checked);
            statusCard.Controls.Add(powerToggle);

            var hintLabel = new Label
            {
                Text = Hotkeys.Get(NativeMethods.HOTKEY_ID_CLICKER).ToString(),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(635, 16),
                AutoSize = true
            };
            statusCard.Controls.Add(hintLabel);

            // Нижний ряд: режим работы и подпись, что выбрано
            var modeLabel = new Label
            {
                Text = Lang.T("Режим:", "Mode:"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(20, 53),
                AutoSize = true
            };
            statusCard.Controls.Add(modeLabel);

            spamModeBtn = new RoundedButton
            {
                Text = Lang.T("Спамить", "Spam"),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(78, 48),
                Size = new Size(84, 28)
            };
            spamModeBtn.Click += (s, e) => SetHoldMode(false);
            statusCard.Controls.Add(spamModeBtn);

            holdModeBtn = new RoundedButton
            {
                Text = Lang.T("Зажимать", "Hold"),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(168, 48),
                Size = new Size(84, 28)
            };
            holdModeBtn.Click += (s, e) => SetHoldMode(true);
            statusCard.Controls.Add(holdModeBtn);

            selectionLabel = new Label
            {
                Text = Lang.T("Спамим: левая кнопка мыши", "Spamming: left mouse button"),
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(280, 54),
                Size = new Size(410, 18),
                AutoSize = false,
                AutoEllipsis = true
            };
            statusCard.Controls.Add(selectionLabel);

            // Карточка мыши
            var mouseCard = Theme.MakeCard(this, new Point(24, 152), new Size(240, 152));

            var mouseTitle = new Label
            {
                Text = Lang.T("Мышь", "Mouse"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(18, 12),
                AutoSize = true
            };
            mouseCard.Controls.Add(mouseTitle);

            mousePicker = new MousePicker { Location = new Point(18, 40), Size = new Size(80, 100) };
            mousePicker.SelectionChanged += (s, e) => SelectMouse(mousePicker.RightSelected);
            mouseCard.Controls.Add(mousePicker);

            leftBtnLabel = new Label
            {
                Text = Lang.T("◉ Левая", "◉ Left"),
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(112, 56),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            leftBtnLabel.Click += (s, e) => { mousePicker.RightSelected = false; mousePicker.Invalidate(); SelectMouse(false); };
            mouseCard.Controls.Add(leftBtnLabel);

            rightBtnLabel = new Label
            {
                Text = Lang.T("○ Правая", "○ Right"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(112, 86),
                AutoSize = true,
                Cursor = Cursors.Hand
            };
            rightBtnLabel.Click += (s, e) => { mousePicker.RightSelected = true; mousePicker.Invalidate(); SelectMouse(true); };
            mouseCard.Controls.Add(rightBtnLabel);

            // Карточка интервала и скорости
            var statsCard = Theme.MakeCard(this, new Point(280, 152), new Size(456, 152));

            var intervalLabel = new Label
            {
                Text = Lang.T("Интервал (мс)", "Interval (ms)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(18, 16),
                AutoSize = true
            };
            statsCard.Controls.Add(intervalLabel);

            intervalValueLabel = new Label
            {
                Text = "100",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                Location = new Point(200, 12),
                AutoSize = true,
                Cursor = Cursors.IBeam
            };
            intervalValueLabel.Click += (s, e) => ShowIntervalEditor();
            statsCard.Controls.Add(intervalValueLabel);

            intervalEditBox = new TextBox
            {
                Location = new Point(200, 12),
                Size = new Size(50, 24),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle,
                Visible = false,
                MaxLength = 4,
                TextAlign = HorizontalAlignment.Center
            };
            intervalEditBox.KeyPress += (s, e) => {
                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
            };
            intervalEditBox.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter) { CommitIntervalEditor(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { intervalEditBox.Visible = false; intervalValueLabel.Visible = true; }
            };
            intervalEditBox.Leave += (s, e) => CommitIntervalEditor();
            statsCard.Controls.Add(intervalEditBox);

            slider = new NeonSlider { Location = new Point(18, 52), Size = new Size(260, 20), Minimum = 10, Maximum = 1000, Value = 100 };
            slider.ValueChanged += (s, e) => UpdateInterval();
            statsCard.Controls.Add(slider);

            var minLbl = new Label { Text = "10", ForeColor = Theme.TextDim, BackColor = Color.Transparent, Font = new Font("Segoe UI", 8F), Location = new Point(18, 78), AutoSize = true };
            var maxLbl = new Label { Text = "1000", ForeColor = Theme.TextDim, BackColor = Color.Transparent, Font = new Font("Segoe UI", 8F), Location = new Point(252, 78), AutoSize = true };
            statsCard.Controls.Add(minLbl);
            statsCard.Controls.Add(maxLbl);

            cpsNumber = new Label
            {
                Text = "10",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 38F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(300, 24),
                Size = new Size(140, 70)
            };
            statsCard.Controls.Add(cpsNumber);

            var cpsSub = new Label
            {
                Text = Lang.T("кликов/сек", "clicks/sec"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(300, 96),
                Size = new Size(140, 18)
            };
            statsCard.Controls.Add(cpsSub);

            sessionClicksLabel = new Label
            {
                Text = Lang.T("За сессию: 0", "Session: 0"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 100),
                AutoSize = true
            };
            statsCard.Controls.Add(sessionClicksLabel);

            totalClicksLabel = new Label
            {
                Text = Lang.T("Всего: ", "Total: ") + totalClicks.ToString("N0"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 120),
                AutoSize = true
            };
            statsCard.Controls.Add(totalClicksLabel);

            // Карточка клавиатуры
            var keyboardCard = Theme.MakeCard(this, new Point(24, 316), new Size(712, 290));

            var keysTitle = new Label
            {
                Text = Lang.T("Или клавиша (спам вместо клика)", "Or a key (spam instead of clicking)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(18, 12),
                AutoSize = true
            };
            keyboardCard.Controls.Add(keysTitle);

            var multiLabel = new Label
            {
                Text = Lang.T("Несколько клавиш", "Multiple keys"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(470, 14),
                AutoSize = true
            };
            keyboardCard.Controls.Add(multiLabel);

            multiKeyToggle = new ToggleSwitch { Location = new Point(600, 10), Checked = false };
            multiKeyToggle.CheckedChanged += (s, e) => OnMultiKeyToggle();
            keyboardCard.Controls.Add(multiKeyToggle);

            BuildKeyboard(keyboardCard, 18, 42);

            UpdateInterval();
            SelectMouse(false);
        }

        // Включение/выключение режима нескольких клавиш. При смене чистим выбор,
        // чтобы не было каши между одиночным и мульти-режимом
        private void OnMultiKeyToggle()
        {
            multiKeyMode = multiKeyToggle.Checked;
            multiKeys.Clear();
            if (holdActive) { HoldUp(); if (running) SetRunning(false); }
            ResetKeyHighlight();
            if (multiKeyMode)
            {
                // Мультирежим работает как спам-клавиатура
                isKeyboardMode = true;
                mousePicker.Dimmed = true;
                mousePicker.Invalidate();
                leftBtnLabel.ForeColor = Theme.TextDim; leftBtnLabel.Text = Lang.T("○ Левая", "○ Left");
                rightBtnLabel.ForeColor = Theme.TextDim; rightBtnLabel.Text = Lang.T("○ Правая", "○ Right");
            }
            UpdateSelectionLabel();
        }

        // Раскладка: {подпись, клавиша, ширина}. Ширина 0 = стандартные 40px
        private void BuildKeyboard(Panel parent, int startX, int startY)
        {
            int keyH = 36;
            int gap = 4;

            object[][][] rows = new object[][][]
            {
                new object[][] {
                    K("Esc", Keys.Escape), K("F1", Keys.F1), K("F2", Keys.F2), K("F3", Keys.F3), K("F4", Keys.F4),
                    K("F5", Keys.F5), K("F6", Keys.F6), K("F7", Keys.F7), K("F9", Keys.F9),
                    K("F10", Keys.F10), K("F11", Keys.F11), K("F12", Keys.F12)
                },
                new object[][] {
                    K("`", Keys.Oemtilde), K("1", Keys.D1), K("2", Keys.D2), K("3", Keys.D3), K("4", Keys.D4),
                    K("5", Keys.D5), K("6", Keys.D6), K("7", Keys.D7), K("8", Keys.D8), K("9", Keys.D9),
                    K("0", Keys.D0), K("-", Keys.OemMinus), K("=", Keys.Oemplus), K("⌫", Keys.Back, 76)
                },
                new object[][] {
                    K("Tab", Keys.Tab, 62), K("Q", Keys.Q), K("W", Keys.W), K("E", Keys.E), K("R", Keys.R),
                    K("T", Keys.T), K("Y", Keys.Y), K("U", Keys.U), K("I", Keys.I), K("O", Keys.O),
                    K("P", Keys.P), K("[", Keys.OemOpenBrackets), K("]", Keys.OemCloseBrackets), K("\\", Keys.OemPipe)
                },
                new object[][] {
                    K("A", Keys.A), K("S", Keys.S), K("D", Keys.D), K("F", Keys.F), K("G", Keys.G),
                    K("H", Keys.H), K("J", Keys.J), K("K", Keys.K), K("L", Keys.L),
                    K(";", Keys.OemSemicolon), K("'", Keys.OemQuotes), K("Enter", Keys.Enter, 76)
                },
                new object[][] {
                    K("Shift", Keys.ShiftKey, 84), K("Z", Keys.Z), K("X", Keys.X), K("C", Keys.C), K("V", Keys.V),
                    K("B", Keys.B), K("N", Keys.N), K("M", Keys.M),
                    K(",", Keys.Oemcomma), K(".", Keys.OemPeriod), K("/", Keys.OemQuestion)
                },
                new object[][] {
                    K("Ctrl", Keys.ControlKey, 58), K("Alt", Keys.Menu, 58), K(Lang.T("Пробел", "Space"), Keys.Space, 250),
                    K("←", Keys.Left), K("↑", Keys.Up), K("↓", Keys.Down), K("→", Keys.Right)
                }
            };

            int y = startY;
            foreach (object[][] row in rows)
            {
                int x = startX;
                foreach (object[] def in row)
                {
                    string label = (string)def[0];
                    Keys key = (Keys)def[1];
                    int w = (int)def[2];

                    var btn = new RoundedButton
                    {
                        Text = label,
                        Location = new Point(x, y),
                        Size = new Size(w, keyH),
                        ButtonColor = Theme.KeyColor,
                        HoverColor = Theme.KeyHover,
                        TextColor = Theme.TextMain,
                        Font = new Font("Segoe UI", label.Length > 3 ? 7.5F : 8.5F, FontStyle.Bold)
                    };
                    Keys capturedKey = key;
                    string capturedLabel = label;
                    btn.Click += (s, e) => SelectKeyboard(capturedKey, capturedLabel);
                    parent.Controls.Add(btn);
                    keyButtons.Add(new KeyBtn { Key = key, Label = label, Btn = btn });

                    x += w + gap;
                }
                y += keyH + gap;
            }
        }

        private object[] K(string label, Keys key)
        {
            return new object[] { label, key, 40 };
        }

        private object[] K(string label, Keys key, int width)
        {
            return new object[] { label, key, width };
        }

        private void SelectMouse(bool rightSelected)
        {
            // Если сейчас что-то зажато - отпускаем перед сменой цели
            if (holdActive) { HoldUp(); if (running) SetRunning(false); }

            // Выбор мыши выключает мультиклавишный режим
            if (multiKeyMode)
            {
                multiKeyMode = false;
                multiKeys.Clear();
                if (multiKeyToggle != null) { multiKeyToggle.Checked = false; multiKeyToggle.Invalidate(); }
            }

            isKeyboardMode = false;
            mousePicker.RightSelected = rightSelected;
            mousePicker.Dimmed = false;
            mousePicker.Invalidate();

            leftBtnLabel.ForeColor = rightSelected ? Theme.TextDim : Theme.Accent;
            leftBtnLabel.Text = rightSelected ? Lang.T("○ Левая", "○ Left") : Lang.T("◉ Левая", "◉ Left");
            rightBtnLabel.ForeColor = rightSelected ? Theme.Accent : Theme.TextDim;
            rightBtnLabel.Text = rightSelected ? Lang.T("◉ Правая", "◉ Right") : Lang.T("○ Правая", "○ Right");

            ResetKeyHighlight();

            UpdateSelectionLabel();
        }

        // Подпись "что делаем и с чем" с учётом режима
        private void UpdateSelectionLabel()
        {
            string action = holdMode ? Lang.T("Зажимаем: ", "Holding: ") : Lang.T("Спамим: ", "Spamming: ");
            string target;
            if (multiKeyMode)
            {
                if (multiKeys.Count == 0) target = Lang.T("выбери клавиши", "pick keys");
                else
                {
                    var names = new List<string>();
                    for (int i = 0; i < keyButtons.Count; i++)
                        if (multiKeys.Contains(keyButtons[i].Key)) names.Add(keyButtons[i].Label);
                    target = Lang.T("клавиши ", "keys ") + string.Join(", ", names.ToArray());
                }
            }
            else if (isKeyboardMode)
            {
                target = Lang.T("клавиша ", "key ") + selectedKeyLabel;
            }
            else
            {
                target = mousePicker.RightSelected
                    ? Lang.T("правая кнопка мыши", "right mouse button")
                    : Lang.T("левая кнопка мыши", "left mouse button");
            }
            selectionLabel.Text = action + target;
        }

        private void ResetKeyHighlight()
        {
            for (int i = 0; i < keyButtons.Count; i++)
            {
                keyButtons[i].Btn.ButtonColor = Theme.KeyColor;
                keyButtons[i].Btn.TextColor = Theme.TextMain;
                keyButtons[i].Btn.Invalidate();
            }
        }

        private void SelectKeyboard(Keys key, string label)
        {
            // Если сейчас что-то зажато - отпускаем перед сменой цели
            if (holdActive) { HoldUp(); if (running) SetRunning(false); }

            isKeyboardMode = true;
            selectedKey = key;
            selectedKeyLabel = label;

            mousePicker.Dimmed = true;
            mousePicker.Invalidate();
            leftBtnLabel.ForeColor = Theme.TextDim;
            leftBtnLabel.Text = Lang.T("○ Левая", "○ Left");
            rightBtnLabel.ForeColor = Theme.TextDim;
            rightBtnLabel.Text = Lang.T("○ Правая", "○ Right");

            // В мультирежиме клик по клавише добавляет/убирает её из набора
            if (multiKeyMode)
            {
                if (multiKeys.Contains(key)) multiKeys.Remove(key);
                else multiKeys.Add(key);
            }

            for (int i = 0; i < keyButtons.Count; i++)
            {
                bool selected = multiKeyMode ? multiKeys.Contains(keyButtons[i].Key) : keyButtons[i].Key == key;
                keyButtons[i].Btn.ButtonColor = selected ? Theme.Accent : Theme.KeyColor;
                keyButtons[i].Btn.TextColor = selected ? Theme.BgColor : Theme.TextMain;
                keyButtons[i].Btn.Invalidate();
            }

            UpdateSelectionLabel();
        }

        private void ShowIntervalEditor()
        {
            intervalEditBox.Text = slider.Value.ToString();
            intervalEditBox.Visible = true;
            intervalValueLabel.Visible = false;
            intervalEditBox.Focus();
            intervalEditBox.SelectAll();
        }

        private void CommitIntervalEditor()
        {
            if (!intervalEditBox.Visible) return;
            int v;
            if (int.TryParse(intervalEditBox.Text, out v))
            {
                slider.Value = v;
                UpdateInterval();
            }
            intervalEditBox.Visible = false;
            intervalValueLabel.Visible = true;
        }

        private void UpdateInterval()
        {
            intervalValueLabel.Text = slider.Value.ToString();
            clickTimer.Interval = slider.Value;
            double cps = 1000.0 / slider.Value;
            cpsNumber.Text = cps >= 10 ? Math.Round(cps).ToString() : cps.ToString("0.#");
        }

        // Переключение режима Спамить/Зажимать. При смене на ходу останавливаем работу,
        // чтобы не осталось "подвисшей" зажатой кнопки
        private void SetHoldMode(bool hold)
        {
            if (holdMode == hold) return;
            if (running) SetRunning(false);
            holdMode = hold;
            UpdateModeButtons();
            UpdateSelectionLabel();
        }

        private void UpdateModeButtons()
        {
            spamModeBtn.ButtonColor = holdMode ? Theme.KeyColor : Theme.Accent;
            spamModeBtn.HoverColor = holdMode ? Theme.KeyHover : Theme.AccentHover;
            spamModeBtn.TextColor = holdMode ? Theme.TextMain : Theme.BgColor;
            holdModeBtn.ButtonColor = holdMode ? Theme.Accent : Theme.KeyColor;
            holdModeBtn.HoverColor = holdMode ? Theme.AccentHover : Theme.KeyHover;
            holdModeBtn.TextColor = holdMode ? Theme.BgColor : Theme.TextMain;
            spamModeBtn.Invalidate();
            holdModeBtn.Invalidate();
        }

        // Физически зажать/отпустить выбранную кнопку или клавишу.
        // Для клавиатуры одного события "вниз" мало: настоящая зажатая клавиша
        // шлёт автоповторы, и программы печатают повторы именно по ним.
        // Поэтому в режиме зажатия таймер повторяет KeyDown (см. HoldTick)
        private void HoldDown()
        {
            if (holdActive) return;
            if (isKeyboardMode) NativeMethods.KeyDown(selectedKey);
            else if (mousePicker.RightSelected) NativeMethods.MouseRightDown();
            else NativeMethods.MouseLeftDown();
            holdActive = true;
        }

        // Повтор "вниз" для эмуляции автоповтора зажатой клавиши.
        // Для мыши повторы не нужны - кнопка честно зажата одним событием
        private void HoldTick()
        {
            if (!holdActive || !isKeyboardMode) return;
            NativeMethods.KeyDown(selectedKey);
        }

        private void HoldUp()
        {
            if (!holdActive) return;
            if (isKeyboardMode) NativeMethods.KeyUp(selectedKey);
            else if (mousePicker.RightSelected) NativeMethods.MouseRightUp();
            else NativeMethods.MouseLeftUp();
            holdActive = false;
        }

        private void SetRunning(bool value)
        {
            running = value;

            if (holdMode)
            {
                // Режим "Зажимать": держим кнопку, а таймер шлёт автоповторы
                // (как настоящая клавиатура) с системным интервалом ~30 мс
                if (running)
                {
                    HoldDown();
                    holdRepeatTimer.Enabled = true;
                }
                else
                {
                    holdRepeatTimer.Enabled = false;
                    HoldUp();
                }
                clickTimer.Enabled = false;
            }
            else
            {
                holdRepeatTimer.Enabled = false;
                clickTimer.Enabled = running;
                if (!running) HoldUp(); // подстраховка после смены режимов
            }

            if (!running) SaveTotalClicks();
            statusLabel.Text = running
                ? (holdMode ? Lang.T("ЗАЖАТО", "HOLDING") : Lang.T("РАБОТАЕТ", "RUNNING"))
                : Lang.T("ОСТАНОВЛЕН", "STOPPED");
            statusLabel.ForeColor = running ? Theme.Accent : Theme.TextDim;
            powerToggle.Checked = running;
            powerToggle.Invalidate();
            statusDot.Invalidate();
        }

        public void ToggleRunning()
        {
            SetRunning(!running);
        }

        private void DoClick()
        {
            if (multiKeyMode)
            {
                // Спамим все выбранные клавиши по очереди
                for (int i = 0; i < multiKeys.Count; i++) NativeMethods.PressKey(multiKeys[i]);
            }
            else if (isKeyboardMode) NativeMethods.PressKey(selectedKey);
            else if (mousePicker.RightSelected) NativeMethods.ClickRight();
            else NativeMethods.ClickLeft();

            sessionClicks++;
            totalClicks++;
            sessionClicksLabel.Text = Lang.T("За сессию: ", "Session: ") + sessionClicks.ToString("N0");
            totalClicksLabel.Text = Lang.T("Всего: ", "Total: ") + totalClicks.ToString("N0");
        }
    }
}
