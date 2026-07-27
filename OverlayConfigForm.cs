using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Настройки FPS-оверлея: угол экрана, размер, какие метрики показывать.
    // Изменения сохраняются сразу; если оверлей включён - GameBoosterPanel
    // пересоздаёт его после закрытия этого окна (см. событие SettingsChanged)
    public class OverlayConfigForm : Form
    {
        public event EventHandler SettingsChanged;

        private OverlaySettings s;
        private RoundedButton[] cornerButtons = new RoundedButton[4];
        private RoundedButton[] sizeButtons = new RoundedButton[3];

        private Point dragStart;
        private bool draggingForm = false;

        public OverlayConfigForm()
        {
            s = OverlaySettings.Load();

            Text = "DeepTools Overlay";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(400, 476);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s2, e) => ApplyRoundedRegion();
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
            titleBar.MouseDown += (s2, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s2, e) => {
                if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s2, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("⚙ Настройка оверлея", "⚙ Overlay settings"),
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
            closeBtn.Click += (s2, e) => Close();
            closeBtn.MouseEnter += (s2, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s2, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            // Угол экрана
            var cornerCard = Theme.MakeCard(this, new Point(16, 52), new Size(368, 92));
            var cornerTitle = new Label
            {
                Text = Lang.T("Угол экрана", "Screen corner"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            cornerCard.Controls.Add(cornerTitle);

            string[] cornerLabels = { "↖", "↗", "↙", "↘" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = new RoundedButton
                {
                    Text = cornerLabels[i],
                    Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                    Location = new Point(16 + i * 56, 40),
                    Size = new Size(48, 38)
                };
                b.Click += (s2, e) => { this.s.Corner = idx; this.s.Save(); StyleCorners(); NotifyChanged(); };
                cornerCard.Controls.Add(b);
                cornerButtons[i] = b;
            }

            // Размер
            var sizeTitle = new Label
            {
                Text = Lang.T("Размер:", "Size:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(258, 14),
                AutoSize = true
            };
            cornerCard.Controls.Add(sizeTitle);

            string[] sizeLabels = { "S", "M", "L" };
            for (int i = 0; i < 3; i++)
            {
                int sizeVal = i + 1;
                var b = new RoundedButton
                {
                    Text = sizeLabels[i],
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Location = new Point(252 + i * 36, 40),
                    Size = new Size(32, 38)
                };
                b.Click += (s2, e) => { this.s.SizeIndex = sizeVal; this.s.Save(); StyleSizes(); NotifyChanged(); };
                cornerCard.Controls.Add(b);
                sizeButtons[i] = b;
            }

            StyleCorners();
            StyleSizes();

            // Метрики
            var metricsCard = Theme.MakeCard(this, new Point(16, 156), new Size(368, 272));
            var metricsTitle = new Label
            {
                Text = Lang.T("Что показывать", "What to show"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            metricsCard.Controls.Add(metricsTitle);

            int y = 40;
            MakeMetricRow(metricsCard, "FPS", y, s.ShowFps, v => { s.ShowFps = v; }); y += 28;
            MakeMetricRow(metricsCard, Lang.T("Время кадра (мс)", "Frametime (ms)"), y, s.ShowFrametime, v => { s.ShowFrametime = v; }); y += 28;
            MakeMetricRow(metricsCard, "1% low FPS", y, s.Show1Low, v => { s.Show1Low = v; }); y += 28;
            MakeMetricRow(metricsCard, Lang.T("График фреймтайма", "Frametime graph"), y, s.ShowFtGraph, v => { s.ShowFtGraph = v; }); y += 28;
            MakeMetricRow(metricsCard, Lang.T("Загрузка CPU", "CPU load"), y, s.ShowCpu, v => { s.ShowCpu = v; }); y += 28;
            MakeMetricRow(metricsCard, Lang.T("Загрузка GPU", "GPU load"), y, s.ShowGpuLoad, v => { s.ShowGpuLoad = v; }); y += 28;
            MakeMetricRow(metricsCard, Lang.T("Температура GPU", "GPU temperature"), y, s.ShowGpuTemp, v => { s.ShowGpuTemp = v; }); y += 28;
            MakeMetricRow(metricsCard, Lang.T("Загрузка RAM", "RAM load"), y, s.ShowRam, v => { s.ShowRam = v; });

            var hint = new Label
            {
                Text = Lang.T("Изменения применяются сразу. Хоткей показа оверлея - ",
                              "Changes apply immediately. Overlay hotkey is ")
                       + Hotkeys.Get(NativeMethods.HOTKEY_ID_OVERLAY) + ".",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 440),
                Size = new Size(364, 30)
            };
            Controls.Add(hint);
        }

        private void MakeMetricRow(Panel parent, string title, int y, bool initial, Action<bool> setter)
        {
            var lbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(16, y + 2),
                AutoSize = true
            };
            parent.Controls.Add(lbl);

            var toggle = new ToggleSwitch { Location = new Point(300, y), Checked = initial };
            toggle.CheckedChanged += (s2, e) => { setter(toggle.Checked); this.s.Save(); NotifyChanged(); };
            parent.Controls.Add(toggle);
        }

        private void StyleCorners()
        {
            for (int i = 0; i < 4; i++) StyleChoice(cornerButtons[i], s.Corner == i);
        }

        private void StyleSizes()
        {
            for (int i = 0; i < 3; i++) StyleChoice(sizeButtons[i], s.SizeIndex == i + 1);
        }

        private void StyleChoice(RoundedButton btn, bool selected)
        {
            btn.ButtonColor = selected ? Theme.Accent : Theme.KeyColor;
            btn.HoverColor = selected ? Theme.AccentHover : Theme.KeyHover;
            btn.TextColor = selected ? Theme.BgColor : Theme.TextMain;
            btn.Invalidate();
        }

        private void NotifyChanged()
        {
            if (SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
        }
    }
}
