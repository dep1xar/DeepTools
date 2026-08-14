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
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "AdaptivePower" };
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
                Thread.Sleep(4000);
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

            // Иначе смотрим на запущенные процессы
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return Context.Idle; }

            bool browserRunning = false;
            foreach (Process p in all)
            {
                try
                {
                    string name = p.ProcessName.ToLowerInvariant();
                    foreach (string b in BrowserProcs)
                    {
                        if (name == b) { browserRunning = true; break; }
                    }
                }
                catch { }
            }

            return browserRunning ? Context.Browser : Context.Idle;
        }

        private static bool IsLaptop()
        {
            try
            {
                var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT BatteryStatus FROM Win32_Battery");
                foreach (System.Management.ManagementObject obj in searcher.Get())
                    return true;
            }
            catch { }
            return false;
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
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(5000);
            }
            catch { }
        }
    }
}
