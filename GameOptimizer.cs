using System;
using Microsoft.Win32;

namespace DeepTools
{
    // Реальные игровые твики через реестр (всё в HKCU, откатывается обратно).
    // 1) Game DVR: фоновая запись Xbox Game Bar постоянно захватывает кадры
    //    и ест FPS даже когда пользователь ей не пользуется.
    // 2) UserGpuPreferences: на ноутбуках с двумя GPU Windows может запустить игру
    //    на встроенной графике - закрепление за дискретной картой даёт разницу в разы.
    public static class GameOptimizer
    {
        public static bool IsDvrDisabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore"))
                {
                    if (k == null) return false;
                    object v = k.GetValue("GameDVR_Enabled");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static bool SetDvrDisabled(bool disabled)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore"))
                {
                    if (k != null) k.SetValue("GameDVR_Enabled", disabled ? 0 : 1, RegistryValueKind.DWord);
                }
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\GameDVR"))
                {
                    if (k != null) k.SetValue("AppCaptureEnabled", disabled ? 0 : 1, RegistryValueKind.DWord);
                }
                return true;
            }
            catch { return false; }
        }

        // Тот же ключ, что и «Параметры -> Дисплей -> Графика»: имя значения - полный
        // путь к exe, GpuPreference=2 - профиль «Высокая производительность»
        private const string GpuPrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

        public static bool HasGpuPreference(string exePath)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(GpuPrefKey))
                {
                    if (k == null) return false;
                    string v = k.GetValue(exePath) as string;
                    return v != null && v.Contains("GpuPreference=2");
                }
            }
            catch { return false; }
        }

        public static bool SetGpuPreference(string exePath)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(GpuPrefKey))
                {
                    if (k == null) return false;
                    k.SetValue(exePath, "GpuPreference=2;");
                    return true;
                }
            }
            catch { return false; }
        }
    }
}
