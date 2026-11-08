using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    // Автозапуск DeepTools вместе с Windows.
    // Через Планировщик задач (schtasks), а НЕ через ключ Run: программе нужны права
    // администратора (requireAdministrator), а admin-приложения Windows не запускает
    // из ключа Run при входе (там нельзя показать UAC). Задача с флагом /RL HIGHEST
    // и триггером ONLOGON стартует программу уже с правами и без окна UAC.
    //
    // Задача создаётся из XML, а не флажками schtasks: только так можно снять
    // DisallowStartIfOnBatteries, из-за которого задача «на вход в систему»
    // молча не срабатывает, и убрать кавычки из <Command>.
    public static class AutoStart
    {
        private const string TaskName = "DeepTools";
        private const string OldRunKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";

        public static bool Enabled
        {
            get { return RunSchtasks("/Query /TN \"" + TaskName + "\"") == 0; }
            set
            {
                CleanupOldRunEntry();
                if (value) CreateTask();
                else RunSchtasks("/Delete /TN \"" + TaskName + "\" /F");
            }
        }

        // Путь, который прописываем в задачу. Если приложение запущено из
        // DeepTools_old.exe (так сборка подменяет работающий exe), этот файл
        // потом удаляется - в автозагрузку его прописывать нельзя
        private static string StartupExePath()
        {
            string exe = Application.ExecutablePath;
            try
            {
                string dir = Path.GetDirectoryName(exe);
                if (Path.GetFileName(exe).IndexOf("old", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string canonical = Path.Combine(dir, "DeepTools.exe");
                    if (File.Exists(canonical)) return canonical;
                }
            }
            catch { }
            return exe;
        }

        private static void CreateTask()
        {
            string exe = StartupExePath();
            string xml = BuildTaskXml(exe);
            string path = Path.Combine(Path.GetTempPath(), "deeptools_task_" + Guid.NewGuid().ToString("N") + ".xml");

            bool created = false;
            try
            {
                // Планировщик принимает XML только в Unicode с BOM
                File.WriteAllText(path, xml, Encoding.Unicode);
                created = RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + path + "\" /F") == 0;
            }
            catch { created = false; }
            finally
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }

            // Запасной путь: старый способ флажками, если XML почему-то отвергнут
            if (!created)
            {
                RunSchtasks("/Create /TN \"" + TaskName + "\" /TR \"\\\"" + exe + "\\\"\" /SC ONLOGON /RL HIGHEST /F");
            }
        }

        // Проверка и починка уже созданной задачи. Старые версии создавали её
        // флажками schtasks, а это оставляет DisallowStartIfOnBatteries и
        // StopIfGoingOnBatteries включёнными - задача «при входе в систему»
        // на многих машинах из-за этого молча не срабатывает. Файл задачи
        // читается без прав администратора, так что проверка дешёвая
        public static void EnsureHealthy()
        {
            try
            {
                if (RunSchtasks("/Query /TN \"" + TaskName + "\"") != 0) return; // автозапуск выключен

                string file = TaskFile();
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;

                string xml = File.ReadAllText(file, Encoding.Unicode);
                bool batteryBlock = xml.IndexOf("DisallowStartIfOnBatteries>true", StringComparison.OrdinalIgnoreCase) >= 0;
                bool quotedCommand = xml.IndexOf("<Command>\"", StringComparison.OrdinalIgnoreCase) >= 0;
                bool wrongPath = xml.IndexOf(StartupExePath(), StringComparison.OrdinalIgnoreCase) < 0;

                if (batteryBlock || quotedCommand || wrongPath) CreateTask();
            }
            catch { }
        }

        private static string TaskFile()
        {
            try
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                                    "System32\\Tasks\\" + TaskName);
            }
            catch { return ""; }
        }

        private static string BuildTaskXml(string exe)
        {
            string sid = "";
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    sid = id.User == null ? "" : id.User.Value;
            }
            catch { }

            string cmd = exe.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

            // Principal по UserId + InteractiveToken: задача должна запускаться
            // в сеансе пользователя (иначе окна не будет видно), но с HighestAvailable
            string principal = string.IsNullOrEmpty(sid)
                ? "<Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>"
                : "<Principal id=\"Author\"><UserId>" + sid + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal>";

            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n"
                + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n"
                + "  <RegistrationInfo><Description>DeepTools autostart</Description></RegistrationInfo>\r\n"
                + "  <Triggers>\r\n"
                + "    <LogonTrigger><Enabled>true</Enabled><Delay>PT20S</Delay></LogonTrigger>\r\n"
                + "  </Triggers>\r\n"
                + "  <Principals>" + principal + "</Principals>\r\n"
                + "  <Settings>\r\n"
                + "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n"
                // Главное: без этого задача «при входе» на многих системах не стартует
                + "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n"
                + "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n"
                + "    <AllowHardTerminate>true</AllowHardTerminate>\r\n"
                + "    <StartWhenAvailable>true</StartWhenAvailable>\r\n"
                + "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n"
                + "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n"
                + "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n"
                + "    <Enabled>true</Enabled>\r\n"
                + "    <Hidden>false</Hidden>\r\n"
                + "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n"
                // Без ограничения по времени: иначе планировщик убивает процесс через 72 часа
                + "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n"
                + "    <Priority>4</Priority>\r\n"
                + "  </Settings>\r\n"
                + "  <Actions Context=\"Author\"><Exec><Command>" + cmd + "</Command></Exec></Actions>\r\n"
                + "</Task>\r\n";
        }

        // Убираем старую запись из ключа Run (осталась от версий до 1.3.x, всё равно не работала)
        private static void CleanupOldRunEntry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(OldRunKey, true))
                {
                    if (key != null && key.GetValue(TaskName) != null) key.DeleteValue(TaskName, false);
                }
            }
            catch { }
        }

        private static int RunSchtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(8000);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }

    // Быстрые действия питания и системы
    public static class PowerTools
    {
        // Перезагрузка сразу в прошивку (BIOS/UEFI)
        public static void RestartToFirmware()
        {
            try { Process.Start("shutdown.exe", "/r /fw /t 0"); } catch { }
        }

        // Быстрый запуск Windows (гибридное завершение). HKLM - нужны права админа
        public static bool FastStartupEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                        "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power"))
                    {
                        if (key == null) return true;
                        object v = key.GetValue("HiberbootEnabled");
                        return v == null || Convert.ToInt32(v) != 0;
                    }
                }
                catch { return true; }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(
                        "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power"))
                    {
                        if (key != null) key.SetValue("HiberbootEnabled", value ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
                catch { }
            }
        }

        public static void RestartExplorer()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("explorer");
                for (int i = 0; i < procs.Length; i++)
                {
                    try { procs[i].Kill(); } catch { }
                }
                System.Threading.Thread.Sleep(500);
                Process.Start("explorer.exe");
            }
            catch { }
        }
    }
}
