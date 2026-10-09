using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // Описание одной настраиваемой глобальной горячей клавиши
    public class HotkeyDef
    {
        public int Id;           // NativeMethods.HOTKEY_ID_*
        public string ConfigKey;
        public Keys Default;
        public string NameRu;
        public string NameEn;
        public Keys Current;

        public string Name { get { return Lang.T(NameRu, NameEn); } }
    }

    // Единая точка про глобальные хоткеи: список действий, текущие клавиши,
    // регистрация в Windows и сохранение в конфиг. UI переназначения - карточка
    // «Горячие клавиши» в Настройках (mainform), захват клавиши - в ProcessCmdKey.
    // Хоткеи макросов (F6/F7 внутри окна макросов) сюда не входят - они живут,
    // только пока открыто окно MacroForm
    public static class Hotkeys
    {
        // "screenshot_hotkey" - ключ конфига из старых версий, сохранён для совместимости
        public static readonly HotkeyDef[] All =
        {
            new HotkeyDef { Id = NativeMethods.HOTKEY_ID_REGION,     ConfigKey = "hotkey_region",     Default = Keys.F6,  NameRu = "Скриншот области",     NameEn = "Region screenshot" },
            new HotkeyDef { Id = NativeMethods.HOTKEY_ID_CLICKER,    ConfigKey = "hotkey_clicker",    Default = Keys.F8,  NameRu = "Автокликер вкл/выкл",  NameEn = "Auto clicker on/off" },
            new HotkeyDef { Id = NativeMethods.HOTKEY_ID_SCREENSHOT, ConfigKey = "screenshot_hotkey", Default = Keys.F9,  NameRu = "Скриншот экрана",      NameEn = "Screen screenshot" },
            new HotkeyDef { Id = NativeMethods.HOTKEY_ID_OVERLAY,    ConfigKey = "hotkey_overlay",    Default = Keys.F10, NameRu = "Оверлей вкл/выкл",     NameEn = "Overlay on/off" },
        };

        public static void Load()
        {
            foreach (HotkeyDef d in All)
            {
                try { d.Current = (Keys)Enum.Parse(typeof(Keys), AppConfig.Get(d.ConfigKey, d.Default.ToString())); }
                catch { d.Current = d.Default; }
            }
        }

        public static HotkeyDef Find(int id)
        {
            foreach (HotkeyDef d in All)
                if (d.Id == id) return d;
            return null;
        }

        public static Keys Get(int id)
        {
            HotkeyDef d = Find(id);
            return d != null ? d.Current : Keys.None;
        }

        // Каким действием уже занята клавиша (кроме указанного) - защита от конфликтов
        public static HotkeyDef UsedBy(Keys key, int exceptId)
        {
            foreach (HotkeyDef d in All)
                if (d.Id != exceptId && d.Current == key) return d;
            return null;
        }

        public static void RegisterAll(IntPtr handle)
        {
            active.Clear();
            viaSystem.Clear();
            hookHandled.Clear();

            EnsureHook(handle);
            foreach (HotkeyDef d in All)
            {
                if (d.Current == Keys.None) continue;
                active.Add(d.Id);
                // RegisterHotKey возвращает false, если клавишу уже заняла другая
                // программа - раньше результат молча терялся и хоткей «просто не
                // работал». Теперь это видно (см. IsRegistered), а саму клавишу
                // всё равно подхватит хук
                if (NativeMethods.RegisterHotKey(handle, d.Id, 0, (uint)d.Current))
                    viaSystem.Add(d.Id);
            }
        }

        public static void UnregisterAll(IntPtr handle)
        {
            foreach (HotkeyDef d in All)
                NativeMethods.UnregisterHotKey(handle, d.Id);
            active.Clear();
            viaSystem.Clear();
        }

        // Может ли DeepTools вообще поймать этот хоткей: либо его держит
        // RegisterHotKey, либо работает хук
        public static bool IsRegistered(int id)
        {
            if (!active.Contains(id)) return false;
            return IsHookActive || viaSystem.Contains(id);
        }

        public static bool IsHookActive { get { return hookHandle != IntPtr.Zero; } }

        // Переназначение: снимает старую регистрацию, вешает новую, пишет в конфиг
        public static void Set(IntPtr handle, int id, Keys key)
        {
            HotkeyDef d = Find(id);
            if (d == null) return;
            NativeMethods.UnregisterHotKey(handle, d.Id);
            viaSystem.Remove(d.Id);
            d.Current = key;
            if (key != Keys.None)
            {
                active.Add(d.Id);
                if (NativeMethods.RegisterHotKey(handle, d.Id, 0, (uint)key))
                    viaSystem.Add(d.Id);
            }
            else
            {
                active.Remove(d.Id);
            }
            AppConfig.Set(d.ConfigKey, key.ToString());
        }

        // ---- Страховка: низкоуровневый хук клавиатуры ----
        // RegisterHotKey не срабатывает, если клавишу перехватила игра или её
        // античит. Хук, поставленный позже, получает нажатие раньше остальных,
        // поэтому горячие клавиши ловит ещё и он и шлёт своё сообщение окну.
        // Клавишу при этом не глотаем - она нужна и программе под нами
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        // Отдельное сообщение для «нажатие поймал хук»: WM_HOTKEY от системы и
        // наше сообщение приходят на одну и ту же клавишу, и по ним видно, что
        // второй раз обрабатывать её не нужно
        public static readonly int WM_HOTKEY_HOOK = 0x8000 + 42;

        private const int HOOK_DUPLICATE_MS = 1500;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Хоткеи, которые DeepTools вообще обрабатывает
        private static readonly HashSet<int> active = new HashSet<int>();
        // Те, что дополнительно заняты в Windows через RegisterHotKey
        private static readonly HashSet<int> viaSystem = new HashSet<int>();
        // Хоткеи, которые временно заняты другим окном (например, окном макросов)
        private static readonly HashSet<int> suspended = new HashSet<int>();
        // Когда хук последний раз поймал клавишу: по этой отметке отсекаем дубль
        private static readonly Dictionary<int, int> hookHandled = new Dictionary<int, int>();

        private static IntPtr hookHandle = IntPtr.Zero;
        // Ссылку на делегат держим статически, иначе GC соберёт его и хук упадёт
        private static readonly LowLevelKeyboardProc hookProc = HookCallback;
        private static IntPtr hookWindow = IntPtr.Zero;

        public static void InstallHook(IntPtr window)
        {
            hookWindow = window;
            EnsureHook();
        }

        // Переставить хук в начало цепочки: вызывается, когда нашлась игра -
        // её хук мог быть поставлен позже нашего
        public static void ReinstallHook()
        {
            if (hookWindow == IntPtr.Zero) return;
            Unhook();
            EnsureHook();
        }

        public static void RemoveHook()
        {
            Unhook();
            hookWindow = IntPtr.Zero;
        }

        // Пока открыто окно макросов, его клавиши заняты: хук не должен слать эти
        // хоткеи главному окну, а сама клавиша временно снимается с регистрации
        public static void Suspend(int id, IntPtr ownerHandle)
        {
            suspended.Add(id);
            active.Remove(id);
            viaSystem.Remove(id);
            if (ownerHandle != IntPtr.Zero)
            {
                try { NativeMethods.UnregisterHotKey(ownerHandle, id); } catch { }
            }
        }

        public static void Resume(int id, IntPtr ownerHandle)
        {
            suspended.Remove(id);
            HotkeyDef d = Find(id);
            if (d == null || d.Current == Keys.None) return;
            active.Add(id);
            if (ownerHandle != IntPtr.Zero &&
                NativeMethods.RegisterHotKey(ownerHandle, id, 0, (uint)d.Current))
                viaSystem.Add(id);
        }

        // Одну и ту же клавишу приносят оба механизма: сначала сообщение хука,
        // потом WM_HOTKEY от системы. Дубль надо отбросить (иначе кликер
        // включится и тут же выключится), а отметку - погасить, чтобы она не
        // съела следующее реальное нажатие
        public static bool IsHookDuplicate(int id)
        {
            // Без хука системное сообщение не может быть дублем
            if (!IsHookActive) { hookHandled.Remove(id); return false; }

            int at;
            if (!hookHandled.TryGetValue(id, out at)) return false;
            hookHandled.Remove(id);
            return unchecked(Environment.TickCount - at) <= HOOK_DUPLICATE_MS;
        }

        private static void EnsureHook(IntPtr window)
        {
            hookWindow = window;
            EnsureHook();
        }

        private static void EnsureHook()
        {
            if (hookHandle != IntPtr.Zero) return;
            try
            {
                using (System.Diagnostics.Process proc = System.Diagnostics.Process.GetCurrentProcess())
                using (System.Diagnostics.ProcessModule module = proc.MainModule)
                {
                    hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc,
                        GetModuleHandle(module.ModuleName), 0);
                }
            }
            catch { hookHandle = IntPtr.Zero; }
        }

        private static void Unhook()
        {
            if (hookHandle == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(hookHandle); } catch { }
            hookHandle = IntPtr.Zero;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Внутри хука нельзя ни бросать исключения, ни тормозить: он на пути
            // всего ввода в системе
            try
            {
                if (nCode >= 0 && hookWindow != IntPtr.Zero)
                {
                    int msg = wParam.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        int vk = Marshal.ReadInt32(lParam);
                        for (int i = 0; i < All.Length; i++)
                        {
                            HotkeyDef d = All[i];
                            if (d.Current == Keys.None || (int)d.Current != vk) continue;
                            if (active.Contains(d.Id) && !suspended.Contains(d.Id))
                            {
                                hookHandled[d.Id] = Environment.TickCount;
                                NativeMethods.PostMessage(hookWindow, WM_HOTKEY_HOOK, (IntPtr)d.Id, IntPtr.Zero);
                            }
                            break;
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(hookHandle, nCode, wParam, lParam);
        }
    }
}
