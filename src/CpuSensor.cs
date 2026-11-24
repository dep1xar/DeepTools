using System;
using System.Threading;
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

        // LibreHardwareMonitor не потокобезопасен: Update() и чтение сенсоров
        // нельзя делать одновременно из UI, sampler'а и адаптивного профиля.
        // Раньше это могло закончиться нативным AccessViolation без записи в
        // crash.log. Теперь все обращения сериализованы, а результат кэшируется
        // на 1.5 секунды - несколько потребителей не дёргают драйвер без нужды.
        private static readonly object readSync = new object();
        private static int cachedTemp = int.MinValue;
        private static DateTime cachedAt = DateTime.MinValue;
        private static readonly TimeSpan CacheTime = TimeSpan.FromMilliseconds(1500);

        public static void InitAsync()
        {
            lock (sync)
            {
                if (initStarted) return;
                initStarted = true;
            }
            var t = new Thread(Init);
            t.IsBackground = true;
            t.Name = "CpuSensorInit";
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
            lock (readSync)
            {
                if (cachedTemp != int.MinValue && (DateTime.UtcNow - cachedAt) < CacheTime)
                    return cachedTemp;

                int result = ReadTemperatureLocked();
                cachedTemp = result;
                cachedAt = DateTime.UtcNow;
                return result;
            }
        }

        private static int ReadTemperatureLocked()
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
                // WMI-запрос уходит в фон: в UI-потоке он мог подвесить окно
                return AcpiThermal.Get();
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
            return AcpiThermal.Get();
        }

        public static void Shutdown()
        {
            lock (readSync)
            {
                lock (sync)
                {
                    try { if (computer != null) computer.Close(); } catch { }
                    computer = null;
                    cpu = null;
                }
                cachedTemp = int.MinValue;
                cachedAt = DateTime.MinValue;
            }
        }
    }
}
