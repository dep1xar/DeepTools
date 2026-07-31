using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DeepTools
{
    // Разбор синих экранов: события BugCheck (1001) из системного журнала за 90 дней
    // + расшифровка кода ошибки по-человечески. То, что обычно смотрят через BlueScreenView
    public class BsodForm : Form
    {
        private class BsodEntry
        {
            public DateTime Time;
            public string Code;
        }

        private FlowLayoutPanel list;
        private Label summaryLabel;

        private Point dragStart;
        private bool draggingForm = false;

        public BsodForm()
        {
            Text = "DeepTools BSOD";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(560, 440);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => { ApplyRoundedRegion(); LoadCrashes(); };
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
                Text = Lang.T("💀 Синие экраны", "💀 Blue screens"),
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

            summaryLabel = new Label
            {
                Text = Lang.T("Читаем журнал событий...", "Reading event log..."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(18, 48),
                Size = new Size(520, 20)
            };
            Controls.Add(summaryLabel);

            var card = Theme.MakeCard(this, new Point(16, 76), new Size(528, 348));

            list = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(508, 328),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);
        }

        // Что означает код и куда копать. Топ реально встречающихся кодов
        private static string ExplainCode(string code)
        {
            switch (code)
            {
                case "0x0000000a": return Lang.T("IRQL_NOT_LESS_OR_EQUAL - кривой драйвер или проблемы с RAM", "IRQL_NOT_LESS_OR_EQUAL - faulty driver or RAM issues");
                case "0x0000001e": return Lang.T("KMODE_EXCEPTION_NOT_HANDLED - чаще всего драйвер", "KMODE_EXCEPTION_NOT_HANDLED - usually a driver");
                case "0x0000003b": return Lang.T("SYSTEM_SERVICE_EXCEPTION - драйвер или повреждённые системные файлы", "SYSTEM_SERVICE_EXCEPTION - driver or corrupted system files");
                case "0x00000050": return Lang.T("PAGE_FAULT_IN_NONPAGED_AREA - RAM, драйвер или антивирус", "PAGE_FAULT_IN_NONPAGED_AREA - RAM, driver or antivirus");
                case "0x0000007e": return Lang.T("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED - драйвер", "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED - a driver");
                case "0x0000009f": return Lang.T("DRIVER_POWER_STATE_FAILURE - драйвер сломался при сне/пробуждении", "DRIVER_POWER_STATE_FAILURE - driver failed during sleep/wake");
                case "0x000000d1": return Lang.T("DRIVER_IRQL_NOT_LESS_OR_EQUAL - драйвер (часто сетевой)", "DRIVER_IRQL_NOT_LESS_OR_EQUAL - a driver (often network)");
                case "0x000000ef": return Lang.T("CRITICAL_PROCESS_DIED - умер системный процесс, проверь диск и sfc /scannow", "CRITICAL_PROCESS_DIED - a system process died, check disk and run sfc /scannow");
                case "0x00000116": return Lang.T("VIDEO_TDR_FAILURE - видеодрайвер завис, переустанови драйвер GPU", "VIDEO_TDR_FAILURE - GPU driver hang, reinstall GPU driver");
                case "0x00000124": return Lang.T("WHEA_UNCORRECTABLE_ERROR - железо: перегрев, разгон или питание", "WHEA_UNCORRECTABLE_ERROR - hardware: overheating, overclock or power");
                case "0x00000133": return Lang.T("DPC_WATCHDOG_VIOLATION - драйвер (часто SSD, обнови прошивку)", "DPC_WATCHDOG_VIOLATION - a driver (often SSD, update firmware)");
                case "0x00000139": return Lang.T("KERNEL_SECURITY_CHECK_FAILURE - RAM или драйвер", "KERNEL_SECURITY_CHECK_FAILURE - RAM or a driver");
                case "0x0000007a": return Lang.T("KERNEL_DATA_INPAGE_ERROR - проблемы с диском или кабелем SATA", "KERNEL_DATA_INPAGE_ERROR - disk or SATA cable problems");
                case "0x000000f2": return Lang.T("HARDWARE_INTERRUPT_STORM - железо или драйвер устройства", "HARDWARE_INTERRUPT_STORM - hardware or a device driver");
                case "0x0000001a": return Lang.T("MEMORY_MANAGEMENT - проверь RAM (Windows Memory Diagnostic)", "MEMORY_MANAGEMENT - check RAM (Windows Memory Diagnostic)");
                default: return Lang.T("Редкий код - загугли его для деталей", "Rare code - google it for details");
            }
        }

        private void LoadCrashes()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                var entries = new List<BsodEntry>();
                try
                {
                    // BugCheck 1001 пишется в System после каждого синего экрана
                    string query = "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting']" +
                        " and (EventID=1001)" +
                        " and TimeCreated[timediff(@SystemTime) <= 7776000000]]]";
                    var elQuery = new EventLogQuery("System", PathType.LogName, query);
                    elQuery.ReverseDirection = true;
                    using (var reader = new EventLogReader(elQuery))
                    {
                        EventRecord rec;
                        while ((rec = reader.ReadEvent()) != null)
                        {
                            using (rec)
                            {
                                Match m = Regex.Match(rec.ToXml(), "0x[0-9a-fA-F]{8}");
                                var entry = new BsodEntry();
                                entry.Time = rec.TimeCreated.HasValue ? rec.TimeCreated.Value : DateTime.MinValue;
                                entry.Code = m.Success ? m.Value.ToLowerInvariant() : "";
                                entries.Add(entry);
                                if (entries.Count >= 20) break;
                            }
                        }
                    }
                }
                catch { }

                int dumpCount = 0;
                try
                {
                    string dumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Minidump");
                    if (Directory.Exists(dumpDir)) dumpCount = Directory.GetFiles(dumpDir, "*.dmp").Length;
                }
                catch { }

                e.Result = new object[] { entries, dumpCount };
            };
            worker.RunWorkerCompleted += (s, e) => {
                if (e.Error != null || e.Result == null)
                {
                    summaryLabel.Text = Lang.T("Не удалось прочитать журнал", "Failed to read the event log");
                    return;
                }

                object[] r = (object[])e.Result;
                var entries = (List<BsodEntry>)r[0];
                int dumpCount = (int)r[1];

                if (entries.Count == 0)
                {
                    summaryLabel.Text = Lang.T("Синих экранов за 90 дней не найдено — система стабильна ✓", "No blue screens in 90 days — system is stable ✓");
                    summaryLabel.ForeColor = Theme.Accent;
                    return;
                }

                summaryLabel.Text = Lang.T("Синих экранов за 90 дней: ", "Blue screens in 90 days: ") + entries.Count
                    + Lang.T("  (минидампов на диске: ", "  (minidumps on disk: ") + dumpCount + ")";
                summaryLabel.ForeColor = entries.Count >= 3 ? Theme.Danger : Theme.Warning;

                foreach (BsodEntry entry in entries)
                {
                    var row = new Panel { Size = new Size(484, 44), BackColor = Color.Transparent, Margin = new Padding(2, 2, 0, 2) };

                    var dateLbl = new Label
                    {
                        Text = entry.Time.ToString("dd.MM.yyyy HH:mm"),
                        ForeColor = Theme.TextMain,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                        Location = new Point(6, 4),
                        AutoSize = true
                    };
                    row.Controls.Add(dateLbl);

                    var codeLbl = new Label
                    {
                        Text = entry.Code,
                        ForeColor = Theme.Danger,
                        BackColor = Color.Transparent,
                        Font = new Font("Consolas", 9F, FontStyle.Bold),
                        Location = new Point(140, 4),
                        AutoSize = true
                    };
                    row.Controls.Add(codeLbl);

                    var explainLbl = new Label
                    {
                        Text = entry.Code == "" ? Lang.T("Код не распознан", "Code not recognized") : ExplainCode(entry.Code),
                        ForeColor = Theme.TextDim,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 8F),
                        Location = new Point(6, 24),
                        Size = new Size(470, 16),
                        AutoEllipsis = true
                    };
                    row.Controls.Add(explainLbl);

                    list.Controls.Add(row);
                }
            };
            worker.RunWorkerAsync();
        }
    }
}
