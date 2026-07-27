using System;
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
            foreach (HotkeyDef d in All)
                NativeMethods.RegisterHotKey(handle, d.Id, 0, (uint)d.Current);
        }

        public static void UnregisterAll(IntPtr handle)
        {
            foreach (HotkeyDef d in All)
                NativeMethods.UnregisterHotKey(handle, d.Id);
        }

        // Переназначение: снимает старую регистрацию, вешает новую, пишет в конфиг
        public static void Set(IntPtr handle, int id, Keys key)
        {
            HotkeyDef d = Find(id);
            if (d == null) return;
            NativeMethods.UnregisterHotKey(handle, d.Id);
            d.Current = key;
            NativeMethods.RegisterHotKey(handle, d.Id, 0, (uint)key);
            AppConfig.Set(d.ConfigKey, key.ToString());
        }
    }
}
