using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    // Настройка файла подкачки. Раньше карточка в «Игровых твиках» просто
    // переключала «0 0 / 1024 4096», и свой размер задать было нельзя.
    // Теперь по клику открывается это окно: текущее состояние, выбор
    // «по системе» или свои начальный и максимальный размеры.
    public class PagefileSettingsForm : Form
    {
        private Label currentLbl;
        private Label diskLbl;
        private RoundedButton systemBtn;
        private RoundedButton customBtn;
        private TextBox initialBox;
        private TextBox maxBox;
        private Label initialLbl;
        private Label maxLbl;
        private Label hintLbl;
        private RoundedButton applyBtn;
        private bool customMode;
        private bool busy;

        public static void Open(IWin32Window owner)
        {
            using (var form = new PagefileSettingsForm())
            {
                if (owner != null) form.ShowDialog(owner);
                else form.ShowDialog();
            }
        }

        public PagefileSettingsForm()
        {
            Text = "DeepTools";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(430, 372);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            LoadCurrent();
            Load += (s, e) => ApplyRoundedRegion();
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
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.BgColor };
            WindowDrag.Attach(titleBar, this);
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("Файл подкачки", "Page file"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 11),
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
                Location = new Point(Width - 42, 9),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => Close();
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            var currentCard = Theme.MakeCard(this, new Point(16, 56), new Size(398, 86));

            currentLbl = new Label
            {
                Text = "",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            currentCard.Controls.Add(currentLbl);

            diskLbl = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 38),
                AutoSize = true
            };
            currentCard.Controls.Add(diskLbl);

            var rebootLbl = new Label
            {
                Text = Lang.T("Изменения применятся после перезагрузки Windows.",
                              "Changes take effect after a Windows restart."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(16, 60),
                AutoSize = true
            };
            currentCard.Controls.Add(rebootLbl);

            var sizeCard = Theme.MakeCard(this, new Point(16, 154), new Size(398, 158));

            systemBtn = new RoundedButton
            {
                Text = Lang.T("По системе", "System managed"),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(16, 14),
                Size = new Size(150, 32)
            };
            systemBtn.Click += (s, e) => SetMode(false);
            sizeCard.Controls.Add(systemBtn);

            customBtn = new RoundedButton
            {
                Text = Lang.T("Свой размер", "Custom size"),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(174, 14),
                Size = new Size(150, 32)
            };
            customBtn.Click += (s, e) => SetMode(true);
            sizeCard.Controls.Add(customBtn);

            initialLbl = new Label
            {
                Text = Lang.T("Начальный, МБ", "Initial, MB"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 58),
                AutoSize = true
            };
            sizeCard.Controls.Add(initialLbl);

            initialBox = new TextBox
            {
                Location = new Point(16, 78),
                Size = new Size(150, 28),
                Font = new Font("Segoe UI", 10F),
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle
            };
            sizeCard.Controls.Add(initialBox);

            maxLbl = new Label
            {
                Text = Lang.T("Максимальный, МБ", "Maximum, MB"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(206, 58),
                AutoSize = true
            };
            sizeCard.Controls.Add(maxLbl);

            maxBox = new TextBox
            {
                Location = new Point(206, 78),
                Size = new Size(150, 28),
                Font = new Font("Segoe UI", 10F),
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle
            };
            sizeCard.Controls.Add(maxBox);

            hintLbl = new Label
            {
                Text = Lang.T("Windows обычно сам подбирает размер. Свой нужен, если мало RAM.",
                              "Windows picks the size itself. Set a custom one if RAM is low."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(16, 112),
                Size = new Size(366, 34)
            };
            sizeCard.Controls.Add(hintLbl);

            applyBtn = new RoundedButton
            {
                Text = Lang.T("Применить", "Apply"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(278, 324),
                Size = new Size(120, 34)
            };
            applyBtn.Click += (s, e) => Apply();
            Controls.Add(applyBtn);

            var cancelBtn = new RoundedButton
            {
                Text = Lang.T("Отмена", "Cancel"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(150, 324),
                Size = new Size(120, 34)
            };
            cancelBtn.Click += (s, e) => Close();
            Controls.Add(cancelBtn);
        }

        private void LoadCurrent()
        {
            string spec = GameTweaksPanel.CurrentPagefileSpec();
            bool managed = string.IsNullOrEmpty(spec) || spec == "0 0";

            if (string.IsNullOrEmpty(spec))
                currentLbl.Text = Lang.T("Размер не задан", "Size is not set");
            else if (managed)
                currentLbl.Text = Lang.T("Сейчас: по выбору системы", "Now: system managed");
            else
                currentLbl.Text = Lang.T("Сейчас: свой размер", "Now: custom size") + " (" + spec + ")";

            string root = "C:\\";
            try
            {
                string r = Path.GetPathRoot(Environment.SystemDirectory);
                if (!string.IsNullOrEmpty(r)) root = r;
            }
            catch { }

            string disk = root + "pagefile.sys";
            try
            {
                var drive = new DriveInfo(root);
                long free = drive.AvailableFreeSpace / (1024L * 1024L * 1024L);
                long total = drive.TotalSize / (1024L * 1024L * 1024L);
                disk += " · " + Lang.T("свободно ", "free ") + free + " / " + total + Lang.T(" ГБ", " GB");
            }
            catch { }
            diskLbl.Text = disk;

            initialBox.Text = "1024";
            maxBox.Text = "4096";
            if (!managed && !string.IsNullOrEmpty(spec))
            {
                string[] parts = spec.Split(' ');
                int init, max;
                if (parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out init))
                    initialBox.Text = init.ToString(CultureInfo.InvariantCulture);
                if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out max))
                    maxBox.Text = max.ToString(CultureInfo.InvariantCulture);
            }

            SetMode(!managed);
        }

        private void SetMode(bool custom)
        {
            customMode = custom;

            StyleSegment(systemBtn, !custom);
            StyleSegment(customBtn, custom);

            // ReadOnly вместо Enabled=false: отключённый TextBox рисуется
            // системным светло-серым и выбивается из тёмной темы
            initialBox.ReadOnly = !custom;
            maxBox.ReadOnly = !custom;
            initialBox.BackColor = custom ? Theme.InputColor : Theme.SidebarColor;
            maxBox.BackColor = custom ? Theme.InputColor : Theme.SidebarColor;
            initialBox.ForeColor = custom ? Theme.TextMain : Theme.TextDim;
            maxBox.ForeColor = custom ? Theme.TextMain : Theme.TextDim;
            initialLbl.ForeColor = custom ? Theme.TextMain : Theme.TextDim;
            maxLbl.ForeColor = custom ? Theme.TextMain : Theme.TextDim;
            hintLbl.Text = custom
                ? Lang.T("Максимальный размер не меньше начального. Обычно хватает 1024-4096 МБ.",
                         "Maximum must be at least the initial size. 1024-4096 MB is usually enough.")
                : Lang.T("Windows обычно сам подбирает размер. Свой нужен, если мало RAM.",
                         "Windows picks the size itself. Set a custom one if RAM is low.");
        }

        private static void StyleSegment(RoundedButton button, bool active)
        {
            button.ButtonColor = active ? Theme.Accent : Theme.KeyColor;
            button.HoverColor = active ? Theme.AccentHover : Theme.KeyHover;
            button.TextColor = active ? Theme.BgColor : Theme.TextMain;
            button.Invalidate();
        }

        private void Apply()
        {
            if (busy) return;

            string spec = "0 0";
            if (customMode)
            {
                int initial, max;
                if (!int.TryParse(initialBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out initial)
                    || !int.TryParse(maxBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out max))
                {
                    DTDialog.Show(Lang.T("Размеры должны быть целыми числами в мегабайтах.",
                                         "Sizes must be whole numbers in megabytes."), "DeepTools");
                    return;
                }
                if (initial < 16 || max < initial)
                {
                    DTDialog.Show(Lang.T("Начальный размер - не меньше 16 МБ, максимальный - не меньше начального.",
                                         "Initial size must be at least 16 MB, maximum must not be smaller than initial."), "DeepTools");
                    return;
                }
                if (max > 262144)
                {
                    DTDialog.Show(Lang.T("Максимальный размер слишком большой (предел 262144 МБ).",
                                         "Maximum size is too large (the limit is 262144 MB)."), "DeepTools");
                    return;
                }
                spec = initial.ToString(CultureInfo.InvariantCulture) + " " + max.ToString(CultureInfo.InvariantCulture);
            }

            busy = true;
            applyBtn.Enabled = false;
            string error;
            bool ok = GameTweaksPanel.ApplyPagefileSpec(spec, out error);
            busy = false;
            applyBtn.Enabled = true;

            if (!ok)
            {
                DTDialog.Show(error, "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DTDialog.Show(Lang.T("Размер файла подкачки сохранён. Он применится после перезагрузки Windows.",
                                 "Page file size saved. It will take effect after a Windows restart."),
                          "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
