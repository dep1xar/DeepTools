using System;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // Единая проверка «сейчас играет игра». Нужна автоочистке RAM и standby-листа:
    // сбрасывать рабочие наборы и standby-страницы под работающей игрой нельзя -
    // игра тут же полезет за ними обратно на диск, и это даёт фризы, а не пользу.
    //
    // Источники по убыванию точности: детект GameBooster, активная игровая сессия,
    // затем дешёвая проверка полноэкранного окна на переднем плане. Последняя
    // нужна, если Game Booster ни разу не открывали - его таймер тогда не работает.
    public static class GameActivity
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int max);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private static readonly object sync = new object();
        private static bool cachedFullscreen;
        private static int cachedAt;

        public static bool IsGameActive
        {
            get
            {
                // Самые точные признаки, если соответствующие модули уже работают
                if (WinKeyBlocker.GameActive) return true;
                if (GameSessionTracker.IsGameRunning) return true;
                if (GameBoosterPanel.DetectedGame != null) return true;

                // Проверка окна занимает доли миллисекунды, но кэшируем на 2 секунды:
                // её дёргают таймеры автоочистки, а не каждый кадр
                lock (sync)
                {
                    int now = Environment.TickCount;
                    if (unchecked(now - cachedAt) < 2000) return cachedFullscreen;
                    cachedAt = now;
                    cachedFullscreen = CheckFullscreenForeground();
                    return cachedFullscreen;
                }
            }
        }

        private static bool CheckFullscreenForeground()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero || IsIconic(hwnd)) return false;

                // Рабочий стол и панель задач тоже «на весь экран» - это не игра
                var cls = new System.Text.StringBuilder(64);
                GetClassName(hwnd, cls, cls.Capacity);
                string name = cls.ToString();
                if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd") return false;

                RECT wr;
                if (!GetWindowRect(hwnd, out wr)) return false;

                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO();
                mi.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(monitor, ref mi)) return false;

                RECT mr = mi.rcMonitor;
                // Допуск 2 px: некоторые игры ставят окно чуть больше монитора
                return Math.Abs(wr.Left - mr.Left) <= 2
                    && Math.Abs(wr.Top - mr.Top) <= 2
                    && Math.Abs(wr.Right - mr.Right) <= 2
                    && Math.Abs(wr.Bottom - mr.Bottom) <= 2;
            }
            catch { return false; }
        }
    }
}
