using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    public class StartupItem
    {
        public string Name;
        public string Command;
        public string SourceLabel;
        public bool IsRegistry;
        public bool IsLocalMachine;
        public string Key;
        public bool Enabled;
    }

    public class StartupPanel : Panel
    {
        private FlowLayoutPanel list;
        private Label statusLabel;
        private Label bootTimeLabel;
        private Label bootAppsLabel;

        public StartupPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            BuildUi();
            RefreshList();
            LoadBootInfo();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("Автозагрузка", "Startup"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var hintLabel = new Label
            {
                Text = Lang.T("Тумблер отключает запуск при старте Windows, но не удаляет программу. Работает так же, как вкладка \"Автозагрузка\" в диспетчере задач.", "The toggle disables launch at Windows startup without removing the app. Works like the Startup tab in Task Manager."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 50),
                Size = new Size(690, 30)
            };
            Controls.Add(hintLabel);

            var refreshBtn = new RoundedButton
            {
                Text = Lang.T("Обновить список", "Refresh list"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(590, 16),
                Size = new Size(146, 32)
            };
            refreshBtn.Click += (s, e) => RefreshList();
            Controls.Add(refreshBtn);

            // Карточка со временем загрузки Windows и виновниками медленного старта
            var bootCard = Theme.MakeCard(this, new Point(24, 90), new Size(712, 96));

            var bootTitle = new Label
            {
                Text = Lang.T("Загрузка Windows", "Windows boot"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            bootCard.Controls.Add(bootTitle);

            bootTimeLabel = new Label
            {
                Text = Lang.T("Читаем журнал событий...", "Reading event log..."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(16, 34),
                AutoSize = true
            };
            bootCard.Controls.Add(bootTimeLabel);

            bootAppsLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 64),
                Size = new Size(680, 24),
                AutoEllipsis = true
            };
            bootCard.Controls.Add(bootAppsLabel);

            // Страж автозагрузки: следит за новыми записями и предупреждает из трея
            var guardLabel = new Label
            {
                Text = Lang.T("Страж: алерт при новой записи", "Guard: alert on new entries"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(452, 14),
                AutoSize = true
            };
            bootCard.Controls.Add(guardLabel);

            var guardToggle = new ToggleSwitch { Location = new Point(650, 10), Checked = StartupGuard.Enabled };
            guardToggle.CheckedChanged += (s, e) => { StartupGuard.Enabled = guardToggle.Checked; };
            bootCard.Controls.Add(guardToggle);

            var card = Theme.MakeCard(this, new Point(24, 196), new Size(712, 374));

            list = new FlowLayoutPanel
            {
                Location = new Point(12, 12),
                Size = new Size(688, 350),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 586),
                AutoSize = true
            };
            Controls.Add(statusLabel);
        }

        // Время последней загрузки Windows и топ программ, замедливших старт.
        // Данные из журнала Microsoft-Windows-Diagnostics-Performance: событие 100 - итог загрузки,
        // события 101-110 - конкретные виновники замедления. Читается в фоне, т.к. журнал бывает большим
        private void LoadBootInfo()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                double bootSec = -1;
                DateTime bootStamp = DateTime.MinValue;
                var culprits = new List<KeyValuePair<string, double>>();

                try
                {
                    string log = "Microsoft-Windows-Diagnostics-Performance/Operational";

                    var q = new EventLogQuery(log, PathType.LogName, "*[System[(EventID=100)]]");
                    q.ReverseDirection = true;
                    using (var reader = new EventLogReader(q))
                    {
                        EventRecord rec = reader.ReadEvent();
                        if (rec != null)
                        {
                            using (rec)
                            {
                                Match m = Regex.Match(rec.ToXml(), "<Data Name=\"BootTime\">(\\d+)</Data>");
                                if (m.Success) bootSec = double.Parse(m.Groups[1].Value) / 1000.0;
                                if (rec.TimeCreated.HasValue) bootStamp = rec.TimeCreated.Value;
                            }
                        }
                    }

                    // Виновники ищутся в окне получаса вокруг события загрузки
                    if (bootStamp != DateTime.MinValue)
                    {
                        var q2 = new EventLogQuery(log, PathType.LogName, "*[System[(EventID>=101) and (EventID<=110)]]");
                        q2.ReverseDirection = true;
                        using (var reader2 = new EventLogReader(q2))
                        {
                            EventRecord rec;
                            int scanned = 0;
                            while ((rec = reader2.ReadEvent()) != null && scanned < 300)
                            {
                                using (rec)
                                {
                                    scanned++;
                                    if (!rec.TimeCreated.HasValue) continue;
                                    if (rec.TimeCreated.Value < bootStamp.AddMinutes(-30)) break;
                                    if (rec.TimeCreated.Value > bootStamp.AddMinutes(30)) continue;

                                    string xml = rec.ToXml();
                                    Match nameM = Regex.Match(xml, "<Data Name=\"(?:FriendlyName|Name)\">([^<]+)</Data>");
                                    Match degM = Regex.Match(xml, "<Data Name=\"DegradationTime\">(\\d+)</Data>");
                                    if (nameM.Success && degM.Success)
                                    {
                                        double degSec = double.Parse(degM.Groups[1].Value) / 1000.0;
                                        if (degSec >= 0.5) culprits.Add(new KeyValuePair<string, double>(nameM.Groups[1].Value, degSec));
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // журнал выключен или пуст - покажем "н/д"
                }

                culprits.Sort((a, b) => b.Value.CompareTo(a.Value));
                e.Result = new object[] { bootSec, culprits };
            };
            worker.RunWorkerCompleted += (s, e) => {
                if (e.Error != null || e.Result == null) return;
                object[] r = (object[])e.Result;
                double bootSec = (double)r[0];
                var culprits = (List<KeyValuePair<string, double>>)r[1];

                if (bootSec <= 0)
                {
                    bootTimeLabel.Text = Lang.T("н/д (журнал загрузки пуст)", "n/a (boot log is empty)");
                    bootTimeLabel.ForeColor = Theme.TextDim;
                    return;
                }

                bootTimeLabel.Text = Lang.T("Последняя загрузка: ", "Last boot: ") + bootSec.ToString("0.#") + Lang.T(" сек", " s");
                bootTimeLabel.ForeColor = bootSec < 40 ? Theme.Accent : (bootSec < 80 ? Theme.Warning : Theme.Danger);

                if (culprits.Count > 0)
                {
                    var parts = new List<string>();
                    for (int i = 0; i < Math.Min(3, culprits.Count); i++)
                    {
                        parts.Add(culprits[i].Key + " +" + culprits[i].Value.ToString("0.#") + Lang.T(" с", " s"));
                    }
                    bootAppsLabel.Text = Lang.T("Замедлили запуск: ", "Slowed down boot: ") + string.Join(",  ", parts.ToArray());
                }
                else
                {
                    bootAppsLabel.Text = Lang.T("Явных виновников замедления не найдено", "No obvious boot slowdown culprits found");
                }
            };
            worker.RunWorkerAsync();
        }

        private void RefreshList()
        {
            list.Controls.Clear();

            List<StartupItem> items = new List<StartupItem>();
            AddRunKeyItems(items, Registry.CurrentUser, false, Lang.T("Пользователь (реестр)", "User (registry)"));
            AddRunKeyItems(items, Registry.LocalMachine, true, Lang.T("Система (реестр)", "System (registry)"));
            AddFolderItems(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Lang.T("Папка автозагрузки", "Startup folder"));
            AddFolderItems(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Lang.T("Папка автозагрузки (все пользователи)", "Startup folder (all users)"));

            if (items.Count == 0)
            {
                var emptyLbl = new Label
                {
                    Text = Lang.T("Программ в автозагрузке не найдено", "No startup apps found"),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(6, 6),
                    AutoSize = true
                };
                list.Controls.Add(emptyLbl);
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                list.Controls.Add(MakeRow(items[i]));
            }
        }

        private void AddRunKeyItems(List<StartupItem> items, RegistryKey root, bool isLocalMachine, string sourceLabel)
        {
            RegistryKey key = null;
            try
            {
                key = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run");
                if (key == null) return;

                string[] names = key.GetValueNames();
                for (int i = 0; i < names.Length; i++)
                {
                    string command = key.GetValue(names[i]) as string;
                    if (command == null) continue;

                    StartupItem item = new StartupItem();
                    item.Name = names[i];
                    item.Command = command;
                    item.SourceLabel = sourceLabel;
                    item.IsRegistry = true;
                    item.IsLocalMachine = isLocalMachine;
                    item.Key = names[i];
                    item.Enabled = IsRunApproved(root, names[i]);
                    items.Add(item);
                }
            }
            catch
            {
            }
            finally
            {
                if (key != null) key.Close();
            }
        }

        private void AddFolderItems(List<StartupItem> items, string folderPath, string sourceLabel)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

            string[] files;
            try { files = Directory.GetFiles(folderPath); }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                string fileName = Path.GetFileName(files[i]);
                if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                StartupItem item = new StartupItem();
                item.Name = Path.GetFileNameWithoutExtension(files[i]);
                item.Command = files[i];
                item.SourceLabel = sourceLabel;
                item.IsRegistry = false;
                item.IsLocalMachine = false;
                item.Key = fileName;
                item.Enabled = IsFolderApproved(fileName);
                items.Add(item);
            }
        }

        private bool IsRunApproved(RegistryKey root, string valueName)
        {
            RegistryKey approved = null;
            try
            {
                approved = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run");
                if (approved == null) return true;
                byte[] bytes = approved.GetValue(valueName) as byte[];
                if (bytes == null || bytes.Length == 0) return true;
                return bytes[0] == 0x02;
            }
            catch
            {
                return true;
            }
            finally
            {
                if (approved != null) approved.Close();
            }
        }

        private bool IsFolderApproved(string fileName)
        {
            RegistryKey approved = null;
            try
            {
                approved = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder");
                if (approved == null) return true;
                byte[] bytes = approved.GetValue(fileName) as byte[];
                if (bytes == null || bytes.Length == 0) return true;
                return bytes[0] == 0x02;
            }
            catch
            {
                return true;
            }
            finally
            {
                if (approved != null) approved.Close();
            }
        }

        private void SetRunEnabled(RegistryKey root, string valueName, bool enabled)
        {
            RegistryKey approved = null;
            try
            {
                approved = root.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run");
                byte[] existing = approved.GetValue(valueName) as byte[];
                byte[] bytes = (existing != null && existing.Length >= 12) ? (byte[])existing.Clone() : new byte[12];
                bytes[0] = enabled ? (byte)0x02 : (byte)0x03;
                approved.SetValue(valueName, bytes, RegistryValueKind.Binary);
                statusLabel.Text = Lang.T("Изменено: ", "Changed: ") + valueName;
                statusLabel.ForeColor = Theme.Accent;
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Не удалось изменить (нужны права администратора?): ", "Failed to change (administrator rights needed?): ") + ex.Message;
                statusLabel.ForeColor = Theme.Warning;
            }
            finally
            {
                if (approved != null) approved.Close();
            }
        }

        private void SetFolderEnabled(string fileName, bool enabled)
        {
            RegistryKey approved = null;
            try
            {
                approved = Registry.CurrentUser.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\StartupFolder");
                byte[] existing = approved.GetValue(fileName) as byte[];
                byte[] bytes = (existing != null && existing.Length >= 12) ? (byte[])existing.Clone() : new byte[12];
                bytes[0] = enabled ? (byte)0x02 : (byte)0x03;
                approved.SetValue(fileName, bytes, RegistryValueKind.Binary);
                statusLabel.Text = Lang.T("Изменено: ", "Changed: ") + fileName;
                statusLabel.ForeColor = Theme.Accent;
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Не удалось изменить: ", "Failed to change: ") + ex.Message;
                statusLabel.ForeColor = Theme.Warning;
            }
            finally
            {
                if (approved != null) approved.Close();
            }
        }

        // ---------- Рейтинг подозрительности ----------
        // Эвристика как у MinerGuard: не приговор, а повод присмотреться.
        // Красный - несколько красных флагов сразу, жёлтый - один

        private static string ExtractExePath(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;
            string c = command.Trim();
            try
            {
                if (c.StartsWith("\""))
                {
                    int end = c.IndexOf('"', 1);
                    if (end > 1) return Environment.ExpandEnvironmentVariables(c.Substring(1, end - 1));
                }
                int exe = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exe > 0) return Environment.ExpandEnvironmentVariables(c.Substring(0, exe + 4));
                int sp = c.IndexOf(' ');
                return Environment.ExpandEnvironmentVariables(sp > 0 ? c.Substring(0, sp) : c);
            }
            catch { return null; }
        }

        // Имя вида "xk9f2mqa" или "svc83729" - типичный автогенерат малвари
        private static bool LooksRandomName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 8) return false;
            int letters = 0, vowels = 0, digits = 0;
            string v = "aeiouyаеёиоуыэюя";
            foreach (char ch in name.ToLowerInvariant())
            {
                if (char.IsDigit(ch)) digits++;
                else if (char.IsLetter(ch))
                {
                    letters++;
                    if (v.IndexOf(ch) >= 0) vowels++;
                }
            }
            if (digits >= 4 && letters >= 4) return true;      // мешанина букв и цифр
            if (letters >= 8 && vowels * 5 < letters) return true; // слово почти без гласных
            return false;
        }

        private int SuspicionScore(StartupItem item, List<string> reasons)
        {
            int score = 0;
            string path = ExtractExePath(item.Command);

            bool exists = false;
            try { exists = path != null && File.Exists(path); } catch { }

            if (path != null)
            {
                string low = path.ToLowerInvariant();
                if (low.Contains("\\temp\\") || low.Contains("\\tmp\\"))
                {
                    score += 2;
                    reasons.Add(Lang.T("запускается из Temp", "runs from Temp"));
                }

                if (!exists)
                {
                    score += 1;
                    reasons.Add(Lang.T("файл не найден", "file not found"));
                }
                else
                {
                    try
                    {
                        System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
                    }
                    catch
                    {
                        score += 1;
                        reasons.Add(Lang.T("нет цифровой подписи", "no digital signature"));
                    }
                }
            }

            if (LooksRandomName(item.Name) ||
                (exists && LooksRandomName(Path.GetFileNameWithoutExtension(path))))
            {
                score += 2;
                reasons.Add(Lang.T("случайное имя", "random-looking name"));
            }

            return score;
        }

        private Panel MakeRow(StartupItem item)
        {
            var row = new Panel { Size = new Size(660, 48), BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2) };

            var nameLbl = new Label
            {
                Text = item.Name,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(8, 4),
                Size = new Size(330, 18),
                AutoEllipsis = true
            };
            row.Controls.Add(nameLbl);

            var reasons = new List<string>();
            int score = SuspicionScore(item, reasons);
            if (score > 0)
            {
                var badge = new Label
                {
                    Text = (score >= 3 ? "●  " + Lang.T("подозрительно", "suspicious")
                                       : "●  " + Lang.T("стоит проверить", "worth checking")),
                    ForeColor = score >= 3 ? Theme.Danger : Theme.Warning,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                    Location = new Point(400, 14),
                    Size = new Size(190, 18),
                    TextAlign = ContentAlignment.MiddleRight
                };
                var tip = new ToolTip();
                tip.SetToolTip(badge, string.Join(", ", reasons.ToArray()));
                row.Controls.Add(badge);
            }

            var sourceLbl = new Label
            {
                Text = item.SourceLabel,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(8, 24),
                AutoSize = true
            };
            row.Controls.Add(sourceLbl);

            var toggle = new ToggleSwitch { Location = new Point(600, 12), Checked = item.Enabled };
            toggle.CheckedChanged += (s, e) => {
                if (item.IsRegistry)
                {
                    RegistryKey root = item.IsLocalMachine ? Registry.LocalMachine : Registry.CurrentUser;
                    SetRunEnabled(root, item.Key, toggle.Checked);
                }
                else
                {
                    SetFolderEnabled(item.Key, toggle.Checked);
                }
            };
            row.Controls.Add(toggle);

            return row;
        }
    }
}