using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Удаление предустановленного мусора Windows (UWP-приложения).
    // Показываем только то, что реально установлено. Удаление честное, через
    // Remove-AppxPackage, только для текущего пользователя и с его подтверждением
    public class DebloatForm : Form
    {
        private class BloatApp
        {
            public string PackageName;
            public string DisplayName;
            public bool DefaultChecked;
            public string Note;

            public BloatApp(string pkg, string display, bool check, string note)
            {
                PackageName = pkg;
                DisplayName = display;
                DefaultChecked = check;
                Note = note;
            }
        }

        private static readonly BloatApp[] KnownBloat = new BloatApp[]
        {
            new BloatApp("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", true, ""),
            new BloatApp("king.com.CandyCrushSaga", "Candy Crush Saga", true, ""),
            new BloatApp("king.com.CandyCrushSodaSaga", "Candy Crush Soda", true, ""),
            new BloatApp("Microsoft.BingNews", Lang.T("Новости", "News"), true, ""),
            new BloatApp("Microsoft.BingWeather", Lang.T("Погода", "Weather"), true, ""),
            new BloatApp("Microsoft.GetHelp", Lang.T("Техподдержка", "Get Help"), true, ""),
            new BloatApp("Microsoft.Getstarted", Lang.T("Советы", "Tips"), true, ""),
            new BloatApp("Microsoft.MicrosoftOfficeHub", "Office Hub", true, ""),
            new BloatApp("Microsoft.Microsoft3DViewer", Lang.T("Просмотр 3D", "3D Viewer"), true, ""),
            new BloatApp("Microsoft.3DBuilder", "3D Builder", true, ""),
            new BloatApp("Microsoft.MixedReality.Portal", Lang.T("Портал смеш. реальности", "Mixed Reality Portal"), true, ""),
            new BloatApp("Microsoft.People", Lang.T("Люди", "People"), true, ""),
            new BloatApp("Microsoft.SkypeApp", "Skype (UWP)", true, ""),
            new BloatApp("Microsoft.WindowsFeedbackHub", Lang.T("Центр отзывов", "Feedback Hub"), true, ""),
            new BloatApp("Microsoft.549981C3F5F10", "Cortana", true, ""),
            new BloatApp("Microsoft.PowerAutomateDesktop", "Power Automate", true, ""),
            new BloatApp("Clipchamp.Clipchamp", "Clipchamp", true, ""),
            new BloatApp("Microsoft.WindowsMaps", Lang.T("Карты", "Maps"), false, ""),
            new BloatApp("Microsoft.Todos", "Microsoft To Do", false, ""),
            new BloatApp("Microsoft.ZuneMusic", Lang.T("Groove Музыка / Media Player", "Groove Music / Media Player"), false, ""),
            new BloatApp("Microsoft.ZuneVideo", Lang.T("Кино и ТВ", "Movies & TV"), false, ""),
            new BloatApp("Microsoft.WindowsAlarms", Lang.T("Будильники и часы", "Alarms & Clock"), false, ""),
            new BloatApp("Microsoft.YourPhone", Lang.T("Связь с телефоном", "Phone Link"), false, Lang.T("нужна для связи с Android", "needed for Android link")),
            new BloatApp("Microsoft.XboxApp", "Xbox (старое приложение)", false, Lang.T("не трогай, если играешь через Game Pass", "keep it if you use Game Pass")),
            new BloatApp("Microsoft.BingSearch", Lang.T("Поиск Bing", "Bing Search"), false, "")
        };

        private FlowLayoutPanel list;
        private Label statusLabel;
        private RoundedButton removeBtn;
        private List<CheckBox> checks = new List<CheckBox>();
        private List<BloatApp> installedApps = new List<BloatApp>();
        private bool working = false;

        private Point dragStart;
        private bool draggingForm = false;

        public DebloatForm()
        {
            Text = "DeepTools Debloat";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(540, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => { ApplyRoundedRegion(); LoadInstalled(); };
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
                Text = Lang.T("🗑 Встроенный мусор Windows", "🗑 Windows bloatware"),
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
            closeBtn.Click += (s, e) => { if (!working) Close(); };
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            var hint = new Label
            {
                Text = Lang.T("Удаляются только магазинные приложения текущего пользователя. Любое можно вернуть из Microsoft Store.",
                              "Only Store apps of the current user are removed. Any of them can be reinstalled from Microsoft Store."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 46),
                Size = new Size(504, 30)
            };
            Controls.Add(hint);

            var card = Theme.MakeCard(this, new Point(16, 82), new Size(508, 360));

            list = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(488, 340),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);

            removeBtn = new RoundedButton
            {
                Text = Lang.T("Удалить выбранное", "Remove selected"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Location = new Point(16, 452),
                Size = new Size(180, 38),
                Enabled = false
            };
            removeBtn.Click += (s, e) => RemoveSelected();
            Controls.Add(removeBtn);

            statusLabel = new Label
            {
                Text = Lang.T("Ищем установленный мусор...", "Scanning for installed bloat..."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(210, 462),
                Size = new Size(314, 30)
            };
            Controls.Add(statusLabel);
        }

        private static string RunPowerShell(string command, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"" + command + "\"");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(timeoutMs);
                return output;
            }
            catch
            {
                return "";
            }
        }

        private void LoadInstalled()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                string output = RunPowerShell("Get-AppxPackage | ForEach-Object { $_.Name }", 30000);
                e.Result = output;
            };
            worker.RunWorkerCompleted += (s, e) => {
                string output = e.Result as string;
                if (string.IsNullOrEmpty(output))
                {
                    statusLabel.Text = Lang.T("Не удалось получить список приложений", "Failed to get the app list");
                    return;
                }

                var installed = new HashSet<string>(
                    output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.OrdinalIgnoreCase);

                foreach (BloatApp app in KnownBloat)
                {
                    if (!installed.Contains(app.PackageName)) continue;
                    installedApps.Add(app);

                    var row = new Panel { Size = new Size(464, 30), BackColor = Color.Transparent, Margin = new Padding(2, 1, 0, 1) };

                    var check = new CheckBox
                    {
                        Text = app.DisplayName + (app.Note == "" ? "" : "  (" + app.Note + ")"),
                        ForeColor = app.DefaultChecked ? Theme.TextMain : Theme.Warning,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 9F),
                        Location = new Point(6, 4),
                        AutoSize = true,
                        Checked = app.DefaultChecked
                    };
                    row.Controls.Add(check);
                    checks.Add(check);

                    list.Controls.Add(row);
                }

                if (installedApps.Count == 0)
                {
                    statusLabel.Text = Lang.T("Мусора не найдено - система уже чистая ✓", "No bloat found - system is already clean ✓");
                    statusLabel.ForeColor = Theme.Accent;
                }
                else
                {
                    statusLabel.Text = Lang.T("Найдено: ", "Found: ") + installedApps.Count
                        + Lang.T(". Жёлтым - то, что может пригодиться", ". Yellow items may be useful");
                    removeBtn.Enabled = true;
                }
            };
            worker.RunWorkerAsync();
        }

        private void RemoveSelected()
        {
            if (working) return;

            var toRemove = new List<BloatApp>();
            for (int i = 0; i < installedApps.Count; i++)
            {
                if (checks[i].Checked) toRemove.Add(installedApps[i]);
            }
            if (toRemove.Count == 0)
            {
                statusLabel.Text = Lang.T("Ничего не выбрано", "Nothing selected");
                return;
            }

            DialogResult confirm = MessageBox.Show(
                Lang.T("Удалить приложений: ", "Apps to remove: ") + toRemove.Count +
                Lang.T("?\n\nИх можно будет вернуть через Microsoft Store.", "?\n\nThey can be reinstalled from Microsoft Store."),
                "DeepTools",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            // Шанс откатиться, если удалили что-то нужное
            RestorePoint.OfferBefore(this, () => DoRemove(toRemove));
        }

        private void DoRemove(List<BloatApp> toRemove)
        {
            working = true;
            removeBtn.Enabled = false;
            removeBtn.Text = Lang.T("Удаляем...", "Removing...");

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += (s, e) => {
                int done = 0;
                foreach (BloatApp app in toRemove)
                {
                    ((System.ComponentModel.BackgroundWorker)s).ReportProgress(0, app.DisplayName);
                    RunPowerShell("Get-AppxPackage -Name '" + app.PackageName + "' | Remove-AppxPackage", 60000);
                    done++;
                }
                e.Result = done;
            };
            worker.ProgressChanged += (s, e) => {
                statusLabel.Text = Lang.T("Удаляем: ", "Removing: ") + (string)e.UserState;
            };
            worker.RunWorkerCompleted += (s, e) => {
                working = false;
                removeBtn.Text = Lang.T("Удалить выбранное", "Remove selected");
                statusLabel.Text = Lang.T("Готово! Удалено приложений: ", "Done! Apps removed: ") + (e.Result ?? 0)
                    + Lang.T(". Перезапусти окно, чтобы обновить список", ". Reopen this window to refresh the list");
                statusLabel.ForeColor = Theme.Accent;
            };
            worker.RunWorkerAsync();
        }
    }
}
