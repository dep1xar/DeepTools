using System;
using System.Runtime.InteropServices;

namespace DeepTools
{
    public static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Резервный (устаревший) способ ввода: нужен только как страховка, если
        // SendInput почему-то не примут
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        public const uint MOUSEEVENTF_LEFTUP = 0x04;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
        public const uint MOUSEEVENTF_RIGHTUP = 0x10;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x20;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x40;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_SCANCODE = 0x0008;
        public const int WM_HOTKEY = 0x0312;

        // Один экземпляр: второй запуск шлёт это сообщение уже запущенному окну,
        // чтобы оно вышло из трея, и сам закрывается. RegisterWindowMessage даёт
        // одинаковый id во всех процессах для одной и той же строки.
        public const int HWND_BROADCAST = 0xffff;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int RegisterWindowMessage(string message);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static readonly int WM_SHOW_DEEPTOOLS =
            RegisterWindowMessage("DeepTools_ShowExistingInstance_dep1xar");

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        public struct POINT
        {
            public int X;
            public int Y;
        }

        // ---- Синтетический ввод (кликер и макросы) ----
        // Было mouse_event/keybd_event, причём у клавиш скан-код оставался нулевым:
        // игры, которые читают скан-коды (Raw Input, DirectInput), таких нажатий не
        // видят, хотя мышь работает. Теперь всё идёт через SendInput, клавиши - со
        // скан-кодом. SendInput ещё и возвращает число принятых событий, поэтому
        // заблокированный ввод (права, античит) видно, а не «просто не работает»

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL, wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MAPVK_VK_TO_VSC = 0;

        // Сколько событий Windows не приняла: кликер показывает это пользователю,
        // иначе «не работает» остаётся загадкой
        public static int BlockedInputs;

        public static void ResetInputDiagnostics() { BlockedInputs = 0; }

        private static bool Send(INPUT input)
        {
            try
            {
                INPUT[] buffer = new INPUT[] { input };
                if (SendInput(1, buffer, Marshal.SizeOf(typeof(INPUT))) == 1) return true;
                return false;
            }
            catch { return false; }
        }

        private static bool SendMouse(uint flags)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.U.mi = new MOUSEINPUT { dwFlags = flags };
            if (Send(input)) return true;

            // Страховка: тот же ввод старым API, он умеет ровно то же самое
            try
            {
                mouse_event(flags, 0, 0, 0, UIntPtr.Zero);
                return true;
            }
            catch { BlockedInputs++; return false; }
        }

        // Клавиши-модификаторы шлём по VK: у них есть левый и правый вариант, и при
        // выводе из скан-кода Windows подставит не тот, которого ждёт программа
        private static bool IsGenericModifier(ushort vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12; // Shift, Ctrl, Alt
        }

        // Клавиши, которые в скан-коде совпадают с цифровым блоком и потому
        // требуют флага расширенной клавиши (иначе стрелка станет NumPad-цифрой)
        private static bool IsExtendedKey(ushort vk)
        {
            switch (vk)
            {
                case 0x21: case 0x22: case 0x23: case 0x24: // PageUp, PageDown, End, Home
                case 0x25: case 0x26: case 0x27: case 0x28: // стрелки
                case 0x2C: case 0x2D: case 0x2E:            // PrintScreen, Insert, Delete
                case 0x5B: case 0x5C: case 0x5D:            // Win, Win, Menu
                case 0x6F: case 0x90:                       // NumPad /, NumLock
                case 0xA3: case 0xA5:                       // правые Ctrl и Alt
                    return true;
                default:
                    return false;
            }
        }

        private static bool SendKey(ushort vk, bool up)
        {
            if (vk == 0) return false;

            ushort scan = IsGenericModifier(vk) ? (ushort)0 : (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            var input = new INPUT { type = INPUT_KEYBOARD };
            if (scan != 0)
            {
                uint flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
                if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
                input.U.ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags };
            }
            else
            {
                input.U.ki = new KEYBDINPUT { wVk = vk, wScan = 0, dwFlags = up ? KEYEVENTF_KEYUP : 0 };
            }
            if (Send(input)) return true;

            // Страховка: старое API (без скан-кода), чтобы клавиша всё-таки ушла
            try
            {
                keybd_event((byte)vk, (byte)scan, up ? KEYEVENTF_KEYUP : 0, UIntPtr.Zero);
                return true;
            }
            catch { BlockedInputs++; return false; }
        }

        public const int HOTKEY_ID_CLICKER = 1;
        public const int HOTKEY_ID_SCREENSHOT = 2;
        public const int HOTKEY_ID_OVERLAY = 3;
        public const int HOTKEY_ID_REGION = 4;
        public const int HOTKEY_ID_MACRO_REC = 5;
        public const int HOTKEY_ID_MACRO_PLAY = 6;

        public static void ClickLeft()
        {
            SendMouse(MOUSEEVENTF_LEFTDOWN);
            SendMouse(MOUSEEVENTF_LEFTUP);
        }

        public static void ClickRight()
        {
            SendMouse(MOUSEEVENTF_RIGHTDOWN);
            SendMouse(MOUSEEVENTF_RIGHTUP);
        }

        public static void PressKey(System.Windows.Forms.Keys key)
        {
            ushort vk = (ushort)key;
            SendKey(vk, false);
            SendKey(vk, true);
        }

        // Раздельные нажатие/отпускание для режима "Зажимать"
        public static void MouseLeftDown() { SendMouse(MOUSEEVENTF_LEFTDOWN); }
        public static void MouseLeftUp() { SendMouse(MOUSEEVENTF_LEFTUP); }
        public static void MouseRightDown() { SendMouse(MOUSEEVENTF_RIGHTDOWN); }
        public static void MouseRightUp() { SendMouse(MOUSEEVENTF_RIGHTUP); }
        public static void MouseMiddleDown() { SendMouse(MOUSEEVENTF_MIDDLEDOWN); }
        public static void MouseMiddleUp() { SendMouse(MOUSEEVENTF_MIDDLEUP); }
        public static void KeyDown(System.Windows.Forms.Keys key) { SendKey((ushort)key, false); }
        public static void KeyUp(System.Windows.Forms.Keys key) { SendKey((ushort)key, true); }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        // Тёмный системный скроллбар (Win10 1809+). Без этого AutoScroll-панели и
        // списки рисуют ослепительно-белую полосу прокрутки на тёмной теме.
        // Применяем при создании handle: контролы часто настраиваются до показа
        public static void ApplyDarkScrollbar(System.Windows.Forms.Control control)
        {
            ApplyWindowTheme(control, "DarkMode_Explorer");
        }

        // Тёмный ComboBox: DarkMode_CFD красит рамку, стрелку и выпадающий список
        public static void ApplyDarkCombo(System.Windows.Forms.Control control)
        {
            ApplyWindowTheme(control, "DarkMode_CFD");
        }

        private static void ApplyWindowTheme(System.Windows.Forms.Control control, string theme)
        {
            if (Theme.IsLight) return; // светлой теме системные цвета подходят как есть
            if (control.IsHandleCreated)
            {
                try { SetWindowTheme(control.Handle, theme, null); } catch { }
            }
            else
            {
                control.HandleCreated += (s, e) => {
                    try { SetWindowTheme(control.Handle, theme, null); } catch { }
                };
            }
        }

        // DPI awareness — чтобы интерфейс не был "пиксельным" на 125%/150%/175% масштабе
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int SetProcessDpiAwarenessDelegate(int awareness);

    }
}