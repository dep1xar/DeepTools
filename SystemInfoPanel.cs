using System;
using System.Collections.Generic;
using System.Drawing;
using System.Management;
using System.Text;
using System.Windows.Forms;

namespace DeepTools
{
    // Вкладка "Мой ПК": вся спека одной страницей + кнопка "скопировать"
    // для вставки в объявление/другу/на форум. Собирается через WMI один раз в фоне
    public class SystemInfoPanel : Panel
    {
        private class SpecRow
        {
            public string Title;
            public string Value;
        }

        private FlowLayoutPanel list;
        private Label statusLabel;
        private RoundedButton copyBtn;
        private List<SpecRow> rows = new List<SpecRow>();

        public SystemInfoPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            BuildUi();
            LoadSpecs();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("Мой ПК", "My PC"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            copyBtn = new RoundedButton
            {
                Text = Lang.T("Скопировать спеку", "Copy specs"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(576, 20),
                Size = new Size(160, 32),
                Enabled = false
            };
            copyBtn.Click += (s, e) => CopySpecs();
            Controls.Add(copyBtn);

            var card = Theme.MakeCard(this, new Point(24, 64), new Size(712, 506));

            list = new FlowLayoutPanel
            {
                Location = new Point(12, 12),
                Size = new Size(688, 482),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            statusLabel = new Label
            {
                Text = Lang.T("Собираем информацию о железе...", "Collecting hardware info..."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 586),
                AutoSize = true
            };
            Controls.Add(statusLabel);
        }

        private static string Wmi(string cls, string prop, string fallback)
        {
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT " + prop + " FROM " + cls);
                foreach (ManagementObject obj in searcher.Get())
                {
                    string v = Convert.ToString(obj[prop]);
                    if (!string.IsNullOrEmpty(v)) return v.Trim();
                }
            }
            catch { }
            return fallback;
        }

        private void LoadSpecs()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                var result = new List<SpecRow>();
                string na = Lang.T("н/д", "n/a");

                result.Add(new SpecRow { Title = Lang.T("Процессор", "CPU"), Value = Wmi("Win32_Processor", "Name", na) });

                // Видеокарты: может быть несколько (встройка + дискретная)
                try
                {
                    var gpus = new List<string>();
                    var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = Convert.ToString(obj["Name"]);
                        if (string.IsNullOrEmpty(name)) continue;
                        gpus.Add(name.Trim());
                    }
                    result.Add(new SpecRow { Title = Lang.T("Видеокарта", "GPU"), Value = gpus.Count > 0 ? string.Join("  +  ", gpus.ToArray()) : na });
                }
                catch
                {
                    result.Add(new SpecRow { Title = Lang.T("Видеокарта", "GPU"), Value = na });
                }

                // RAM: суммарный объём + планки с частотой
                try
                {
                    double totalGb = 0;
                    var sticks = new List<string>();
                    var searcher = new ManagementObjectSearcher("SELECT Capacity, Speed, Manufacturer FROM Win32_PhysicalMemory");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        double gb = Convert.ToDouble(obj["Capacity"]) / 1024 / 1024 / 1024;
                        totalGb += gb;
                        string mfr = Convert.ToString(obj["Manufacturer"]);
                        sticks.Add(Math.Round(gb) + Lang.T(" ГБ ", " GB ") + Convert.ToString(obj["Speed"]) + Lang.T(" МГц", " MHz")
                            + (string.IsNullOrEmpty(mfr) ? "" : " (" + mfr.Trim() + ")"));
                    }
                    result.Add(new SpecRow
                    {
                        Title = Lang.T("Память", "RAM"),
                        Value = Math.Round(totalGb) + Lang.T(" ГБ:  ", " GB:  ") + string.Join(",  ", sticks.ToArray())
                    });
                }
                catch
                {
                    result.Add(new SpecRow { Title = Lang.T("Память", "RAM"), Value = na });
                }

                result.Add(new SpecRow
                {
                    Title = Lang.T("Материнская плата", "Motherboard"),
                    Value = (Wmi("Win32_BaseBoard", "Manufacturer", "") + " " + Wmi("Win32_BaseBoard", "Product", "")).Trim()
                });

