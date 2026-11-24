using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeepTools
{
    // Единый фоновый источник показаний для главной, Health Check, трея и виджета.
    //
    // Раньше всё это считал таймер HealthCheckPanel прямо в UI-потоке: Update()
    // LibreHardwareMonitor, PerformanceCounter и WMI-запросы могли занять сотни
    // миллисекунд. Когда CPU загружен игрой, такой тик растягивался на секунды -
    // окно переставало отвечать, а глобальный хук клавиатуры (он живёт в UI-потоке)
    // задерживал ввод во всей системе. Теперь опрос идёт в отдельном потоке с
    // низким приоритетом, а интерфейс только читает готовые значения.
    public static class SystemStatsSampler
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint Low;
            public uint High;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

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

        private static readonly object startLock = new object();
        private static Thread thread;
        private static volatile bool running;

        // GetSystemTimes отдаёт накопленные значения - первый замер только
        // запоминает базу, процент появляется со второго
        private static bool cpuPrimed;
        private static ulong prevIdle;
        private static ulong prevKernel;
        private static ulong prevUser;

        private static string cachedDiskUsage;
        private static DateTime cachedDiskAt = DateTime.MinValue;

        private static int cachedAlertCpu = 90;
        private static int cachedAlertGpu = 85;
        private static bool cachedAlertEnabled = true;
        private static DateTime alertConfigAt = DateTime.MinValue;

        public static void Start()
        {
            lock (startLock)
            {
                if (thread != null) return;
                running = true;
                thread = new Thread(Loop);
                thread.IsBackground = true;
                thread.Name = "StatsSampler";
                // Ниже Normal: под загруженным CPU игра получает приоритет,
                // а мониторинг догоняет, когда появляется свободное время
                thread.Priority = ThreadPriority.BelowNormal;
                thread.Start();
            }
        }

        public static void Stop()
        {
            running = false;
        }

        private static void Loop()
        {
            while (running)
            {
                try { Sample(); }
                catch { }

                // 2 секунды - тот же ритм, что был у Health Check. CPU считается
                // по дельте GetSystemTimes, поэтому окно замера не важно
                Thread.Sleep(2000);
            }
        }

        private static void Sample()
        {
            float cpu = ReadCpuPercent();
            float ram = ReadRamPercent();
            int cpuTemp = CpuSensor.GetTemperature();
            int gpuTemp = NvmlGpu.GetTemperature();
            if (gpuTemp < 0) gpuTemp = AcpiThermal.Get();
            float gpuLoad = GpuLoad.Get();
            string disk = ReadDiskUsage();

            SystemStats.CpuLoadNum = cpu;
            SystemStats.CpuLoad = cpu >= 0 ? string.Format("{0:0.0}%", cpu) : Lang.T("н/д", "n/a");

            SystemStats.RamLoadNum = ram;
            SystemStats.RamLoad = ram >= 0 ? string.Format("{0:0.0}%", ram) : Lang.T("н/д", "n/a");

            SystemStats.CpuTempNum = cpuTemp;
            SystemStats.CpuTemp = cpuTemp >= 0 ? cpuTemp + "°C" : Lang.T("н/д", "n/a");

            SystemStats.GpuLoadNum = gpuLoad;
            SystemStats.GpuLoad = gpuLoad >= 0 ? string.Format("{0:0.0}%", gpuLoad) : Lang.T("н/д", "n/a");

            SystemStats.GpuTempNum = gpuTemp;
            SystemStats.GpuTemp = gpuTemp >= 0 ? gpuTemp + "°C" : Lang.T("н/д", "n/a");

            SystemStats.DiskUsage = disk;

            CheckTemperatureAlerts(cpuTemp, gpuTemp);
        }

        private static ulong ToUInt64(FILETIME ft)
        {
            return ((ulong)ft.High << 32) | ft.Low;
        }

        private static float ReadCpuPercent()
        {
            FILETIME idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return -1;

            ulong i = ToUInt64(idle);
            ulong k = ToUInt64(kernel);
            ulong u = ToUInt64(user);

            if (!cpuPrimed)
            {
                prevIdle = i;
                prevKernel = k;
                prevUser = u;
                cpuPrimed = true;
                return -1;
            }

            ulong dIdle = i - prevIdle;
            ulong dKernel = k - prevKernel;
            ulong dUser = u - prevUser;
            prevIdle = i;
            prevKernel = k;
            prevUser = u;

            // В kernel уже входит idle, поэтому всего времени = kernel + user
            ulong total = dKernel + dUser;
            if (total == 0) return -1;
            ulong busy = total > dIdle ? total - dIdle : 0;
            double percent = (double)busy * 100.0 / total;
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return (float)percent;
        }

        private static float ReadRamPercent()
        {
            try
            {
                var state = new MEMORYSTATUSEX();
                if (!GlobalMemoryStatusEx(state)) return -1;
                return state.dwMemoryLoad;
            }
            catch { return -1; }
        }

        private static string ReadDiskUsage()
        {
            // Обращение к уснувшему HDD может занять сотни миллисекунд, поэтому
            // результат кэшируется, а сам обход идёт только в этом потоке
            if (cachedDiskUsage != null && (DateTime.UtcNow - cachedDiskAt).TotalSeconds < 60)
                return cachedDiskUsage;
            cachedDiskAt = DateTime.UtcNow;

            string result = Lang.T("н/д", "n/a");
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        double used = (double)(drive.TotalSize - drive.TotalFreeSpace) * 100 / drive.TotalSize;
                        result = string.Format("{0} {1:0}%", drive.Name.Replace("\\", ""), used);
                        break;
                    }
                }
            }
            catch { }

            cachedDiskUsage = result;
            return result;
        }

        // Пороговые уведомления не зависят от того, открыт ли Health Check:
        // раньше они жили в его таймере, и без прогрева панели пропадали
        private static void CheckTemperatureAlerts(int cpuTemp, int gpuTemp)
        {
            if ((DateTime.UtcNow - alertConfigAt).TotalSeconds >= 30)
            {
                alertConfigAt = DateTime.UtcNow;
                cachedAlertEnabled = AppConfig.GetBool("alert_enabled", true);
                cachedAlertCpu = ParseTemp(AppConfig.Get("alert_cpu_temp", "90"), 90);
                cachedAlertGpu = ParseTemp(AppConfig.Get("alert_gpu_temp", "85"), 85);
            }

            if (!cachedAlertEnabled) return;

            if (cpuTemp > 0 && cpuTemp >= cachedAlertCpu)
            {
                TrayNotify.Warn(
                    Lang.T("Перегрев CPU", "CPU overheating"),
                    Lang.T("Температура процессора ", "CPU temperature is ") + cpuTemp + "°C. " +
                    Lang.T("Проверь охлаждение и запылённость.", "Check cooling and dust buildup."));
            }

            if (gpuTemp > 0 && gpuTemp >= cachedAlertGpu)
            {
                TrayNotify.Warn(
                    Lang.T("Перегрев GPU", "GPU overheating"),
                    Lang.T("Температура видеокарты ", "GPU temperature is ") + gpuTemp + "°C. " +
                    Lang.T("Проверь охлаждение и запылённость.", "Check cooling and dust buildup."));
            }
        }

        private static int ParseTemp(string value, int fallback)
        {
            int result;
            if (!int.TryParse(value, out result)) return fallback;
            if (result < 60) result = 60;
            if (result > 105) result = 105;
            return result;
        }
    }

    // Запасной ACPI-датчик Windows (MSAcpi_ThermalZoneTemperature). WMI-запрос
    // может думать сотни миллисекунд, поэтому он никогда не выполняется в
    // вызывающем потоке: значение обновляется в фоне раз в 30 секунд.
    public static class AcpiThermal
    {
        private static int cached = -1;
        private static long nextTryTicks;
        private static int queryRunning;

        public static int Get()
        {
            if (DateTime.UtcNow.Ticks >= Interlocked.Read(ref nextTryTicks)
                && Interlocked.CompareExchange(ref queryRunning, 1, 0) == 0)
            {
                Interlocked.Exchange(ref nextTryTicks, DateTime.UtcNow.AddSeconds(30).Ticks);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    int found = -1;
                    try
                    {
                        using (var searcher = new ManagementObjectSearcher(
                            "root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                        using (ManagementObjectCollection results = searcher.Get())
                        {
                            foreach (ManagementObject obj in results)
                            {
                                try
                                {
                                    double tenthsKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                                    double celsius = (tenthsKelvin / 10.0) - 273.15;
                                    if (celsius > 0 && celsius < 150)
                                    {
                                        found = (int)Math.Round(celsius);
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }

                    cached = found;
                    Interlocked.Exchange(ref queryRunning, 0);
                });
            }

            return cached;
        }
    }
}
