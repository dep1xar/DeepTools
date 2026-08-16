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
                    IsGpuEnabled = true
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
    }
}
