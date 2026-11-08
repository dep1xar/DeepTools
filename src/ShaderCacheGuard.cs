using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace DeepTools
{
    // Авто-очистка кэша шейдеров при смене версии драйвера GPU. После обновления
    // драйвера старый кэш «протухает» и вызывает фризы/артефакты - его надо сбросить.
    // Игры пересоберут кэш сами (первый запуск чуть дольше).
    public static class ShaderCacheGuard
    {
        public static long LastFreedMb { get; private set; }

        // Папки кэшей шейдеров GPU/DirectX, которые чистим при смене драйвера
        private static List<string> CachePaths()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return new List<string>
            {
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, "NVIDIA\\DXCache"),
                Path.Combine(local, "NVIDIA\\GLCache"),
                Path.Combine(programData, "NVIDIA Corporation\\NV_Cache"),
                Path.Combine(local, "AMD\\DxCache"),
                Path.Combine(local, "AMD\\DX9Cache"),
                Path.Combine(local, "AMD\\GLCache"),
            };
        }

        // Версия драйвера всех видеоадаптеров одной строкой. Меняется при обновлении.
        public static string GetGpuDriverVersion()
        {
            var parts = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT DriverVersion FROM Win32_VideoController"))
                foreach (ManagementObject mo in searcher.Get())
                {
                    object v = mo["DriverVersion"];
                    if (v != null) parts.Add(v.ToString());
                }
            }
            catch { }
            return string.Join("|", parts.ToArray());
        }

        // Почистить кэши шейдеров прямо сейчас. Возвращает освобождённые МБ.
        public static long CleanNow()
        {
            long freed = 0;
            foreach (string path in CachePaths())
            {
                try { freed += CleanupEngine.CleanFolderContents(path); }
                catch { }
            }
            LastFreedMb = freed / (1024 * 1024);
            return LastFreedMb;
        }

        // Вызывается на старте, если фича включена. Чистит кэш только если версия
        // драйвера изменилась с прошлого запуска. Возвращает true, если чистили.
        public static bool CheckAndClean()
        {
            string current = GetGpuDriverVersion();
            if (string.IsNullOrEmpty(current)) return false;

            string stored = AppConfig.Get("gpu_driver_ver", "");
            // Первый раз просто запоминаем базовую версию, ничего не трогаем
            if (stored == "")
            {
                AppConfig.Set("gpu_driver_ver", current);
                return false;
            }

            if (current != stored)
            {
                CleanNow();
                AppConfig.Set("gpu_driver_ver", current);
                return true;
            }
            return false;
        }
    }
}
