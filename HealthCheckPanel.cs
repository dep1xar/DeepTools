using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    public class LoadGraph : Panel
    {
        private List<float> history = new List<float>();
        public int MaxPoints = 60;
        public Color LineColor = Theme.Accent;

        public LoadGraph()
        {
            BackColor = Theme.SidebarColor;
            DoubleBuffered = true;
        }

        public void AddPoint(float value)
        {
            history.Add(Math.Max(0, Math.Min(100, value)));
            if (history.Count > MaxPoints) history.RemoveAt(0);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle plot = new Rectangle(6, 6, Width - 12, Height - 12);
            if (plot.Width <= 0 || plot.Height <= 0) return;

            using (var gridPen = new Pen(Theme.BorderColor))
            {
                for (int i = 1; i < 4; i++)
                {
                    int y = plot.Bottom - plot.Height * i / 4;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                }
            }

            if (history.Count < 2 || MaxPoints < 2) return;

            float stepX = plot.Width / (float)(MaxPoints - 1);
            PointF[] points = new PointF[history.Count];
            for (int i = 0; i < history.Count; i++)
            {
                float x = plot.Left + i * stepX;
                float y = plot.Bottom - history[i] / 100f * plot.Height;
                points[i] = new PointF(x, y);
            }

            using (GraphicsPath areaPath = new GraphicsPath())
            {
                areaPath.AddLine(points[0].X, plot.Bottom, points[0].X, points[0].Y);
                for (int i = 1; i < points.Length; i++) areaPath.AddLine(points[i - 1], points[i]);
                areaPath.AddLine(points[points.Length - 1].X, points[points.Length - 1].Y, points[points.Length - 1].X, plot.Bottom);
                areaPath.CloseFigure();
                using (var areaBrush = new SolidBrush(Color.FromArgb(40, LineColor)))
                {
                    g.FillPath(areaBrush, areaPath);
                }
            }

            using (var linePen = new Pen(LineColor, 2))
            {
                for (int i = 1; i < points.Length; i++) g.DrawLine(linePen, points[i - 1], points[i]);
            }
        }
    }

    // Чтение температуры GPU через NVML - библиотеку драйвера NVIDIA.
    // Это тот же источник, что у MSI Afterburner и диспетчера задач.
    // На ПК без NVIDIA nvml.dll отсутствует - тогда Available = false и показываем н/д
    public static class NvmlGpu
    {
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        private static extern int NvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        private static extern int NvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        private static extern int NvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        private static bool initialized = false;
        private static bool available = false;
        private static IntPtr deviceHandle = IntPtr.Zero;

        public static bool Available
        {
            get { EnsureInit(); return available; }
        }

        private static void EnsureInit()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                if (NvmlInit() != 0) return;
                if (NvmlDeviceGetHandleByIndex(0, out deviceHandle) != 0) return;
                available = true;
            }
            catch
            {
                // nvml.dll нет (не NVIDIA) или несовместимый драйвер
                available = false;
            }
        }

        // Температура ядра GPU в °C, или -1 если недоступна
        public static int GetTemperature()
        {
            EnsureInit();
            if (!available) return -1;
            try
            {
                uint temp;
                if (NvmlDeviceGetTemperature(deviceHandle, 0, out temp) != 0) return -1;
                return (int)temp;
            }
            catch
            {
                return -1;
            }
        }
    }

    public class HealthCheckPanel : Panel
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private readonly Timer refreshTimer;
        private readonly PerformanceCounter cpuCounter;

        private Label cpuValueLabel;
        private Label ramValueLabel;
        private Label diskValueLabel;
        private Label gpuValueLabel;
        private Label cpuTempValueLabel;
        private Label tempValueLabel;
        private Label statusLabel;

        // Пороги перегрева для уведомления из трея. Настраиваются степперами
        // на панели и хранятся в AppConfig (alert_cpu_temp / alert_gpu_temp)
        private int alertCpuTemp = 90;
        private int alertGpuTemp = 85;
        private bool alertEnabled = true;
        private const int AlertTempMin = 60;
        private const int AlertTempMax = 105;

        private static int ParseTemp(string v, int def)
        {
            int r;
            if (!int.TryParse(v, out r)) return def;
            return Math.Max(AlertTempMin, Math.Min(AlertTempMax, r));
        }

        private LoadGraph cpuGraph;
        private LoadGraph ramGraph;

        private RoundedButton deepCheckBtn;
        private Label deepResultLabel;
        private bool deepCheckRunning = false;

        private RoundedButton installDriverBtn;

        public HealthCheckPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            alertEnabled = AppConfig.GetBool("alert_enabled", true);
            alertCpuTemp = ParseTemp(AppConfig.Get("alert_cpu_temp", "90"), 90);
            alertGpuTemp = ParseTemp(AppConfig.Get("alert_gpu_temp", "85"), 85);

            cpuCounter = CreateCpuCounter();
            CpuSensor.InitAsync();
            Application.ApplicationExit += (s, e) => CpuSensor.Shutdown();
            refreshTimer = new Timer { Interval = 2000 };
            refreshTimer.Tick += (s, e) => RefreshMetrics();

            BuildUi();
            RefreshMetrics();
            refreshTimer.Start();
        }

        private PerformanceCounter CreateCpuCounter()
        {
            try { return new PerformanceCounter("Processor", "% Processor Time", "_Total"); }
            catch { return null; }
        }

        private PerformanceCounter CreateGpuCounter()
        {
            try { return new PerformanceCounter("GPU Engine", "Utilization Percentage", "_Total"); }
            catch { return null; }
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = "Health Check",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var stressBtn = new RoundedButton
            {
                Text = Lang.T("Стресс", "Stress"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(404, 20),
                Size = new Size(88, 32)
            };
            stressBtn.Click += (s, e) => {
                using (var f = new StressTestForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(stressBtn);

            var tempHistBtn = new RoundedButton
            {
                Text = Lang.T("📈 24ч", "📈 24h"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(320, 20),
                Size = new Size(76, 32)
            };
            tempHistBtn.Click += (s, e) => {
                using (var f = new TempHistoryForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(tempHistBtn);

            var bsodBtn = new RoundedButton
            {
                Text = "BSOD",
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(500, 20),
                Size = new Size(88, 32)
            };
            bsodBtn.Click += (s, e) => {
                using (var f = new BsodForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(bsodBtn);

            var benchBtn = new RoundedButton
            {
                Text = Lang.T("⚡ Бенчмарк", "⚡ Benchmark"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(596, 20),
                Size = new Size(140, 32)
            };
            benchBtn.Click += (s, e) => {
                using (var f = new BenchmarkForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(benchBtn);

            var summaryCard = Theme.MakeCard(this, new Point(24, 60), new Size(712, 100));

            AddMetricCard(summaryCard, "CPU", 16, 20, out cpuValueLabel);
            AddMetricCard(summaryCard, "RAM", 132, 20, out ramValueLabel);
            AddMetricCard(summaryCard, Lang.T("Диск", "Disk"), 248, 20, out diskValueLabel);
            AddMetricCard(summaryCard, "GPU", 364, 20, out gpuValueLabel);
            AddMetricCard(summaryCard, Lang.T("Темп. CPU", "CPU temp"), 480, 20, out cpuTempValueLabel);
            AddMetricCard(summaryCard, Lang.T("Темп. GPU", "GPU temp"), 596, 20, out tempValueLabel);

            var graphsRow = new Panel { Location = new Point(24, 172), Size = new Size(712, 210), BackColor = Color.Transparent };
            Controls.Add(graphsRow);

            var cpuGraphCard = Theme.MakeCard(graphsRow, new Point(0, 0), new Size(350, 210));
            var cpuGraphTitle = new Label
            {
                Text = Lang.T("Нагрузка CPU (последние 2 минуты)", "CPU load (last 2 minutes)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 14),
                AutoSize = true
            };
            cpuGraphCard.Controls.Add(cpuGraphTitle);
            cpuGraph = new LoadGraph { Location = new Point(16, 40), Size = new Size(318, 154), LineColor = Theme.Accent };
            cpuGraphCard.Controls.Add(cpuGraph);

            var ramGraphCard = Theme.MakeCard(graphsRow, new Point(362, 0), new Size(350, 210));
            var ramGraphTitle = new Label
            {
                Text = Lang.T("Нагрузка RAM (последние 2 минуты)", "RAM load (last 2 minutes)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 14),
                AutoSize = true
            };
            ramGraphCard.Controls.Add(ramGraphTitle);
            ramGraph = new LoadGraph { Location = new Point(16, 40), Size = new Size(318, 154), LineColor = Color.FromArgb(90, 160, 240) };
            ramGraphCard.Controls.Add(ramGraph);

            var disksCard = Theme.MakeCard(this, new Point(24, 396), new Size(712, 112));

            // Тумблер стража майнеров - алерт, если что-то грузит CPU в простое
            var minerLabel = new Label
            {
                Text = Lang.T("Страж майнеров (алерт при нагрузке в простое)", "Miner guard (alert on load while idle)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 516),
                AutoSize = true
            };
            Controls.Add(minerLabel);

            var minerToggle = new ToggleSwitch { Location = new Point(330, 512), Checked = MinerGuard.Enabled };
            minerToggle.CheckedChanged += (s, e) => { MinerGuard.Enabled = minerToggle.Checked; };
            Controls.Add(minerToggle);

            // Тревога перегрева: тумблер + настраиваемые пороги CPU/GPU
            var alertToggle = new ToggleSwitch { Location = new Point(400, 512), Checked = alertEnabled };
            alertToggle.CheckedChanged += (s, e) => {
                alertEnabled = alertToggle.Checked;
                AppConfig.SetBool("alert_enabled", alertEnabled);
            };
            Controls.Add(alertToggle);

            var alertLabel = new Label
            {
                Text = Lang.T("Тревога:", "Alert:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(452, 516),
                AutoSize = true
            };
            Controls.Add(alertLabel);

            MakeTempStepper("CPU", 508, 512, alertCpuTemp, v => {
                alertCpuTemp = v;
                AppConfig.Set("alert_cpu_temp", v.ToString());
            });
            MakeTempStepper("GPU", 624, 512, alertGpuTemp, v => {
                alertGpuTemp = v;
                AppConfig.Set("alert_gpu_temp", v.ToString());
            });

            var disksTitle = new Label
            {
                Text = Lang.T("Здоровье дисков (SMART)", "Disk health (SMART)"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            disksCard.Controls.Add(disksTitle);

            deepCheckBtn = new RoundedButton
            {
                Text = Lang.T("Глубокая проверка", "Deep check"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(552, 8),
                Size = new Size(144, 28)
            };
            deepCheckBtn.Click += (s, e) => RunDeepDiskCheck();
            disksCard.Controls.Add(deepCheckBtn);

            deepResultLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(16, 88),
                Size = new Size(680, 18),
                AutoEllipsis = true
            };
            disksCard.Controls.Add(deepResultLabel);

            BuildDiskRows(disksCard, 36);

            var futureDesc = new Label
            {
                Text = Lang.T(
                    "Темп. CPU читается через LibreHardwareMonitor (при перегреве выше порога придёт уведомление из трея, пороги настраиваются выше). Темп. GPU - через драйвер NVIDIA (NVML), на ПК без NVIDIA используется ACPI-датчик Windows.",
                    "CPU temp is read via LibreHardwareMonitor (a tray alert fires above the threshold; thresholds are configurable above). GPU temp comes from the NVIDIA driver (NVML); PCs without NVIDIA fall back to the Windows ACPI sensor."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(24, 548),
                Size = new Size(500, 34)
            };
            Controls.Add(futureDesc);

            // Кнопка появляется, когда датчик CPU недоступен: предлагает поставить
            // драйвер PawnIO (обходит блокировку «Целостности памяти» в Windows 11)
            installDriverBtn = new RoundedButton
            {
                Text = Lang.T("Вкл. датчики (PawnIO)", "Enable sensors (PawnIO)"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(540, 550),
                Size = new Size(196, 30),
                Visible = false
            };
            installDriverBtn.Click += (s, e) => SensorDriver.InstallBundled();
            Controls.Add(installDriverBtn);

            statusLabel = new Label
            {
                Text = Lang.T("Ожидание данных", "Waiting for data"),
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 592),
                AutoSize = true
            };
            Controls.Add(statusLabel);
        }

        // Компактный степпер порога температуры: «CPU − 90° +»
        private void MakeTempStepper(string title, int x, int y, int initial, Action<int> onChange)
        {
            var lbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(x, y + 5),
                AutoSize = true
            };
            Controls.Add(lbl);

            int value = initial;

            var valLbl = new Label
            {
                Text = value + "°",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(x + 52, y + 3),
                Size = new Size(34, 18),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var minusBtn = new RoundedButton
            {
                Text = "−",
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(x + 28, y),
                Size = new Size(24, 24)
            };
            minusBtn.Click += (s, e) => {
                if (value > AlertTempMin) { value -= 5; if (value < AlertTempMin) value = AlertTempMin; valLbl.Text = value + "°"; onChange(value); }
            };
            Controls.Add(minusBtn);
            Controls.Add(valLbl);

            var plusBtn = new RoundedButton
            {
                Text = "+",
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(x + 86, y),
                Size = new Size(24, 24)
            };
            plusBtn.Click += (s, e) => {
                if (value < AlertTempMax) { value += 5; if (value > AlertTempMax) value = AlertTempMax; valLbl.Text = value + "°"; onChange(value); }
            };
            Controls.Add(plusBtn);
        }

        // Глубокая проверка: SMART-флаг "скоро откажет" + ошибки дисков в журнале событий
        // за последние 30 дней. Работает в фоне, чтобы не подвешивать интерфейс
        private void RunDeepDiskCheck()
        {
            if (deepCheckRunning) return;
            deepCheckRunning = true;
            deepCheckBtn.Text = Lang.T("Проверяем...", "Checking...");
            deepResultLabel.Text = "";
            deepResultLabel.ForeColor = Theme.TextDim;

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                bool smartFail = false;
                string smartDetail = "";
                int diskErrors = 0;
                int ntfsErrors = 0;

                // 1. SMART-флаг предсказания отказа
                try
                {
                    var searcher = new ManagementObjectSearcher(
                        "root\\WMI", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        if (Convert.ToBoolean(obj["PredictFailure"]))
                        {
                            smartFail = true;
                            smartDetail = Convert.ToString(obj["InstanceName"]);
                        }
                    }
                }
                catch { }

                // 2. Ошибки дисков в журнале событий за 30 дней
                try
                {
                    string query = "*[System[Provider[@Name='disk' or @Name='Ntfs' or @Name='volmgr']" +
                        " and (Level=1 or Level=2 or Level=3)" +
                        " and TimeCreated[timediff(@SystemTime) <= 2592000000]]]";
                    var elQuery = new System.Diagnostics.Eventing.Reader.EventLogQuery(
                        "System", System.Diagnostics.Eventing.Reader.PathType.LogName, query);
                    var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(elQuery);
                    System.Diagnostics.Eventing.Reader.EventRecord record;
                    while ((record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            if (record.ProviderName == "disk") diskErrors++;
                            else ntfsErrors++;
                        }
                    }
                }
                catch { }

                e.Result = new object[] { smartFail, smartDetail, diskErrors, ntfsErrors };
            };
            worker.RunWorkerCompleted += (s, e) => {
                deepCheckRunning = false;
                deepCheckBtn.Text = Lang.T("Глубокая проверка", "Deep check");

                if (e.Error != null || e.Result == null)
                {
                    deepResultLabel.Text = Lang.T("Не удалось выполнить проверку", "Check failed");
                    deepResultLabel.ForeColor = Theme.Warning;
                    return;
                }

                object[] r = (object[])e.Result;
                bool smartFail = (bool)r[0];
                string smartDetail = (string)r[1];
                int diskErrors = (int)r[2];
                int ntfsErrors = (int)r[3];

                if (smartFail)
                {
                    deepResultLabel.Text = Lang.T(
                        "⚠ SMART предсказывает отказ диска! Срочно сделай бэкап. ", "⚠ SMART predicts disk failure! Back up your data now. ") + smartDetail;
                    deepResultLabel.ForeColor = Theme.Danger;
                }
                else if (diskErrors > 0 || ntfsErrors > 0)
                {
                    deepResultLabel.Text = Lang.T("SMART в норме, но за 30 дней есть ошибки в журнале: диск - ", "SMART is fine, but the event log has errors in 30 days: disk - ")
                        + diskErrors + ", NTFS/volmgr - " + ntfsErrors
                        + Lang.T(". Стоит следить и держать бэкап важных файлов", ". Worth monitoring; keep backups of important files");
                    deepResultLabel.ForeColor = Theme.Warning;
                }
                else
                {
                    deepResultLabel.Text = Lang.T("Всё чисто: SMART в норме, ошибок в журнале за 30 дней нет", "All clear: SMART is fine, no disk errors in the event log for 30 days");
                    deepResultLabel.ForeColor = Theme.Accent;
                }
            };
            worker.RunWorkerAsync();
        }

        // Читает статус дисков через WMI и рисует по строке на каждый физический диск
        private void BuildDiskRows(Panel parent, int startY)
        {
            int y = startY;
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT Model, Size, Status FROM Win32_DiskDrive");
                foreach (ManagementObject disk in searcher.Get())
                {
                    string model = Convert.ToString(disk["Model"]);
                    string status = Convert.ToString(disk["Status"]);
                    double sizeGb = 0;
                    try { sizeGb = Convert.ToDouble(disk["Size"]) / 1024 / 1024 / 1024; } catch { }

                    bool healthy = status == "OK";

                    var dot = new Panel { Size = new Size(10, 10), Location = new Point(20, y + 4), BackColor = Color.Transparent };
                    Color dotColor = healthy ? Theme.Accent : Theme.Danger;
                    dot.Paint += (s, e) => {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var b = new SolidBrush(dotColor)) e.Graphics.FillEllipse(b, 0, 0, 10, 10);
                    };
                    parent.Controls.Add(dot);

                    var nameLbl = new Label
                    {
                        Text = model + "  (" + Math.Round(sizeGb) + Lang.T(" ГБ)", " GB)"),
                        ForeColor = Theme.TextMain,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 9F),
                        Location = new Point(40, y),
                        AutoSize = true
                    };
                    parent.Controls.Add(nameLbl);

                    var statusLbl = new Label
                    {
                        Text = healthy
                            ? Lang.T("Работает нормально", "Healthy")
                            : Lang.T("Проблема: ", "Problem: ") + status,
                        ForeColor = healthy ? Theme.Accent : Theme.Danger,
                        BackColor = Color.Transparent,
                        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                        Location = new Point(540, y),
                        AutoSize = true
                    };
                    parent.Controls.Add(statusLbl);

                    y += 26;
                    if (y > 90) break; // больше трёх дисков в карточку не влезает
                }
            }
            catch
            {
                var errLbl = new Label
                {
                    Text = Lang.T("Не удалось прочитать статус дисков", "Failed to read disk status"),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(20, y),
                    AutoSize = true
                };
                parent.Controls.Add(errLbl);
            }
        }

        private void AddMetricCard(Control parent, string title, int x, int y, out Label valueLabel)
        {
            var card = new Panel { Location = new Point(x, y), Size = new Size(106, 60), BackColor = Theme.SidebarColor };
            parent.Controls.Add(card);

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(10, 8),
                AutoSize = true
            };
            card.Controls.Add(titleLbl);

            valueLabel = new Label
            {
                Text = "—",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(10, 28),
                AutoSize = true
            };
            card.Controls.Add(valueLabel);
        }

        private void RefreshMetrics()
        {
            try
            {
                float cpuPercent = GetCpuPercent();
                float ramPercent = GetRamPercent();

                cpuValueLabel.Text = cpuCounter == null ? Lang.T("н/д", "n/a") : string.Format("{0:0.0}%", cpuPercent);
                ramValueLabel.Text = string.Format("{0:0.0}%", ramPercent);
                diskValueLabel.Text = GetDiskUsage();
                gpuValueLabel.Text = GetGpuUsage();
                tempValueLabel.Text = GetTempDisplay();

                int cpuTemp = CpuSensor.GetTemperature();
                if (cpuTemp >= 0)
                {
                    cpuTempValueLabel.Text = cpuTemp + "°C";
                    cpuTempValueLabel.ForeColor = cpuTemp >= alertCpuTemp ? Theme.Danger
                        : (cpuTemp >= alertCpuTemp - 10 ? Theme.Warning : Theme.TextMain);
                    if (installDriverBtn != null) installDriverBtn.Visible = false;

                    if (alertEnabled && cpuTemp >= alertCpuTemp)
                    {
                        TrayNotify.Warn(
                            Lang.T("Перегрев CPU", "CPU overheating"),
                            Lang.T("Температура процессора ", "CPU temperature is ") + cpuTemp + "°C. " +
                            Lang.T("Проверь охлаждение и запылённость.", "Check cooling and dust buildup."));
                    }
                }
                else
                {
                    cpuTempValueLabel.Text = Lang.T("н/д", "n/a");
                    cpuTempValueLabel.ForeColor = Theme.TextDim;
                    // Датчик недоступен - вероятно, драйвер заблокирован Windows. Предлагаем PawnIO
                    if (installDriverBtn != null) installDriverBtn.Visible = true;
                }

                if (cpuCounter != null) cpuGraph.AddPoint(cpuPercent);
                ramGraph.AddPoint(ramPercent);

                // Делимся показаниями с меню трея, главной страницей и трекером игр
                SystemStats.CpuLoad = cpuValueLabel.Text;
                SystemStats.CpuTemp = cpuTempValueLabel.Text;
                SystemStats.RamLoad = ramValueLabel.Text;
                SystemStats.GpuLoad = gpuValueLabel.Text;
                SystemStats.GpuTemp = tempValueLabel.Text;
                SystemStats.CpuLoadNum = cpuCounter == null ? -1 : cpuPercent;
                SystemStats.CpuTempNum = cpuTemp;
                SystemStats.GpuTempNum = NvmlGpu.GetTemperature();

                // Тревога перегрева GPU (порог настраивается, кулдаун в TrayNotify)
                if (alertEnabled && SystemStats.GpuTempNum >= alertGpuTemp)
                {
                    TrayNotify.Warn(
                        Lang.T("Перегрев GPU", "GPU overheating"),
                        Lang.T("Температура видеокарты ", "GPU temperature is ") + SystemStats.GpuTempNum + "°C. " +
                        Lang.T("Проверь охлаждение и запылённость.", "Check cooling and dust buildup."));
                }

                statusLabel.Text = Lang.T("Данные обновлены", "Data updated");
                statusLabel.ForeColor = Theme.Accent;
            }
            catch
            {
                statusLabel.Text = Lang.T("Не удалось получить все данные", "Failed to get all data");
                statusLabel.ForeColor = Theme.Warning;
            }
        }

        private float GetCpuPercent()
        {
            if (cpuCounter == null) return 0;
            try { return cpuCounter.NextValue(); }
            catch { return 0; }
        }

        private float GetRamPercent()
        {
            try
            {
                var state = new MEMORYSTATUSEX();
                if (!GlobalMemoryStatusEx(state)) return 0;
                ulong used = state.ullTotalPhys - state.ullAvailPhys;
                return (float)((double)used * 100 / state.ullTotalPhys);
            }
            catch { return 0; }
        }

        private string GetDiskUsage()
        {
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        double usedPercent = (double)(drive.TotalSize - drive.TotalFreeSpace) * 100 / drive.TotalSize;
                        return string.Format("{0} {1:0}%", drive.Name.Replace("\\", ""), usedPercent);
                    }
                }
            }
            catch
            {
            }
            return Lang.T("н/д", "n/a");
        }

        private string GetGpuUsage()
        {
            float v = GpuLoad.Get();
            return v < 0 ? Lang.T("н/д", "n/a") : string.Format("{0:0.0}%", v);
        }

        private string GetTempDisplay()
        {
            // Сначала видеокарта через NVML (как у Afterburner)
            int gpuTemp = NvmlGpu.GetTemperature();
            if (gpuTemp >= 0) return gpuTemp + "°C";

            // Запасной вариант: ACPI-датчик материнки (есть не на всех системах)
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

                foreach (ManagementObject obj in searcher.Get())
                {
                    double tenthsOfKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                    double celsius = (tenthsOfKelvin / 10.0) - 273.15;
                    if (celsius > 0 && celsius < 150)
                    {
                        return string.Format("{0:0}°C", celsius);
                    }
                }
            }
            catch
            {
            }
            return Lang.T("н/д", "n/a");
        }
    }
}
