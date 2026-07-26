using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DeepTools
{
    public class ServiceEntry
    {
        public string ServiceName;
        public string DisplayName;
        public string Description;
        public bool Risky;
    }

    public class ServicesPanel : Panel
    {
        private static readonly ServiceEntry[] SafeServices = new ServiceEntry[]
        {
            new ServiceEntry { ServiceName = "XblAuthManager", DisplayName = "Xbox Live Auth Manager", Description = Lang.T("Нужна только для входа в Xbox Live", "Only needed for Xbox Live sign-in"), Risky = false },
            new ServiceEntry { ServiceName = "XblGameSave", DisplayName = "Xbox Live Game Save", Description = Lang.T("Синхронизация сохранений Xbox", "Xbox save game sync"), Risky = false },
            new ServiceEntry { ServiceName = "XboxNetApiSvc", DisplayName = "Xbox Live Networking Service", Description = Lang.T("Сетевые функции Xbox Live", "Xbox Live networking features"), Risky = false },
            new ServiceEntry { ServiceName = "XboxGipSvc", DisplayName = "Xbox Accessory Management", Description = Lang.T("Поддержка аксессуаров Xbox", "Xbox accessory support"), Risky = false },
            new ServiceEntry { ServiceName = "DiagTrack", DisplayName = Lang.T("Служба телеметрии", "Telemetry service"), Description = Lang.T("Отправка данных использования в Microsoft", "Sends usage data to Microsoft"), Risky = false },
            new ServiceEntry { ServiceName = "dmwappushservice", DisplayName = "WAP Push Message Routing", Description = Lang.T("Устаревшая служба push-уведомлений", "Legacy push notification service"), Risky = false },
            new ServiceEntry { ServiceName = "MapsBroker", DisplayName = Lang.T("Загрузчик картографических данных", "Downloaded maps manager"), Description = Lang.T("Автообновление офлайн-карт", "Auto-update of offline maps"), Risky = false },
            new ServiceEntry { ServiceName = "RetailDemo", DisplayName = Lang.T("Демо-режим в магазине", "Retail demo mode"), Description = Lang.T("Нужна только на витринах в магазинах", "Only needed on store display units"), Risky = false },
            new ServiceEntry { ServiceName = "Fax", DisplayName = Lang.T("Служба факсов", "Fax service"), Description = Lang.T("Отправка и приём факсов", "Sending and receiving faxes"), Risky = false },
            new ServiceEntry { ServiceName = "Spooler", DisplayName = Lang.T("Диспетчер печати", "Print Spooler"), Description = Lang.T("Отключай только если не пользуешься принтером", "Disable only if you do not use a printer"), Risky = true },
            new ServiceEntry { ServiceName = "WSearch", DisplayName = "Windows Search", Description = Lang.T("Индексация файлов для быстрого поиска - отключение замедлит поиск", "File indexing for fast search - disabling slows down search"), Risky = true }
        };

        private FlowLayoutPanel list;
        private Label statusLabel;
        private List<ToggleSwitch> toggles = new List<ToggleSwitch>();

        public ServicesPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            BuildUi();
            RefreshList();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("Службы Windows", "Windows Services"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var hintLabel = new Label
            {
                Text = Lang.T("Отключение переводит службу в режим \"Отключена\" (start=disabled), а не удаляет её. В любой момент можно включить обратно.", "Disabling sets the service to Disabled (start=disabled), it does not remove it. You can re-enable it at any time."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 50),
                Size = new Size(560, 30)
            };
            Controls.Add(hintLabel);

            var telemetryBtn = new RoundedButton
            {
                Text = Lang.T("🔇 Телеметрия", "🔇 Telemetry"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(444, 16),
                Size = new Size(134, 34)
            };
            telemetryBtn.Click += (s, e) => {
                using (var f = new TelemetryForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(telemetryBtn);

            var presetBtn = new RoundedButton
            {
                Text = Lang.T("Пресет: Игровой ПК", "Preset: Gaming PC"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(590, 16),
                Size = new Size(146, 34)
            };
            presetBtn.Click += (s, e) => ApplyGamingPreset();
            Controls.Add(presetBtn);

            var card = Theme.MakeCard(this, new Point(24, 90), new Size(712, 480));

            list = new FlowLayoutPanel
            {
                Location = new Point(12, 12),
                Size = new Size(688, 456),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);

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

        private void RefreshList()
        {
            list.Controls.Clear();
            toggles.Clear();

            for (int i = 0; i < SafeServices.Length; i++)
            {
                list.Controls.Add(MakeRow(SafeServices[i]));
            }
        }

        private Panel MakeRow(ServiceEntry entry)
        {
            var row = new Panel { Size = new Size(660, 52), BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2) };

            var nameLbl = new Label
            {
                Text = entry.DisplayName + (entry.Risky ? "  ⚠" : ""),
                ForeColor = entry.Risky ? Theme.Warning : Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(8, 4),
                AutoSize = true
            };
            row.Controls.Add(nameLbl);

            var descLbl = new Label
            {
                Text = entry.Description,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(8, 24),
                Size = new Size(500, 24)
            };
            row.Controls.Add(descLbl);

            var statusBadge = new Label
            {
                Text = "...",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(520, 14),
                AutoSize = true
            };
            row.Controls.Add(statusBadge);

            var toggle = new ToggleSwitch { Location = new Point(600, 12), Checked = false };
            toggles.Add(toggle);

            bool isEnabled;
            string state = QueryServiceStartType(entry.ServiceName, out isEnabled);
            toggle.Checked = isEnabled;
            statusBadge.Text = state;

            toggle.CheckedChanged += (s, e) => {
                bool ok = SetServiceEnabled(entry.ServiceName, toggle.Checked);
                if (ok)
                {
                    string newState;
                    QueryServiceStartType(entry.ServiceName, out isEnabled);
                    newState = isEnabled ? "Включена" : Lang.T("Отключена", "Disabled");
                    statusBadge.Text = newState;
                    statusLabel.Text = entry.DisplayName + ": " + newState;
                    statusLabel.ForeColor = Theme.Accent;
                }
                else
                {
                    statusLabel.Text = Lang.T("Не удалось изменить ", "Failed to change ") + entry.DisplayName + Lang.T(" (нужны права администратора?)", " (administrator rights needed?)");
                    statusLabel.ForeColor = Theme.Warning;
                    toggle.Checked = !toggle.Checked;
                    toggle.Invalidate();
                }
            };
            row.Controls.Add(toggle);

            return row;
        }

        private string QueryServiceStartType(string serviceName, out bool isEnabled)
        {
            isEnabled = true;
            string output = RunSc("qc " + serviceName);
            if (output == null)
            {
                isEnabled = false;
                return Lang.T("не найдена", "not found");
            }

            if (output.IndexOf("DISABLED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isEnabled = false;
                return Lang.T("Отключена", "Disabled");
            }

            isEnabled = true;
            return Lang.T("Включена", "Enabled");
        }

        private bool SetServiceEnabled(string serviceName, bool enabled)
        {
            if (!enabled)
            {
                RunSc("stop " + serviceName);
                string result = RunSc("config " + serviceName + " start= disabled");
                return result != null;
            }
            else
            {
                string result = RunSc("config " + serviceName + " start= demand");
                return result != null;
            }
        }

        private string RunSc(string arguments)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("sc.exe", arguments);
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;

                Process proc = Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                if (proc.ExitCode != 0) return null;
                return output;
            }
            catch
            {
                return null;
            }
        }

        private void ApplyGamingPreset()
        {
            // Пресет трогает 9 служб разом - предлагаем точку восстановления
            RestorePoint.OfferBefore(FindForm(), () => {
                string[] toDisable = new string[]
                {
                    "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc",
                    "DiagTrack", "dmwappushservice", "MapsBroker", "RetailDemo", "Fax"
                };

                int success = 0;
                for (int i = 0; i < toDisable.Length; i++)
                {
                    if (SetServiceEnabled(toDisable[i], false)) success++;
                }

                statusLabel.Text = Lang.T("Пресет применён: отключено служб ", "Preset applied: services disabled ") + success + Lang.T(" из ", " of ") + toDisable.Length;
                statusLabel.ForeColor = Theme.Accent;
                RefreshList();
            });
        }
    }
}