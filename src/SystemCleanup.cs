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

        // Проверка на reparse point (junction / symlink) — защита от удаления не той папки
        private static bool IsReparsePoint(string path)
        {
            try
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.ReparsePoint) != 0;
            }
            catch { return false; }
        }

        // Есть ли что чистить (для показа размера в UI)
        public static long WindowsOldSizeBytes()
        {
            long total = 0;
            foreach (string d in OldDirs)
            {
                if (!Directory.Exists(d)) continue;
                if (IsReparsePoint(d)) continue; // symlink/junction — не считаем и не трогаем
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
                if (IsReparsePoint(d)) continue; // защита от symlink атаки
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
            // Защита: не чистить если это reparse point
            try { if (IsReparsePoint(dir)) return 0; } catch { }
            long size; int count;
            CleanupEngine.GetDirectorySize(dir, out size, out count);

            RunCmd("net stop wuauserv & net stop bits");
            long freed = CleanupEngine.CleanFolderContents(dir);
            RunCmd("net start wuauserv & net start bits");
            return freed / (1024 * 1024);
        }

        private static void RunCmd(string command)
        {
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                p = Process.Start(psi);
                if (p == null) return;
                // Асинхронное чтение обоих потоков — защита от deadlock при переполнении буфера stderr
                string stdout = "", stderr = "";
                var outDone = new System.Threading.ManualResetEvent(false);
                var errDone = new System.Threading.ManualResetEvent(false);
                p.OutputDataReceived += (s, e) => { if (e.Data == null) outDone.Set(); else stdout += e.Data + "\n"; };
                p.ErrorDataReceived += (s, e) => { if (e.Data == null) errDone.Set(); else stderr += e.Data + "\n"; };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                bool exited = p.WaitForExit(120000);
                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    p.WaitForExit(5000);
                }
                // Дождаться дочитывания потоков
                outDone.WaitOne(3000);
                errDone.WaitOne(3000);
            }
            catch { }
            finally
            {
                try { if (p != null) p.Dispose(); } catch { }
            }
        }
    }
}
