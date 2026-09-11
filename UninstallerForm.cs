using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    // Деинсталлятор программ: читает список установленного из реестра (Uninstall),
    // запускает штатный деинсталлятор, а после — ищет и удаляет остаточные папки
    // (хвосты) в AppData/ProgramData/Program Files по имени и издателю программы
    public class UninstallerForm : Form
    {
        private class AppEntry
        {
            public string Name;
            public string UninstallCmd;
            public long SizeKb;
            public string Publisher;
            public string InstallLocation;
        }

        private ListBox list;
        private TextBox searchBox;
        private Label statusLabel;
        private List<AppEntry> apps = new List<AppEntry>();
        private List<AppEntry> shown = new List<AppEntry>();

        private Point dragStart;
        private bool draggingForm = false;

        public UninstallerForm()
        {
            Text = "DeepTools Uninstaller";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(560, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => { ApplyRoundedRegion(); LoadAppsAsync(); };
            // Когда пользователь возвращается в окно после штатного деинсталлятора,
            // перечитываем список - удалённая программа пропадёт
            Activated += (s, e) => { if (loadedOnce) LoadAppsAsync(); };
        }

        private bool loadedOnce = false;

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
                Text = Lang.T("🧩 Деинсталлятор программ", "🧩 Program uninstaller"),
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

            var card = Theme.MakeCard(this, new Point(16, 52), new Size(528, 410));

            searchBox = new TextBox
            {
                Location = new Point(12, 12),
                Size = new Size(504, 26),
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F),
                Text = Lang.T("Поиск программы...", "Search program...")
            };
            bool placeholder = true;
            searchBox.GotFocus += (s, e) => { if (placeholder) { searchBox.Text = ""; placeholder = false; searchBox.ForeColor = Theme.TextMain; } };
            searchBox.LostFocus += (s, e) => {
                if (searchBox.Text.Length == 0)
                {
                    placeholder = true;
                    searchBox.ForeColor = Theme.TextDim;
                    searchBox.Text = Lang.T("Поиск программы...", "Search program...");
                }
            };
            searchBox.ForeColor = Theme.TextDim;
            searchBox.TextChanged += (s, e) => { if (!placeholder) ApplyFilter(); };
            card.Controls.Add(searchBox);

            list = new ListBox
            {
                Location = new Point(12, 46),
                Size = new Size(504, 352),
                BackColor = Theme.SidebarColor,
                ForeColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9.5F),
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.One,
                IntegralHeight = false
            };
            card.Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            var uninstallBtn = new RoundedButton
            {
                Text = Lang.T("Удалить", "Uninstall"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 474),
                Size = new Size(160, 36)
            };
            uninstallBtn.Click += (s, e) => UninstallSelected();
            Controls.Add(uninstallBtn);

            var leftoverBtn = new RoundedButton
            {
                Text = Lang.T("Почистить хвосты", "Clean leftovers"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(186, 474),
                Size = new Size(180, 36)
            };
            leftoverBtn.Click += (s, e) => CleanLeftoversSelected();
            Controls.Add(leftoverBtn);

            statusLabel = new Label
            {
                Text = Lang.T("Читаем список программ...", "Reading program list..."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(376, 484),
                Size = new Size(168, 30)
            };
            Controls.Add(statusLabel);
        }

        private void LoadApps()
        {
            apps.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectFrom(apps, Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", seen);
            CollectFrom(apps, Registry.LocalMachine, "SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall", seen);
            CollectFrom(apps, Registry.CurrentUser, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", seen);

            apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            ApplyFilter();
            loadedOnce = true;
        }

        private bool loading = false;

        // Реестр Uninstall читаем в фоне: на машинах с сотней программ синхронное
        // чтение вешало окно («не отвечает»), особенно при повторном Activated
        private void LoadAppsAsync()
        {
            if (loading) return;
            loading = true;
            statusLabel.Text = Lang.T("Читаем список программ...", "Reading program list...");

            var w = new System.ComponentModel.BackgroundWorker();
            w.DoWork += (s, e) => e.Result = CollectApps();
            w.RunWorkerCompleted += (s, e) => {
                loading = false;
                if (IsDisposed) return;
                if (e.Error != null || e.Result == null) { statusLabel.Text = Lang.T("Не удалось прочитать список", "Failed to read the list"); return; }
                apps = (List<AppEntry>)e.Result;
                ApplyFilter();
                loadedOnce = true;
            };
            w.RunWorkerAsync();
        }

        // Сбор списка установленных программ (безопасно вызывать из фонового потока)
        private static List<AppEntry> CollectApps()
        {
            var result = new List<AppEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectFrom(result, Registry.LocalMachine, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", seen);
            CollectFrom(result, Registry.LocalMachine, "SOFTWARE\\Wow6432Node\\Microsoft\\Windows\\CurrentVersion\\Uninstall", seen);
            CollectFrom(result, Registry.CurrentUser, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall", seen);

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        // Фильтр по строке поиска: перерисовываем список и держим shown в синхроне
        private void ApplyFilter()
        {
            string q = searchBox == null ? "" : searchBox.Text.Trim();
            if (q == Lang.T("Поиск программы...", "Search program...")) q = "";

            shown.Clear();
            foreach (AppEntry a in apps)
            {
                if (q.Length > 0 && a.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown.Add(a);
            }

            list.BeginUpdate();
            list.Items.Clear();
            foreach (AppEntry a in shown)
            {
                string size = a.SizeKb > 0 ? "  —  " + FormatSize(a.SizeKb) : "";
                list.Items.Add(a.Name + size);
            }
            list.EndUpdate();

            statusLabel.Text = q.Length > 0
                ? Lang.T("Найдено: ", "Found: ") + shown.Count + " / " + apps.Count
                : Lang.T("Программ: ", "Programs: ") + apps.Count;
        }

        private static void CollectFrom(List<AppEntry> into, RegistryKey hive, string subkey, HashSet<string> seen)
        {
            try
            {
                using (RegistryKey key = hive.OpenSubKey(subkey))
                {
                    if (key == null) return;
                    foreach (string sub in key.GetSubKeyNames())
                    {
                        try
                        {
                            using (RegistryKey app = key.OpenSubKey(sub))
                            {
                                if (app == null) continue;
                                string name = app.GetValue("DisplayName") as string;
                                string uninstall = (app.GetValue("QuietUninstallString") as string)
                                    ?? (app.GetValue("UninstallString") as string);
                                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uninstall)) continue;

                                object sysComp = app.GetValue("SystemComponent");
                                if (sysComp != null && Convert.ToInt32(sysComp) == 1) continue;
                                if (app.GetValue("ParentKeyName") != null) continue; // обновления/патчи

                                if (seen.Contains(name)) continue;
                                seen.Add(name);

                                long sizeKb = 0;
                                try { object es = app.GetValue("EstimatedSize"); if (es != null) sizeKb = Convert.ToInt64(es); }
                                catch { }

                                into.Add(new AppEntry
                                {
                                    Name = name,
                                    UninstallCmd = uninstall,
                                    SizeKb = sizeKb,
                                    Publisher = app.GetValue("Publisher") as string ?? "",
                                    InstallLocation = app.GetValue("InstallLocation") as string ?? ""
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private AppEntry Selected()
        {
            int idx = list.SelectedIndex;
            if (idx < 0 || idx >= shown.Count) return null;
            return shown[idx];
        }

        private void UninstallSelected()
        {
            AppEntry a = Selected();
            if (a == null) { statusLabel.Text = Lang.T("Выбери программу", "Select a program"); return; }

            if (DTDialog.Show(
                Lang.T("Запустить удаление «", "Start uninstalling \"") + a.Name + Lang.T("»?\n\nОткроется штатный деинсталлятор. После него можно нажать «Почистить хвосты».",
                    "\"?\n\nThe program's own uninstaller will open. After it finishes, use \"Clean leftovers\"."),
                "DeepTools", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + a.UninstallCmd) { UseShellExecute = false, CreateNoWindow = true };
                Process.Start(psi);
                statusLabel.Text = Lang.T("Деинсталлятор запущен", "Uninstaller launched");
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Ошибка: ", "Error: ") + ex.Message;
            }
        }

        private void CleanLeftoversSelected()
        {
            AppEntry a = Selected();
            if (a == null) { statusLabel.Text = Lang.T("Выбери программу", "Select a program"); return; }

            List<string> found = FindLeftovers(a);
            if (found.Count == 0)
            {
                DTDialog.Show(Lang.T("Остаточных папок не найдено", "No leftover folders found"), "DeepTools");
                return;
            }

            using (var f = new LeftoverForm(a.Name, found))
            {
                f.ShowDialog(this);
            }
        }

        // Ищем папки-хвосты по токену имени и издателю в типовых местах
        private List<string> FindLeftovers(AppEntry a)
        {
            var result = new List<string>();
            var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string token = NameToken(a.Name);
            string publisher = (a.Publisher ?? "").Trim();

            string[] roots = {
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(root); }
                catch { continue; }

                foreach (string dir in dirs)
                {
                    string dn = Path.GetFileName(dir);
                    bool match = (token.Length >= 3 && dn.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (publisher.Length >= 3 && dn.IndexOf(publisher, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (match && added.Add(dir)) result.Add(dir);
                }
            }

            if (!string.IsNullOrEmpty(a.InstallLocation) && Directory.Exists(a.InstallLocation) && added.Add(a.InstallLocation))
                result.Add(a.InstallLocation);

            return result;
        }

        // Первое значимое слово имени без версий/мусора
        private static string NameToken(string name)
        {
            string[] parts = name.Split(new[] { ' ', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                bool hasDigitOnly = true;
                foreach (char c in p) if (!char.IsDigit(c) && c != '.') { hasDigitOnly = false; break; }
                if (!hasDigitOnly && p.Length >= 3) return p;
            }
            return parts.Length > 0 ? parts[0] : name;
        }

        private static string FormatSize(long kb)
        {
            double mb = kb / 1024.0;
            if (mb >= 1024) return (mb / 1024).ToString("0.#") + Lang.T(" ГБ", " GB");
            return mb.ToString("0") + Lang.T(" МБ", " MB");
        }
    }

    // Список найденных хвостов с галочками и удалением
    public class LeftoverForm : Form
    {
        private CheckedListBox clb;
        private List<string> paths;
        private Point dragStart;
        private bool draggingForm = false;

        public LeftoverForm(string appName, List<string> found)
        {
            paths = found;

            Text = "DeepTools Leftovers";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(560, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            var titleBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.BgColor };
            titleBar.MouseDown += (s, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) => { if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y); };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("Хвосты: ", "Leftovers: ") + appName,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Location = new Point(18, 10),
                AutoSize = true
            };
            titleBar.Controls.Add(titleLbl);

            var hint = new Label
            {
                Text = Lang.T("Проверь список! Отметь только то, что точно можно удалить.",
                              "Check the list! Tick only what is safe to delete."),
                ForeColor = Theme.Warning,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 46),
                Size = new Size(524, 18)
            };
            Controls.Add(hint);

            clb = new CheckedListBox
            {
                Location = new Point(18, 72),
                Size = new Size(524, 268),
                BackColor = Theme.SidebarColor,
                ForeColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F),
                BorderStyle = BorderStyle.None,
                CheckOnClick = true
            };
            foreach (string p in paths) clb.Items.Add(p, false);
            Controls.Add(clb);
            NativeMethods.ApplyDarkScrollbar(clb);

            var delBtn = new RoundedButton
            {
                Text = Lang.T("Удалить отмеченное", "Delete checked"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(18, 352),
                Size = new Size(200, 38)
            };
            delBtn.Click += (s, e) => DeleteChecked();
            Controls.Add(delBtn);

            var closeBtn = new RoundedButton
            {
                Text = Lang.T("Закрыть", "Close"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(230, 352),
                Size = new Size(140, 38)
            };
            closeBtn.Click += (s, e) => Close();
            Controls.Add(closeBtn);
        }

        private void DeleteChecked()
        {
            int deleted = 0;
            var toRemove = new List<int>();
            for (int i = 0; i < clb.Items.Count; i++)
            {
                if (!clb.GetItemChecked(i)) continue;
                string path = clb.Items[i].ToString();
                try { if (Directory.Exists(path)) { Directory.Delete(path, true); deleted++; toRemove.Add(i); } }
                catch { }
            }
            for (int i = toRemove.Count - 1; i >= 0; i--) clb.Items.RemoveAt(toRemove[i]);
            DTDialog.Show(Lang.T("Удалено папок: ", "Folders deleted: ") + deleted, "DeepTools");
        }
    }
}
