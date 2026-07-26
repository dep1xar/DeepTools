using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    // Отключение телеметрии Windows: реестр, службы, задачи планировщика.
    // Три пресета (мягко/средне/жёстко) + галочки по отдельности + полный откат.
    // Каждый твик знает, как включить и выключить себя, и честно показывает
    // текущее состояние - никакой магии со скрытыми изменениями
    public class TelemetryForm : Form
    {
        private abstract class Tweak
        {
            public string Name;
            public string Description;
            public int Level; // 1 = мягко, 2 = средне, 3 = жёстко
            public abstract bool IsApplied();
            public abstract bool Apply();   // отключить телеметрию
            public abstract bool Revert();  // вернуть как было
        }

        // Твик = записать значение-политику в реестр. Откат = удалить значение:
        // все наши политики по умолчанию в Windows отсутствуют
        private class RegTweak : Tweak
        {
            public string KeyPath;      // от HKLM
            public string ValueName;
            public int OffValue;        // значение "телеметрия отключена"

            public override bool IsApplied()
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath))
                    {
                        if (key == null) return false;
                        object v = key.GetValue(ValueName);
                        return v is int && (int)v == OffValue;
                    }
                }
                catch { return false; }
            }

            public override bool Apply()
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(KeyPath))
                    {
                        key.SetValue(ValueName, OffValue, RegistryValueKind.DWord);
                    }
                    return true;
                }
                catch { return false; }
            }

            public override bool Revert()
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(KeyPath, true))
                    {
                        if (key != null) key.DeleteValue(ValueName, false);
                    }
                    return true;
                }
                catch { return false; }
            }
        }

        // Твик = отключить службу (откат = вернуть в start=demand)
        private class ServiceTweak : Tweak
        {
            public string ServiceName;

            public override bool IsApplied()
            {
                string output = RunTool("sc.exe", "qc " + ServiceName);
                return output != null && output.IndexOf("DISABLED", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public override bool Apply()
            {
                RunTool("sc.exe", "stop " + ServiceName);
                return RunTool("sc.exe", "config " + ServiceName + " start= disabled") != null;
            }

            public override bool Revert()
            {
                return RunTool("sc.exe", "config " + ServiceName + " start= demand") != null;
            }
        }

        // Твик = отключить задачу планировщика (откат = включить обратно)
        private class TaskTweak : Tweak
        {
            public string TaskPath; // например \Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser

            public override bool IsApplied()
            {
                string output = RunTool("schtasks.exe", "/Query /TN \"" + TaskPath + "\" /FO LIST");
                if (output == null) return true; // задачи нет - считаем отключённой
                return output.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0
                    || output.IndexOf("Отключено", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            public override bool Apply()
            {
                return RunTool("schtasks.exe", "/Change /TN \"" + TaskPath + "\" /Disable") != null;
            }

            public override bool Revert()
            {
                return RunTool("schtasks.exe", "/Change /TN \"" + TaskPath + "\" /Enable") != null;
            }
        }

        private static string RunTool(string exe, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, arguments);
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                if (p.ExitCode != 0) return null;
                return output;
            }
            catch { return null; }
        }

        private List<Tweak> BuildTweaks()
        {
            var tweaks = new List<Tweak>();

            // --- Уровень 1: мягко (то, что Microsoft сама даёт выключить) ---
            tweaks.Add(new RegTweak
            {
                Name = Lang.T("Диагностические данные - минимум", "Diagnostic data - minimum"),
                Description = Lang.T("AllowTelemetry = Security/Basic. Основной регулятор объёма телеметрии", "AllowTelemetry = Security/Basic. The main telemetry volume control"),
                Level = 1,
                KeyPath = "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection",
                ValueName = "AllowTelemetry",
                OffValue = 0
            });
            tweaks.Add(new RegTweak
            {
                Name = Lang.T("Рекламный идентификатор", "Advertising ID"),
                Description = Lang.T("Отключает персональный ID для рекламы в приложениях", "Disables the per-user ID used for ads in apps"),
                Level = 1,
                KeyPath = "SOFTWARE\\Policies\\Microsoft\\Windows\\AdvertisingInfo",
                ValueName = "DisabledByGroupPolicy",
                OffValue = 1
            });
            tweaks.Add(new RegTweak
            {
                Name = Lang.T("Опросы «оцените Windows»", "Windows feedback prompts"),
                Description = Lang.T("Windows перестаёт спрашивать отзывы (Siuf)", "Windows stops asking for feedback (Siuf)"),
                Level = 1,
                KeyPath = "SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection",
                ValueName = "DoNotShowFeedbackNotifications",
                OffValue = 1
            });

            // --- Уровень 2: средне (службы и фоновые сборщики) ---
            tweaks.Add(new ServiceTweak
            {
                Name = Lang.T("Служба DiagTrack", "DiagTrack service"),
                Description = Lang.T("Connected User Experiences and Telemetry - главный отправитель данных", "Connected User Experiences and Telemetry - the main data uploader"),
                Level = 2,
                ServiceName = "DiagTrack"
            });
            tweaks.Add(new ServiceTweak
            {
                Name = Lang.T("Служба dmwappushservice", "dmwappushservice"),
                Description = Lang.T("Маршрутизация WAP push - устаревшая, участвует в сборе данных", "WAP push routing - legacy, participates in data collection"),
                Level = 2,
                ServiceName = "dmwappushservice"
            });
            tweaks.Add(new TaskTweak
            {
                Name = "Microsoft Compatibility Appraiser",
                Description = Lang.T("Фоновая инвентаризация ПО для Microsoft, грузит диск", "Background software inventory for Microsoft, causes disk load"),
                Level = 2,
                TaskPath = "\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser"
            });
            tweaks.Add(new TaskTweak
            {
                Name = "Customer Experience Improvement (Consolidator)",
                Description = Lang.T("Задача CEIP - отправка данных об использовании", "CEIP task - usage data upload"),
                Level = 2,
                TaskPath = "\\Microsoft\\Windows\\Customer Experience Improvement Program\\Consolidator"
            });
            tweaks.Add(new TaskTweak
            {
                Name = "Customer Experience Improvement (UsbCeip)",
                Description = Lang.T("Задача CEIP - данные об USB-устройствах", "CEIP task - USB device data"),
                Level = 2,
                TaskPath = "\\Microsoft\\Windows\\Customer Experience Improvement Program\\UsbCeip"
            });
            tweaks.Add(new TaskTweak
            {
                Name = "ProgramDataUpdater",
                Description = Lang.T("Инвентаризация установленных программ", "Installed program inventory"),
                Level = 2,
                TaskPath = "\\Microsoft\\Windows\\Application Experience\\ProgramDataUpdater"
            });

            // --- Уровень 3: жёстко (функции, которых часть пользователей хватится) ---
            tweaks.Add(new RegTweak
            {
                Name = Lang.T("Индивидуальные подборки и советы", "Tailored experiences"),
                Description = Lang.T("Отключает «персональные» советы и рекламу на основе диагностики", "Disables 'personalized' tips and ads based on diagnostics"),
                Level = 3,
                KeyPath = "SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent",
                ValueName = "DisableTailoredExperiencesWithDiagnosticData",
                OffValue = 1
            });
            tweaks.Add(new RegTweak
            {
                Name = Lang.T("Рекламные «предложения» Windows", "Windows consumer features"),
                Description = Lang.T("Убирает автоустановку рекламных приложений (Candy Crush и т.п.)", "Stops auto-install of promoted apps (Candy Crush etc.)"),
                Level = 3,
                KeyPath = "SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent",
                ValueName = "DisableWindowsConsumerFeatures",
                OffValue = 1
            });
            tweaks.Add(new RegTweak
            {
                Name = Lang.T("История активности (Timeline)", "Activity history (Timeline)"),
                Description = Lang.T("Windows перестаёт собирать историю действий и слать её в облако", "Windows stops collecting activity history and syncing it to the cloud"),
                Level = 3,
                KeyPath = "SOFTWARE\\Policies\\Microsoft\\Windows\\System",
                ValueName = "PublishUserActivities",
                OffValue = 0
            });

            return tweaks;
        }

        private List<Tweak> tweaks;
        private List<CheckBox> checks = new List<CheckBox>();
        private List<Label> stateBadges = new List<Label>();
        private FlowLayoutPanel list;
        private Label statusLabel;
        private RoundedButton applyBtn;
        private RoundedButton revertBtn;
        private bool working;

        private Point dragStart;
        private bool draggingForm = false;

        public TelemetryForm()
        {
            Text = "DeepTools Telemetry";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(600, 604);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            tweaks = BuildTweaks();
            BuildUi();
            Load += (s, e) => { ApplyRoundedRegion(); RefreshStates(); };
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
                Text = Lang.T("🔇 Телеметрия Windows", "🔇 Windows telemetry"),
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
                Text = Lang.T("Твики через официальные политики, службы и задачи планировщика. Всё обратимо кнопкой «Вернуть как было».",
                              "Tweaks use official policies, services and scheduler tasks. Everything is reversible via 'Revert all'."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 44),
                Size = new Size(564, 30)
            };
            Controls.Add(hint);

            // Кнопки пресетов: отмечают галочки нужных уровней
            string[] presetNames = { Lang.T("Мягко", "Light"), Lang.T("Средне", "Medium"), Lang.T("Жёстко", "Aggressive") };
            for (int lvl = 1; lvl <= 3; lvl++)
            {
                int level = lvl;
                var presetBtn = new RoundedButton
                {
                    Text = presetNames[lvl - 1],
                    ButtonColor = Theme.KeyColor,
                    HoverColor = Theme.KeyHover,
                    TextColor = lvl == 3 ? Theme.Warning : Theme.TextMain,
                    Location = new Point(18 + (lvl - 1) * 100, 80),
                    Size = new Size(92, 30)
                };
                presetBtn.Click += (s, e) => {
                    for (int i = 0; i < tweaks.Count; i++)
                        checks[i].Checked = tweaks[i].Level <= level;
                };
                Controls.Add(presetBtn);
            }

            var presetHint = new Label
            {
                Text = Lang.T("- пресеты отмечают галочки", "- presets tick the boxes"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(322, 88),
                AutoSize = true
            };
            Controls.Add(presetHint);

            var card = Theme.MakeCard(this, new Point(16, 118), new Size(568, 380));

            list = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(548, 360),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);

            string[] levelTag = { "", Lang.T("мягко", "light"), Lang.T("средне", "medium"), Lang.T("жёстко", "aggressive") };
            for (int i = 0; i < tweaks.Count; i++)
            {
                Tweak t = tweaks[i];
                var row = new Panel { Size = new Size(524, 44), BackColor = Color.Transparent, Margin = new Padding(2, 1, 0, 1) };

                var check = new CheckBox
                {
                    Text = t.Name + "  ·  " + levelTag[t.Level],
                    ForeColor = t.Level == 3 ? Theme.Warning : Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Location = new Point(4, 2),
                    AutoSize = true,
                    Checked = false
                };
                row.Controls.Add(check);
                checks.Add(check);

                var descLbl = new Label
                {
                    Text = t.Description,
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 7.5F),
                    Location = new Point(22, 24),
                    Size = new Size(420, 16),
                    AutoEllipsis = true
                };
                row.Controls.Add(descLbl);

                var badge = new Label
                {
                    Text = "...",
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(444, 12),
                    Size = new Size(76, 18),
                    TextAlign = ContentAlignment.MiddleRight
                };
                row.Controls.Add(badge);
                stateBadges.Add(badge);

                list.Controls.Add(row);
            }

            applyBtn = new RoundedButton
            {
                Text = Lang.T("Отключить выбранное", "Disable selected"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(16, 510),
                Size = new Size(180, 38)
            };
            applyBtn.Click += (s, e) => ApplySelected(true);
            Controls.Add(applyBtn);

            revertBtn = new RoundedButton
            {
                Text = Lang.T("Вернуть как было", "Revert all"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(206, 510),
                Size = new Size(150, 38)
            };
            revertBtn.Click += (s, e) => ApplySelected(false);
            Controls.Add(revertBtn);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 556),
                Size = new Size(568, 34)
            };
            Controls.Add(statusLabel);

            if (!Program.IsRunningAsAdmin())
            {
                statusLabel.Text = Lang.T("⚠ Нужны права администратора - без них твики не применятся", "⚠ Administrator rights required - tweaks will not apply without them");
                statusLabel.ForeColor = Theme.Warning;
                applyBtn.Enabled = false;
                revertBtn.Enabled = false;
            }
        }

        // Читает текущее состояние всех твиков в фоне и красит бейджи
        private void RefreshStates()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                var states = new bool[tweaks.Count];
                for (int i = 0; i < tweaks.Count; i++) states[i] = tweaks[i].IsApplied();
                e.Result = states;
            };
            worker.RunWorkerCompleted += (s, e) => {
                var states = (bool[])e.Result;
                int applied = 0;
                for (int i = 0; i < states.Length; i++)
                {
                    stateBadges[i].Text = states[i] ? Lang.T("отключено ✓", "disabled ✓") : Lang.T("активно", "active");
                    stateBadges[i].ForeColor = states[i] ? Theme.Accent : Theme.TextDim;
                    checks[i].Checked = states[i];
                    if (states[i]) applied++;
                }
                if (Program.IsRunningAsAdmin())
                    statusLabel.Text = Lang.T("Отключено твиков: ", "Tweaks disabled: ") + applied + " / " + tweaks.Count;
            };
            worker.RunWorkerAsync();
        }

        // apply = true: отключить телеметрию по галочкам; false: откатить ВСЕ твики
        private void ApplySelected(bool apply)
        {
            if (working) return;

            var selected = new List<Tweak>();
            if (apply)
            {
                for (int i = 0; i < tweaks.Count; i++)
                    if (checks[i].Checked) selected.Add(tweaks[i]);
                if (selected.Count == 0)
                {
                    statusLabel.Text = Lang.T("Ничего не выбрано", "Nothing selected");
                    return;
                }
            }
            else
            {
                selected.AddRange(tweaks);
            }

            Action run = () => {
                working = true;
                applyBtn.Enabled = false;
                revertBtn.Enabled = false;
                statusLabel.Text = apply ? Lang.T("Применяем...", "Applying...") : Lang.T("Откатываем...", "Reverting...");
                statusLabel.ForeColor = Theme.TextDim;

                var worker = new System.ComponentModel.BackgroundWorker();
                worker.DoWork += (s, e) => {
                    int ok = 0;
                    foreach (Tweak t in selected)
                    {
                        bool success = apply ? t.Apply() : t.Revert();
                        if (success) ok++;
                    }
                    e.Result = ok;
                };
                worker.RunWorkerCompleted += (s, e) => {
                    working = false;
                    applyBtn.Enabled = true;
                    revertBtn.Enabled = true;
                    statusLabel.Text = (apply ? Lang.T("Готово: применено ", "Done: applied ") : Lang.T("Готово: откачено ", "Done: reverted "))
                        + e.Result + " / " + selected.Count;
                    statusLabel.ForeColor = Theme.Accent;
                    RefreshStates();
                };
                worker.RunWorkerAsync();
            };

            // Правим реестр и службы - предлагаем точку восстановления
            if (apply) RestorePoint.OfferBefore(this, run);
            else run();
        }
    }
}
