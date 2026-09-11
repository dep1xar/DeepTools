using System;
using System.Diagnostics;
using System.IO;

namespace DeepTools
{
    // Очистка тяжёлых системных остатков: папка Windows.old (предыдущая установка
    // Windows) и кэш загруженных обновлений. Часто это десятки гигабайт.
    // Требует прав администратора (у программы они есть).
    public static class SystemCleanup
    {
        private static readonly string[] OldDirs =
        {
            @"C:\Windows.old",
            @"C:\$Windows.~BT",
            @"C:\$Windows.~WS"
        };

        // Есть ли что чистить (для показа размера в UI)
        public static long WindowsOldSizeBytes()
        {
            long total = 0;
            foreach (string d in OldDirs)
            {
                if (!Directory.Exists(d)) continue;
                long size; int count;
                CleanupEngine.GetDirectorySize(d, out size, out count);
                total += size;
            }
            return total;
        }

        // Удаляет Windows.old и связанные папки. Возвращает освобождённые МБ.
        public static long CleanWindowsOld()
        {
            long before = WindowsOldSizeBytes();
            foreach (string d in OldDirs)
            {
                if (!Directory.Exists(d)) continue;
                // Папки принадлежат TrustedInstaller - берём владение и права, затем удаляем
                string sid = "*S-1-5-32-544"; // группа Администраторы, независимо от локали
                RunCmd("takeown /F \"" + d + "\" /R /A /D Y & " +
                       "icacls \"" + d + "\" /grant " + sid + ":F /T /C & " +
                       "rd /s /q \"" + d + "\"");
            }
            long after = WindowsOldSizeBytes();
            return (before - after) / (1024 * 1024);
        }

        // Чистит кэш загруженных обновлений Windows (SoftwareDistribution\Download)
        public static long CleanUpdateCache()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SoftwareDistribution\\Download");
            long size; int count;
            CleanupEngine.GetDirectorySize(dir, out size, out count);

            RunCmd("net stop wuauserv & net stop bits");
            long freed = CleanupEngine.CleanFolderContents(dir);
            RunCmd("net start wuauserv & net start bits");
            return freed / (1024 * 1024);
        }

        private static void RunCmd(string command)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return;
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(120000); // takeown/rd на больших папках долгие
                }
            }
            catch { }
        }
    }
}
