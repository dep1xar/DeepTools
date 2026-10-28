using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace DeepTools
{
    // Полное удаление Яндекс.Браузера.
    //
    // Один штатный деинсталлятор его не убирает: остаются профиль и кэш в
    // AppData/LocalAppData, записи в реестре, задачи планировщика и ярлыки.
    // Здесь собираем ВСЁ, что нашлось, показываем списком и сносим одним
    // нажатием: процессы -> задачи -> службы -> реестр -> папки -> ярлыки.
    //
    // Главный процесс браузера — browser.exe, поэтому его добиваем первым,
    // иначе файлы заняты и папка не удалится.
    public class YandexItem
    {
        // process | folder | registry | runvalue | task | service | shortcut
        public string Kind;
        public string Title;
        public string Path;          // папка/ярлык/имя процесса/задачи/службы
        public long Size;
        public RegistryHive Hive;    // для registry / runvalue
        public string SubKey;        // для registry / runvalue
        public string ValueName;     // для runvalue

        public bool IsProcess { get { return Kind == "process"; } }
        public bool IsRegistry { get { return Kind == "registry"; } }
        public bool IsRunValue { get { return Kind == "runvalue"; } }
    }

    public static class YandexCleaner
    {
        // Имена процессов браузера. browser.exe — основной; остальные —
        // прокси-процесс, старые сборки и апдейтер
        private static readonly string[] ProcessNames =
        {
            "browser", "browser_proxy", "yandexbrowser", "ybrowser",
            "yandexbrowserupdate", "yupdate", "service_update"
        };

        // Явно «яндексовые» имена: им доверяем без проверки пути
        private static readonly string[] YandexOnlyNames =
        {
            "browser_proxy", "yandexbrowser", "ybrowser", "yandexbrowserupdate"
        };

        public static string FormatSize(long bytes)
        {
            return CleanupEngine.FormatSize(bytes);
        }

        // ---- Поиск ----

        public static List<YandexItem> Scan(bool includeOtherProducts, Action<string> progress)
        {
            var items = new List<YandexItem>();

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            // 1. Процессы
            Report(progress, Lang.T("Ищем запущенные процессы…", "Looking for running processes…"));
            CollectProcesses(items);

            // 2. Папки
            Report(progress, Lang.T("Проверяем папки…", "Checking folders…"));
            var folders = new List<string>
            {
                Path.Combine(local, "Yandex", "YandexBrowser"),
                Path.Combine(local, "YandexBrowser"),
                Path.Combine(local, "Programs", "Yandex", "YandexBrowser"),
                Path.Combine(roaming, "Yandex", "YandexBrowser"),
                Path.Combine(roaming, "YandexBrowser"),
                Path.Combine(pf, "Yandex", "YandexBrowser"),
                Path.Combine(pf86, "Yandex", "YandexBrowser"),
                Path.Combine(common, "Yandex", "YandexBrowser")
            };
            if (includeOtherProducts)
            {
                folders.Add(Path.Combine(local, "Yandex"));
                folders.Add(Path.Combine(roaming, "Yandex"));
                folders.Add(Path.Combine(pf, "Yandex"));
                folders.Add(Path.Combine(pf86, "Yandex"));
                folders.Add(Path.Combine(common, "Yandex"));
            }
            CollectFolders(items, folders);

            // 3. Записи реестра
            Report(progress, Lang.T("Проверяем реестр…", "Checking the registry…"));
            CollectRegistry(items, includeOtherProducts);

            // 4. Задачи планировщика
            Report(progress, Lang.T("Проверяем задачи планировщика…", "Checking scheduled tasks…"));
            CollectTasks(items);

            // 5. Службы
            Report(progress, Lang.T("Проверяем службы…", "Checking services…"));
            CollectServices(items);

            // 6. Ярлыки
            Report(progress, Lang.T("Проверяем ярлыки…", "Checking shortcuts…"));
            CollectShortcuts(items);

            Report(progress, Lang.T("Готово", "Done"));
            return items;
        }

        private static void Report(Action<string> progress, string text)
        {
            if (progress != null) { try { progress(text); } catch { } }
        }

        private static bool LooksLikeYandex(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string l = s.ToLowerInvariant();
            return l.Contains("yandex") || l.Contains("яндекс");
        }

        private static void CollectProcesses(List<YandexItem> items)
        {
            Process[] all;
            try { all = Process.GetProcesses(); }
            catch { return; }

            var seen = new HashSet<int>();
            for (int i = 0; i < all.Length; i++)
            {
                Process p = all[i];
                try
                {
                    string name = (p.ProcessName ?? "").ToLowerInvariant();
                    bool named = false;
                    for (int n = 0; n < ProcessNames.Length; n++)
                        if (name == ProcessNames[n]) { named = true; break; }
                    if (!named) { p.Dispose(); continue; }

                    // browser.exe — слишком общее имя: пускаем только если путь
                    // реально лежит в папке Яндекса
                    string path = "";
                    try { path = p.MainModule != null ? p.MainModule.FileName : ""; }
                    catch { path = ""; }

                    bool trusted = false;
                    for (int n = 0; n < YandexOnlyNames.Length; n++)
                        if (name == YandexOnlyNames[n]) { trusted = true; break; }

                    if (!trusted)
                    {
                        if (string.IsNullOrEmpty(path) || !LooksLikeYandex(path))
                        {
                            p.Dispose();
                            continue;
                        }
                    }

                    if (!seen.Add(p.Id)) { p.Dispose(); continue; }

                    items.Add(new YandexItem
                    {
                        Kind = "process",
                        Title = p.ProcessName + ".exe  (PID " + p.Id + ")",
                        // PID вместе с именем: к моменту удаления процесс мог
                        // завершиться, и PID достался бы чужому — сверяем имя
                        Path = p.Id + "|" + name,
                        Size = 0
                    });
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }

        private static void CollectFolders(List<YandexItem> items, List<string> candidates)
        {
            // Родительские пути обрабатываем первыми — иначе вложенная папка
            // попадёт в список раньше родителя и её размер посчитается дважды
            var ordered = new List<string>(candidates);
            ordered.Sort(delegate(string a, string b)
            {
                int la = a == null ? 0 : a.Length;
                int lb = b == null ? 0 : b.Length;
                return la.CompareTo(lb);
            });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < ordered.Count; i++)
            {
                string dir = ordered[i];
                if (string.IsNullOrEmpty(dir)) continue;
                if (!Directory.Exists(dir)) continue;
                if (!seen.Add(dir)) continue;
                // Родительская папка уже в списке — вложенную не дублируем
                if (AlreadyCovered(seen, dir))
                {
                    seen.Remove(dir);
                    continue;
                }

                long size = 0;
                int count = 0;
                try { CleanupEngine.GetDirectorySize(dir, out size, out count); }
                catch { }

                items.Add(new YandexItem
                {
                    Kind = "folder",
                    Title = dir + "  ·  " + FormatSize(size),
                    Path = dir,
                    Size = size
                });
            }
        }

        private static bool AlreadyCovered(HashSet<string> seen, string dir)
        {
            foreach (string s in seen)
            {
                if (string.Equals(s, dir, StringComparison.OrdinalIgnoreCase)) continue;
                if (dir.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static void CollectRegistry(List<YandexItem> items, bool includeOtherProducts)
        {
            // Основные ключи браузера
            AddKey(items, RegistryHive.CurrentUser, @"Software\Yandex\YandexBrowser");
            AddKey(items, RegistryHive.LocalMachine, @"Software\Yandex\YandexBrowser");
            AddKey(items, RegistryHive.LocalMachine, @"Software\Wow6432Node\Yandex\YandexBrowser");
            AddKey(items, RegistryHive.CurrentUser, @"Software\Classes\yandexbrowser");

            if (includeOtherProducts)
            {
                AddKey(items, RegistryHive.CurrentUser, @"Software\Yandex");
                AddKey(items, RegistryHive.LocalMachine, @"Software\Yandex");
                AddKey(items, RegistryHive.LocalMachine, @"Software\Wow6432Node\Yandex");
            }

            // Записи в «Установка и удаление программ»
            CollectUninstallEntries(items, RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            CollectUninstallEntries(items, RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            CollectUninstallEntries(items, RegistryHive.LocalMachine,
                @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall");

            // Автозагрузка
            CollectRunValues(items, RegistryHive.CurrentUser,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            CollectRunValues(items, RegistryHive.LocalMachine,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            CollectRunValues(items, RegistryHive.LocalMachine,
                @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run");
        }

        private static RegistryKey OpenBase(RegistryHive hive)
        {
            switch (hive)
            {
                case RegistryHive.CurrentUser: return Registry.CurrentUser;
                case RegistryHive.LocalMachine: return Registry.LocalMachine;
                default: return null;
            }
        }

        private static string HiveName(RegistryHive hive)
        {
            return hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
        }

        private static void AddKey(List<YandexItem> items, RegistryHive hive, string subKey)
        {
            RegistryKey root = OpenBase(hive);
            if (root == null) return;
            try
            {
                using (RegistryKey k = root.OpenSubKey(subKey))
                {
                    if (k == null) return;
                }
                items.Add(new YandexItem
                {
                    Kind = "registry",
                    Title = HiveName(hive) + @"\" + subKey,
                    Hive = hive,
                    SubKey = subKey,
                    Size = 0
                });
            }
            catch { }
        }

        private static void CollectUninstallEntries(List<YandexItem> items, RegistryHive hive, string uninstallSub)
        {
            RegistryKey root = OpenBase(hive);
            if (root == null) return;
            try
            {
                using (RegistryKey key = root.OpenSubKey(uninstallSub))
                {
                    if (key == null) return;
                    string[] subs = key.GetSubKeyNames();
                    for (int i = 0; i < subs.Length; i++)
                    {
                        try
                        {
                            using (RegistryKey app = key.OpenSubKey(subs[i]))
                            {
                                if (app == null) continue;
                                string display = app.GetValue("DisplayName") as string;
                                if (!LooksLikeYandex(display)) continue;

                                string full = uninstallSub + @"\" + subs[i];
                                items.Add(new YandexItem
                                {
                                    Kind = "registry",
                                    Title = HiveName(hive) + @"\" + full + "  (" + display + ")",
                                    Hive = hive,
                                    SubKey = full,
                                    Size = 0
                                });
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private static void CollectRunValues(List<YandexItem> items, RegistryHive hive, string runSub)
        {
            RegistryKey root = OpenBase(hive);
            if (root == null) return;
            try
            {
                using (RegistryKey key = root.OpenSubKey(runSub))
                {
                    if (key == null) return;
                    string[] names = key.GetValueNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        string data = "";
                        try { data = key.GetValue(names[i]) as string; } catch { }
                        if (!LooksLikeYandex(names[i]) && !LooksLikeYandex(data)) continue;

                        items.Add(new YandexItem
                        {
                            Kind = "runvalue",
                            Title = HiveName(hive) + @"\" + runSub + @"  →  " + names[i],
                            Hive = hive,
                            SubKey = runSub,
                            ValueName = names[i],
                            Size = 0
                        });
                    }
                }
            }
            catch { }
        }

        private static void CollectTasks(List<YandexItem> items)
        {
            try
            {
                string tasksDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\Tasks");
                if (!Directory.Exists(tasksDir)) return;

                string[] files = Directory.GetFiles(tasksDir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    string name = Path.GetFileName(file);
                    if (!LooksLikeYandex(name)) continue;

                    // Имя задачи = путь относительно папки Tasks, с обратными слэшами
                    string rel = file.Substring(tasksDir.Length).TrimStart('\\');
                    items.Add(new YandexItem
                    {
                        Kind = "task",
                        Title = Lang.T("Задача: ", "Task: ") + rel,
                        Path = rel,
                        Size = 0
                    });
                }
            }
            catch { }
        }

        private static void CollectServices(List<YandexItem> items)
        {
            try
            {
                using (RegistryKey services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
                {
                    if (services == null) return;
                    string[] names = services.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (!LooksLikeYandex(names[i])) continue;
                        items.Add(new YandexItem
                        {
                            Kind = "service",
                            Title = Lang.T("Служба: ", "Service: ") + names[i],
                            Path = names[i],
                            Size = 0
                        });
                    }
                }
            }
            catch { }
        }

        private static void CollectShortcuts(List<YandexItem> items)
        {
            var roots = new List<string>();
            try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)); } catch { }
            try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)); } catch { }
            try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.Programs)); } catch { }
            try { roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)); } catch { }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < roots.Count; r++)
            {
                string root = roots[r];
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    string[] links = Directory.GetFiles(root, "*.lnk", SearchOption.AllDirectories);
                    for (int i = 0; i < links.Length; i++)
                    {
                        string file = links[i];
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!LooksLikeYandex(name)) continue;
                        if (!seen.Add(file)) continue;

                        items.Add(new YandexItem
                        {
                            Kind = "shortcut",
                            Title = Lang.T("Ярлык: ", "Shortcut: ") + file,
                            Path = file,
                            Size = 0
                        });
                    }
                }
                catch { }
            }
        }

        // ---- Удаление ----

        // Возвращает сколько байт освобождено
        public static long Remove(List<YandexItem> items, Action<string> progress)
        {
            long freed = 0;

            // 1. Процессы — первыми, иначе папки заняты
            Report(progress, Lang.T("Закрываем процессы…", "Closing processes…"));
            for (int i = 0; i < items.Count; i++)
            {
                YandexItem it = items[i];
                if (!it.IsProcess) continue;
                int sep = it.Path.IndexOf('|');
                if (sep <= 0) continue;
                int pid;
                if (!int.TryParse(it.Path.Substring(0, sep), out pid)) continue;
                string expected = it.Path.Substring(sep + 1);
                KillProcess(pid, expected);
            }
            System.Threading.Thread.Sleep(400);

            // 2. Задачи планировщика
            for (int i = 0; i < items.Count; i++)
            {
                YandexItem it = items[i];
                if (it.Kind != "task") continue;
                Report(progress, Lang.T("Удаляем задачу: ", "Removing task: ") + it.Path);
                RunTool("schtasks.exe", "/Delete /TN \"" + it.Path + "\" /F");
            }

            // 3. Службы
            for (int i = 0; i < items.Count; i++)
            {
                YandexItem it = items[i];
                if (it.Kind != "service") continue;
                Report(progress, Lang.T("Удаляем службу: ", "Removing service: ") + it.Path);
                RunTool("sc.exe", "stop \"" + it.Path + "\"");
                RunTool("sc.exe", "delete \"" + it.Path + "\"");
            }

            // 4. Реестр
            for (int i = 0; i < items.Count; i++)
            {
                YandexItem it = items[i];
                if (it.IsRunValue)
                {
                    Report(progress, Lang.T("Убираем автозагрузку: ", "Removing startup entry: ") + it.ValueName);
                    DeleteRunValue(it);
                }
                else if (it.IsRegistry)
                {
                    Report(progress, Lang.T("Удаляем ключ: ", "Removing key: ") + it.SubKey);
                    DeleteKeyTree(it);
                }
            }

            // 5. Папки и ярлыки
            for (int i = 0; i < items.Count; i++)
            {
                YandexItem it = items[i];
                if (it.Kind == "folder")
                {
                    Report(progress, Lang.T("Удаляем папку: ", "Removing folder: ") + it.Path);
                    long size = it.Size;
                    if (ForceDelete(it.Path)) freed += size;
                }
                else if (it.Kind == "shortcut")
                {
                    Report(progress, Lang.T("Удаляем ярлык: ", "Removing shortcut: ") + it.Path);
                    try { File.Delete(it.Path); } catch { }
                }
            }

            Report(progress, Lang.T("Готово", "Done"));
            return freed;
        }

        private static void KillProcess(int pid, string expectedName)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    // PID мог достаться другому процессу — сверяем имя
                    string name = (p.ProcessName ?? "").ToLowerInvariant();
                    if (!string.IsNullOrEmpty(expectedName) && name != expectedName) return;

                    try { p.CloseMainWindow(); } catch { }
                    if (!p.WaitForExit(1500))
                    {
                        try { p.Kill(); p.WaitForExit(3000); } catch { }
                    }
                }
            }
            catch { }
        }

        private static void RunTool(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p != null && !p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch { }
        }

        private static void DeleteRunValue(YandexItem it)
        {
            RegistryKey root = OpenBase(it.Hive);
            if (root == null) return;
            try
            {
                using (RegistryKey k = root.OpenSubKey(it.SubKey, true))
                {
                    if (k != null) k.DeleteValue(it.ValueName, false);
                }
            }
            catch { }
        }

        private static void DeleteKeyTree(YandexItem it)
        {
            RegistryKey root = OpenBase(it.Hive);
            if (root == null) return;
            try
            {
                root.DeleteSubKeyTree(it.SubKey, false);
            }
            catch { }
        }

        // Удаление папки с обходом «только чтение» и занятых файлов.
        // internal — чтобы можно было покрыть тестом (см. .visualtest)
        internal static bool ForceDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;

            if (File.Exists(path))
            {
                try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                try { File.Delete(path); } catch { }
                return !File.Exists(path);
            }

            if (!Directory.Exists(path)) return true;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                ClearAttributes(path);
                try { Directory.Delete(path, true); } catch { }
                if (!Directory.Exists(path)) return true;
                System.Threading.Thread.Sleep(200);
            }

            // Последний шанс: rd /s /q. Путь пришёл из наших же констант
            // и найденных папок Яндекса — подстановки команд нет
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c rd /s /q \"" + path + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p != null && !p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                    }
                }
            }
            catch { }

            return !Directory.Exists(path);
        }

        private static void ClearAttributes(string root)
        {
            try
            {
                string[] dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
                for (int i = 0; i < dirs.Length; i++)
                {
                    try { File.SetAttributes(dirs[i], FileAttributes.Directory); } catch { }
                }
            }
            catch { }

            try
            {
                string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try { File.SetAttributes(files[i], FileAttributes.Normal); } catch { }
                }
            }
            catch { }

            try { File.SetAttributes(root, FileAttributes.Directory); } catch { }
        }
    }
}
