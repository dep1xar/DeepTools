using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace DeepTools
{
    // Страж автозагрузки: раз в минуту сравнивает текущие записи автозагрузки
    // с сохранённым слепком. Если программа втихую добавила себя - уведомление из трея.
    // Слепок хранится в конфиге одной строкой через "|"
    public static class StartupGuard
    {
        private static System.Windows.Forms.Timer timer;

        public static bool Enabled
        {
            get { return AppConfig.GetBool("startup_guard", true); }
            set { AppConfig.SetBool("startup_guard", value); }
        }

        public static void Start()
        {
            // Первый запуск: молча запоминаем текущее состояние как норму
            if (AppConfig.Get("startup_baseline", "") == "")
            {
                SaveBaseline(Snapshot());
            }

            timer = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            timer.Tick += (s, e) => { if (Enabled) Check(); };
            timer.Start();
        }

        private static void SaveBaseline(List<string> items)
        {
            AppConfig.Set("startup_baseline", string.Join("|", items.ToArray()));
        }

        public static void Check()
        {
            List<string> current = Snapshot();
            string savedRaw = AppConfig.Get("startup_baseline", "");
            var known = new HashSet<string>(savedRaw.Split('|'));

            var added = new List<string>();
            for (int i = 0; i < current.Count; i++)
            {
                if (!known.Contains(current[i])) added.Add(current[i]);
            }

            if (added.Count > 0)
            {
                string names = string.Join(", ", added.ToArray());
                TrayNotify.Warn(
                    Lang.T("Новая автозагрузка", "New startup entry"),
                    names + Lang.T(
                        " добавилась в автозагрузку Windows. Если это не ты - отключи её на вкладке \"Автозагрузка\".",
                        " was added to Windows startup. If this was not you, disable it on the Startup tab."));
            }

            // Слепок обновляется всегда: об исчезнувших записях не предупреждаем,
            // а о новых - только один раз
            SaveBaseline(current);
        }

        // Собирает имена всех записей автозагрузки: реестр HKCU/HKLM + папки автозагрузки
        private static List<string> Snapshot()
        {
            var items = new List<string>();

            CollectRunKey(items, Registry.CurrentUser);
            CollectRunKey(items, Registry.LocalMachine);
            CollectFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            CollectFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));

            items.Sort(StringComparer.OrdinalIgnoreCase);
            return items;
        }

        private static void CollectRunKey(List<string> items, RegistryKey root)
        {
            RegistryKey key = null;
            try
            {
                key = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run");
                if (key == null) return;
                string[] names = key.GetValueNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].Length > 0) items.Add(names[i].Replace("|", "_"));
                }
            }
            catch { }
            finally
            {
                if (key != null) key.Close();
            }
        }

        private static void CollectFolder(List<string> items, string folder)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
                string[] files = Directory.GetFiles(folder);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    if (string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    items.Add(name.Replace("|", "_"));
                }
            }
            catch { }
        }
    }
}
