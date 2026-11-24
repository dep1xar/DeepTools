using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace DeepTools
{
    // Адаптивный профиль питания: автоматически переключает план по контексту.
    // Игра запущена → Ultimate Performance.
    // Только браузер/офис → Balanced.
    // Полный простой → Power Saver (или Balanced на десктопе).
    // Отступает если FPS уже стабильный и температура высокая.
    public static class AdaptivePowerProfile
    {
        public enum Context { Idle, Browser, Game }

        private static bool _enabled = false;
        private static Thread _thread;
        private static volatile bool _running = false;
        private static Context _lastCtx = Context.Idle;
        private const string PowerSaverGuid = "a1841308-3541-4fab-bc81-f71556f20b4a";

        private static readonly string[] BrowserProcs = {
            "chrome", "msedge", "firefox", "opera", "yandexbrowser", "brave"
        };

        public static bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (value) Start();
                else Stop();
            }
        }

        public static Context CurrentContext { get { return _lastCtx; } }

        private static void Start()
        {
            if (_thread != null && _thread.IsAlive) return;
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "AdaptivePower" };
            // Ниже Normal: смена плана питания не должна конкурировать с игрой
            _thread.Priority = ThreadPriority.BelowNormal;
            _thread.Start();
        }

        private static void Stop()
        {
            _running = false;
            // Восстанавливаем план, который был до включения адаптивного режима
            try { PowerPlan.RestorePrevious(); } catch { }
        }

        private static void Loop()
        {
            while (_running)
            {
                try { Tick(); } catch { }
                // 10 секунд: план питания меняется редко, а перечисление
                // процессов и WMI-запрос на ноутбуке - не бесплатные
                Thread.Sleep(10000);
            }
        }

        private static void Tick()
        {
            Context ctx = DetectContext();
            if (ctx == _lastCtx) return;
            _lastCtx = ctx;

            // Не трогаем план если температура CPU уже высокая (>85°C)
            int temp = CpuSensor.GetTemperature();
            if (temp > 85 && ctx == Context.Game) return;

            switch (ctx)
            {
                case Context.Game:
                    PowerPlan.EnableUltimate();
                    break;
                case Context.Browser:
                    // Balanced - достаточно для браузера
                    RunPowercfg("/setactive " + PowerPlan.GetBalancedGuid());
                    break;
                case Context.Idle:
                    // Десктоп: Balanced. Ноутбук (есть батарея): Power Saver
                    string idlePlan = IsLaptop() ? PowerSaverGuid : PowerPlan.GetBalancedGuid();
                    RunPowercfg("/setactive " + idlePlan);
                    break;
            }
        }

        private static Context DetectContext()
        {
            // Если PresentTracer видит кадры игры - это игра
            if (PresentTracer.GetAnyFps() > 10) return Context.Game;

            // GetProcessesByName ищет по имени без полного перечисления процессов,
            // а каждый массив обязательно освобождаем - иначе хэндлы процессов
            // копились каждый тик
            foreach (string b in BrowserProcs)
            {
                Process[] found = null;
                try
                {
                    found = Process.GetProcessesByName(b);
                    if (found.Length > 0) return Context.Browser;
                }
                catch { }
                finally
                {
                    if (found != null)
                        for (int i = 0; i < found.Length; i++)
                            try { found[i].Dispose(); } catch { }
                }
            }

            return Context.Idle;
        }

        // WMI-запрос про батарею небыстрый, а ответ не меняется - кэшируем
        private static bool? _isLaptop;

        private static bool IsLaptop()
        {
            if (_isLaptop.HasValue) return _isLaptop.Value;
            bool result = false;
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT BatteryStatus FROM Win32_Battery"))
                using (System.Management.ManagementObjectCollection items = searcher.Get())
                {
                    foreach (System.Management.ManagementObject obj in items)
                    {
                        using (obj) { }
                        result = true;
                        break;
                    }
                }
            }
            catch { }
            _isLaptop = result;
            return result;
        }

        private static void RunPowercfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.WaitForExit(5000);
                        try { p.StandardOutput.ReadToEnd(); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
