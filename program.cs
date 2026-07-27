using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace DeepTools
{
    public class Program
    {
        [STAThread]
        static void Main()
        {
            // Должно быть до первого обращения к встроенным сборкам (датчики температур)
            EmbeddedAssemblies.Install();

            // Необработанные исключения пишем в %AppData%\DeepTools\crash.log -
            // иначе при падении у пользователя нет ничего, что можно прислать
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => LogCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig.Load();

            // При первом запуске выбираем язык по языку Windows:
            // русская система - русский, любая другая - английский
            string savedLang = AppConfig.Get("language", "");
            if (savedLang == "")
            {
                bool systemIsRussian = System.Globalization.CultureInfo
                    .CurrentUICulture.TwoLetterISOLanguageName == "ru";
                savedLang = systemIsRussian ? "ru" : "en";
                AppConfig.Set("language", savedLang);
            }
            Lang.IsEn = savedLang == "en";
            Theme.Apply(AppConfig.Get("theme", "dark") == "light");

            bool isAdmin = IsRunningAsAdmin();

            // exe собран с манифестом requireAdministrator, поэтому Windows сама показывает UAC.
            // Этот блок - подстраховка на случай запуска в обход манифеста
            if (!isAdmin)
            {
                if (TryRelaunchAsAdmin())
                {
                    return; // текущий процесс завершится, новый запустится с правами
                }
            }

            Application.Run(new MainForm(isAdmin));
        }

        private static void LogCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] DeepTools " +
                    Application.ProductVersion + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }

            try
            {
                MessageBox.Show(
                    Lang.T("Произошла ошибка: ", "An error occurred: ") + ex.Message + Environment.NewLine +
                    Lang.T("Подробности сохранены в crash.log (папка DeepTools в AppData).",
                           "Details saved to crash.log (DeepTools folder in AppData)."),
                    "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
        }

        // Проверка, запущено ли приложение от имени администратора
        public static bool IsRunningAsAdmin()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        // Перезапуск текущего exe с запросом прав администратора через UAC
        public static bool TryRelaunchAsAdmin()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                ProcessStartInfo startInfo = new ProcessStartInfo(exePath);
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
                Process.Start(startInfo);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Пользователь нажал "Нет" в окне UAC - просто продолжаем без прав
                return false;
            }
        }
    }
}
