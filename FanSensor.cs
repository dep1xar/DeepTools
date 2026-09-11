using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace DeepTools
{
    // Обороты вентиляторов через LibreHardwareMonitorLib. Датчики Fan живут на
    // материнке (SuperIO-чип, это subhardware) и на видеокартах. Инициализация
    // медленная - делаем в фоне, как CpuSensor.
    public static class FanSensor
    {
        private static Computer computer;
        private static bool initStarted = false;
        private static readonly object sync = new object();

        public static void InitAsync()
        {
            lock (sync)
            {
                if (initStarted) return;
                initStarted = true;
            }
            var t = new System.Threading.Thread(Init) { IsBackground = true };
            t.Start();
        }

        private static void Init()
        {
            try
            {
                var c = new Computer
                {
                    IsMotherboardEnabled = true,
                    IsControllerEnabled = true,
                    IsGpuEnabled = true,
                    IsCpuEnabled = true
                };
                c.Open();
                lock (sync) { computer = c; }
            }
            catch { }
        }

        // Список (имя, обороты) по всем найденным вентиляторам с ненулевой скоростью
        public static List<KeyValuePair<string, int>> Read()
        {
            var result = new List<KeyValuePair<string, int>>();
            Computer c;
            lock (sync) { c = computer; }
            if (c == null) { if (!initStarted) InitAsync(); return result; }

            try
            {
                foreach (IHardware hw in c.Hardware)
                {
                    hw.Update();
                    Collect(hw, result);
                    foreach (IHardware sub in hw.SubHardware)
                    {
                        sub.Update();
                        Collect(sub, result);
                    }
                }
            }
            catch { }
            return result;
        }

        private static void Collect(IHardware hw, List<KeyValuePair<string, int>> into)
        {
            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Fan || !s.Value.HasValue) continue;
                int rpm = (int)Math.Round(s.Value.Value);
                if (rpm <= 0) continue; // остановленный/неподключённый вентилятор
                string name = string.IsNullOrEmpty(s.Name) ? "Fan" : s.Name;
                into.Add(new KeyValuePair<string, int>(name, rpm));
            }
        }

        public static void Shutdown()
        {
            lock (sync)
            {
                try { if (computer != null) computer.Close(); } catch { }
                computer = null;
            }
        }

        // Температура GPU через LibreHardwareMonitor. Спасает владельцев AMD/Intel:
        // NVML есть только у NVIDIA, а LHM читает и Radeon, и встройку. -1 = нет данных
        public static int ReadGpuTemp()
        {
            Computer c;
            lock (sync) { c = computer; }
            if (c == null) { if (!initStarted) InitAsync(); return -1; }

            try
            {
                foreach (IHardware hw in c.Hardware)
                {
                    if (hw.HardwareType != HardwareType.GpuNvidia &&
                        hw.HardwareType != HardwareType.GpuAmd &&
                        hw.HardwareType != HardwareType.GpuIntel) continue;

                    hw.Update();
                    int best = -1;
                    foreach (ISensor s in hw.Sensors)
                    {
                        if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                        int v = (int)Math.Round(s.Value.Value);
                        if (v <= 0 || v > 125) continue;
                        // «Core» - температура ядра, приоритетнее hotspot/памяти
                        if (s.Name != null && s.Name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) return v;
                        if (v > best) best = v;
                    }
                    if (best > 0) return best;
                }
            }
            catch { }
            return -1;
        }

        // Температура с датчиков материнки (CPU socket у SuperIO-чипа) - запасной
        // вариант, когда драйвер LHM не смог прочитать сам CPU
        public static int ReadBoardCpuTemp()
        {
            Computer c;
            lock (sync) { c = computer; }
            if (c == null) return -1;

            try
            {
                foreach (IHardware hw in c.Hardware)
                {
                    if (hw.HardwareType != HardwareType.Motherboard) continue;
                    hw.Update();
                    int found = ScanCpuTemp(hw);
                    if (found > 0) return found;
                    foreach (IHardware sub in hw.SubHardware)
                    {
                        sub.Update();
                        found = ScanCpuTemp(sub);
                        if (found > 0) return found;
                    }
                }
            }
            catch { }
            return -1;
        }

        private static int ScanCpuTemp(IHardware hw)
        {
            foreach (ISensor s in hw.Sensors)
            {
                if (s.SensorType != SensorType.Temperature || !s.Value.HasValue) continue;
                int v = (int)Math.Round(s.Value.Value);
                if (v <= 0 || v > 125) continue;
                string n = s.Name == null ? "" : s.Name;
                if (n.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) return v;
            }
            return -1;
        }
    }
}