                // Диски
                try
                {
                    var disks = new List<string>();
                    var searcher = new ManagementObjectSearcher("SELECT Model, Size FROM Win32_DiskDrive");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string model = Convert.ToString(obj["Model"]);
                        double gb = Convert.ToDouble(obj["Size"]) / 1024 / 1024 / 1024;
                        disks.Add(model.Trim() + " (" + Math.Round(gb) + Lang.T(" ГБ)", " GB)"));
                    }
                    result.Add(new SpecRow { Title = Lang.T("Диски", "Drives"), Value = string.Join(",  ", disks.ToArray()) });
                }
                catch
                {
                    result.Add(new SpecRow { Title = Lang.T("Диски", "Drives"), Value = na });
                }

                result.Add(new SpecRow
                {
                    Title = Lang.T("Windows", "Windows"),
                    Value = Wmi("Win32_OperatingSystem", "Caption", na) + " (" + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit") + ")"
                });

                result.Add(new SpecRow
                {
                    Title = Lang.T("Монитор(ы)", "Display(s)"),
                    Value = GetDisplays()
                });

                // Время работы / загрузка + тренд длительности загрузок
                try
                {
                    TimeSpan up = BootInfo.Uptime();
                    string upStr = ((int)up.TotalDays > 0 ? (int)up.TotalDays + Lang.T(" дн ", "d ") : "")
                        + up.Hours + Lang.T(" ч ", "h ") + up.Minutes + Lang.T(" мин", "m");
                    string bootVal = Lang.T("аптайм ", "uptime ") + upStr
                        + Lang.T(",  загрузка: ", ",  booted: ") + BootInfo.LastBoot().ToString("dd.MM HH:mm");

                    List<int> hist = BootInfo.RecordAndGetHistory();
                    if (hist.Count > 0)
                    {
                        bootVal += Lang.T(",  старт: ", ",  boot: ") + hist[hist.Count - 1] + Lang.T(" сек", "s");
                        if (hist.Count >= 2)
                            bootVal += " (" + string.Join(", ", hist.ConvertAll(x => x.ToString()).ToArray()) + ")";
                    }
                    result.Add(new SpecRow { Title = Lang.T("Время работы", "Uptime"), Value = bootVal });
                }
                catch { }

                // Здоровье батареи (только на ноутбуках)
                try
                {
                    BatteryHealth.Info bat = BatteryHealth.Get();
                    if (bat != null)
                    {
                        string batVal = Lang.T("износ ", "wear ") + bat.WearPercent + "%  ("
                            + (bat.DesignedmWh / 1000.0).ToString("0.0") + " → "
                            + (bat.FullChargemWh / 1000.0).ToString("0.0") + Lang.T(" Вт·ч", " Wh") + ")";
                        if (bat.CycleCount > 0)
                            batVal += Lang.T(",  циклов: ", ",  cycles: ") + bat.CycleCount;
                        result.Add(new SpecRow { Title = Lang.T("Батарея", "Battery"), Value = batVal });
                    }
                }
                catch { }

                e.Result = result;
            };
            worker.RunWorkerCompleted += (s, e) => {
                if (e.Error != null || e.Result == null)
                {
                    statusLabel.Text = Lang.T("Не удалось собрать информацию", "Failed to collect info");
                    return;
                }

                rows = (List<SpecRow>)e.Result;
                foreach (SpecRow row in rows) AddRow(row);
                copyBtn.Enabled = true;
                statusLabel.Text = Lang.T("Готово", "Done");
                statusLabel.ForeColor = Theme.Accent;
            };
            worker.RunWorkerAsync();
        }

        private static string GetDisplays()
        {
            try
            {
                var displays = new List<string>();
                foreach (Screen screen in Screen.AllScreens)
                {
                    displays.Add(screen.Bounds.Width + "x" + screen.Bounds.Height);
                }
                return string.Join(",  ", displays.ToArray());
            }
            catch
            {
                return Lang.T("н/д", "n/a");
            }
        }

        private void AddRow(SpecRow row)
        {
            var panel = new Panel { Size = new Size(664, 52), BackColor = Color.Transparent, Margin = new Padding(2, 2, 0, 2) };

            var titleLbl = new Label
            {
                Text = row.Title,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(8, 4),
                AutoSize = true
            };
            panel.Controls.Add(titleLbl);

            var valueLbl = new Label
            {
                Text = row.Value,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(8, 22),
                Size = new Size(650, 24),
                AutoEllipsis = true
            };
            panel.Controls.Add(valueLbl);

            list.Controls.Add(panel);
        }

        private void CopySpecs()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (SpecRow row in rows)
                {
                    sb.AppendLine(row.Title + ": " + row.Value);
                }
                Clipboard.SetText(sb.ToString());
                statusLabel.Text = Lang.T("Спека скопирована в буфер обмена ✓", "Specs copied to clipboard ✓");
                statusLabel.ForeColor = Theme.Accent;
            }
            catch
            {
                statusLabel.Text = Lang.T("Не удалось скопировать", "Failed to copy");
                statusLabel.ForeColor = Theme.Warning;
            }
        }
    }
}
