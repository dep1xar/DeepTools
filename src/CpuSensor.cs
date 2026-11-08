using System;
using System.Management;
using LibreHardwareMonitor.Hardware;

namespace DeepTools
{
    // Температура CPU через LibreHardwareMonitorLib (тот же движок, что у HWiNFO/LibreHardwareMonitor).
    // Требует прав администратора (у нас всегда есть) - библиотека грузит свой драйвер для чтения датчиков.
    // Инициализация занимает ~1-2 секунды, поэтому делается в фоне через InitAsync()
    public static class CpuSensor
    {
        private static Computer computer;
        private static IHardware cpu;
        private static bool initStarted = false;
        private static readonly object sync = new object();

        public static void InitAsync()
        {
            lock (sync)
            {
                if (initStarted) return;
                initStarted = true;
            }
            var t = new System.Threading.Thread(Init);
            t.IsBackground = true;
            t.Start();
        }

        private static void Init()
        {
            try
            {
                var c = new Computer();
                c.IsCpuEnabled = true;
                c.Open();
                foreach (IHardware hw in c.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        lock (sync)
                        {
                            computer = c;
                            cpu = hw;
                        }
                        return;
                    }
                }
                c.Close();
            }
            catch
            {
                // датчики недоступны (виртуалка, экзотический CPU) - будет н/д
            }
        }

        // Температура CPU в °C, или -1 если недоступна.
        // Предпочитаем Package/Tctl/Average - это "общая" температура процессора,
        // иначе берём максимум по ядрам
        public static int GetTemperature()
        {
            IHardware hw;
            lock (sync) { hw = cpu; }

            // LibreHardwareMonitor не поднялся (драйвер не загрузился, конфликт с другим
            // монитором, Secure Boot и т.п.) - пробуем запасной ACPI-датчик Windows.
            // Он есть не на всех платах и часто показывает температуру платы, а не ядра,
            // но это лучше, чем "н/д". Заодно пробуем ещё раз инициализировать LHM.
            if (hw == null)
            {
                if (!initStarted) InitAsync();
                int board0 = FanSensor.ReadBoardCpuTemp();
                if (board0 > 0) return board0;
                return GetAcpiTemperature();
            }

            try
            {
                hw.Update();
                float best = -1;
                float package = -1;
                foreach (ISensor s in hw.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                    float v = s.Value.Value;
                    if (v <= 0 || v > 120) continue;

                    string n = s.Name == null ? "" : s.Name;
                    if (n.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (v > package) package = v;
                    }
                    if (v > best) best = v;
                }
                if (package > 0) return (int)Math.Round(package);
                if (best > 0) return (int)Math.Round(best);
            }
            catch { }

            // LHM есть, но датчиков CPU не отдал: пробуем датчик материнки (SuperIO),
            // затем ACPI через WMI. Без этого владельцы плат, где WinRing0 не читает
            // MSR процессора, видели просто «н/д»
            int board = FanSensor.ReadBoardCpuTemp();
            if (board > 0) return board;
            return GetAcpiTemperature();
        }

        // Запасной датчик через WMI: MSAcpi_ThermalZoneTemperature (десятые доли кельвина)
        private static int GetAcpiTemperature()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    double tenthsKelvin = Convert.ToDouble(obj["CurrentTemperature"]);
                    double celsius = (tenthsKelvin / 10.0) - 273.15;
                    if (celsius > 0 && celsius < 150) return (int)Math.Round(celsius);
                }
            }
            catch { }
            return -1;
        }

        public static void Shutdown()
        {
            lock (sync)
            {
                try { if (computer != null) computer.Close(); } catch { }
                computer = null;
                cpu = null;
            }
        }
    }
}
