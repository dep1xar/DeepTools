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

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        public const uint MOUSEEVENTF_LEFTUP = 0x04;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
        public const uint MOUSEEVENTF_RIGHTUP = 0x10;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x20;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x40;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        public struct POINT
        {
            public int X;
            public int Y;
        }

        // Раздельные нажатие/отпускание средней кнопки - для воспроизведения макросов
        public static void MouseMiddleDown() { mouse_event(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0, UIntPtr.Zero); }
        public static void MouseMiddleUp() { mouse_event(MOUSEEVENTF_MIDDLEUP, 0, 0, 0, UIntPtr.Zero); }

        public const int HOTKEY_ID_CLICKER = 1;
        public const int HOTKEY_ID_SCREENSHOT = 2;
        public const int HOTKEY_ID_OVERLAY = 3;
        public const int HOTKEY_ID_REGION = 4;
        public const int HOTKEY_ID_MACRO_REC = 5;
        public const int HOTKEY_ID_MACRO_PLAY = 6;

        public static void ClickLeft()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void ClickRight()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
        }

        public static void PressKey(System.Windows.Forms.Keys key)
        {
            byte vk = (byte)key;
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // Раздельные нажатие/отпускание для режима "Зажимать"
        public static void MouseLeftDown() { mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero); }
        public static void MouseLeftUp() { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero); }
        public static void MouseRightDown() { mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero); }
        public static void MouseRightUp() { mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero); }
        public static void KeyDown(System.Windows.Forms.Keys key) { keybd_event((byte)key, 0, 0, UIntPtr.Zero); }
        public static void KeyUp(System.Windows.Forms.Keys key) { keybd_event((byte)key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); }

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
    }
}