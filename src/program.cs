using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace DeepTools
{
    public class Program
    {
        // Держим мьютекс живым всё время работы программы (иначе GC освободит
        // его и одно-экземплярность сломается)
        private static Mutex _singleInstance;

        // Аргумент, с которым процесс запускается после смены темы/языка:
        // новый экземпляр ждёт, пока старый освободит мьютекс и завершится.
        private const string RestartFlag = "--restart";

        [STAThread]
        static void Main(string[] args)
        {
            // Исправление "пиксельного" интерфейса: включаем High DPI и сглаживание
            // SetProcessDPIAware делает приложение DPI-aware - текст и контролы не растягиваются билинейно
            try
            {
                // Попытка включить Per-Monitor V2 через SetProcessDpiAwareness (Win8.1+) или SetProcessDPIAware (Vista+)
                // Делаем ДО любых UI-операций и до EmbeddedAssemblies
                TryEnableDpiAwareness();
            }
            catch { }

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
            Theme.ApplyNamed(AppConfig.Get("theme", "dark"));

            // Самый первый запуск: спрашиваем язык явно, а не угадываем по локали -
            // иначе человек с "чужим" языком системы даже не поймёт, где его сменить.
            // Заодно помечаем, что надо показать мастер первого запуска
            string savedLang = AppConfig.Get("language", "");
            if (savedLang == "")
            {
                using (var picker = new LanguagePickerForm())
                    picker.ShowDialog();

                savedLang = AppConfig.Get("language", "");
                if (savedLang == "") // закрыл окно, не выбрав - fallback на локаль Windows
                {
                    bool systemIsRussian = System.Globalization.CultureInfo
                        .CurrentUICulture.TwoLetterISOLanguageName == "ru";
                    savedLang = systemIsRussian ? "ru" : "en";
                    AppConfig.Set("language", savedLang);
                }
                AppConfig.SetBool("wizard_pending", true);
            }
            Lang.IsEn = savedLang == "en";

            // Первый запуск: без явного согласия с политикой конфиденциальности
            // программа не стартует и телеметрия не отправляется. Экран согласия
            // показывается до главного окна и до любых сетевых запросов.
            if (!PrivacyPolicy.EnsureAccepted())
                return;

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

            // Один экземпляр DeepTools. Проверяем здесь, уже ПОСЛЕ возможного
            // перезапуска с правами админа - иначе временный процесс без прав
            // занял бы мьютекс и админский экземпляр решил бы, что он второй.
            bool createdNew = false;
            bool restarting = false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, RestartFlag, StringComparison.OrdinalIgnoreCase))
                {
                    restarting = true;
                    break;
                }
            }

            // При перезапуске старый процесс ещё несколько мгновений держит
            // мьютекс. Ждём его завершения, иначе новый экземпляр решит, что
            // DeepTools уже запущен, и выйдет - приложение просто исчезнет.
            int attempts = restarting ? 40 : 1; // до 10 секунд
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                _singleInstance = new Mutex(true, "DeepTools_SingleInstance_dep1xar", out createdNew);
                if (createdNew) break;

                _singleInstance.Dispose();
                _singleInstance = null;
                if (attempt + 1 < attempts) Thread.Sleep(250);
            }

            if (!createdNew)
            {
                // Уже запущено - будим то окно (выйдет из трея) и выходим
                NativeMethods.PostMessage((IntPtr)NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SHOW_DEEPTOOLS, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            try { Telemetry.Start(); } catch { }
            try { Application.Run(new MainForm(isAdmin)); }
            finally
            {
                // Сначала отпускаем мьютекс: при перезапуске новый экземпляр
                // ждёт именно его, а синхронная отправка exit может занимать
                // несколько секунд и не должна задерживать старт.
                try { if (_singleInstance != null) { _singleInstance.ReleaseMutex(); _singleInstance.Dispose(); _singleInstance = null; } } catch { }
                try { Telemetry.Stop(); } catch { }
            }
        }

        internal static void TryEnableDpiAwareness()
        {
            try
            {
                // Windows 8.1+ : SetProcessDpiAwareness (shcore.dll)
                IntPtr shcore = NativeMethods.LoadLibrary("shcore.dll");
                if (shcore != IntPtr.Zero)
                {
                    IntPtr proc = NativeMethods.GetProcAddress(shcore, "SetProcessDpiAwareness");
                    if (proc != IntPtr.Zero)
                    {
                        // 2 = PROCESS_PER_MONITOR_DPI_AWARE (достаточно; 1 = SYSTEM_AWARE)
                        var del = (NativeMethods.SetProcessDpiAwarenessDelegate)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(proc, typeof(NativeMethods.SetProcessDpiAwarenessDelegate));
                        del(2);
                        return;
                    }
                }
            }
            catch { }
            try { NativeMethods.SetProcessDPIAware(); } catch { }
        }

        private static void LogCrash(Exception ex)
        {
            if (ex == null) return;

            // Анонимная телеметрия: модуль + тип исключения + HRESULT.
            // Текст исключения и стек на сервер не отправляются.
            try { Telemetry.ReportError("Unhandled", ex); } catch { }

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

        // Перезапуск текущего exe после смены темы/языка.
        // Новый процесс получает --restart и подождёт, пока этот завершится.
        public static bool TryRestart(out string error)
        {
            error = "";
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(Application.ExecutablePath);
                startInfo.Arguments = RestartFlag;
                startInfo.UseShellExecute = true;
                if (!IsRunningAsAdmin())
                    startInfo.Verb = "runas";
                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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
