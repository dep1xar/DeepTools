using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // Блокировка клавиши Win в полноэкранных играх, чтобы случайное нажатие
    // не сворачивало игру. Низкоуровневый хук клавиатуры (WH_KEYBOARD_LL):
    // когда тумблер включён и на переднем плане полноэкранная игра - Win глотается.
    // Сочетания с Win (Win+D и т.п.) блокируются тоже, обычные клавиши не трогаем
    public static class WinKeyBlocker
    {
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_KEYBOARD_LL = 13;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private static IntPtr hookHandle = IntPtr.Zero;
        // Держим ссылку на делегат, иначе GC его соберёт и хук упадёт
        private static LowLevelKeyboardProc hookProc = HookCallback;

        // Тумблер из GameBooster. Хук ставится один раз, фильтрация - по флагу
        public static bool Enabled
        {
            get { return AppConfig.GetBool("block_win_key", false); }
            set
            {
                AppConfig.SetBool("block_win_key", value);
                if (value) EnsureHooked();
                else Unhook();
            }
        }

        // Выставляется детектом GameBooster: true, когда на переднем плане полноэкранная игра
        public static volatile bool GameActive = false;

        public static void Init()
        {
            if (Enabled) EnsureHooked();
        }

        private static void EnsureHooked()
        {
            if (hookHandle != IntPtr.Zero) return;
            try
            {
                using (Process proc = Process.GetCurrentProcess())
                using (ProcessModule module = proc.MainModule)
                {
                    hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(module.ModuleName), 0);
                }
            }
            catch
            {
                hookHandle = IntPtr.Zero;
            }
        }

        private static void Unhook()
        {
            if (hookHandle == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(hookHandle); } catch { }
            hookHandle = IntPtr.Zero;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && GameActive)
            {
                int vk = Marshal.ReadInt32(lParam); // первое поле KBDLLHOOKSTRUCT - vkCode
                if (vk == VK_LWIN || vk == VK_RWIN)
                {
                    return (IntPtr)1; // глотаем и нажатие, и отпускание
                }
            }
            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }

        public static void Shutdown()
        {
            Unhook();
        }
    }
}
