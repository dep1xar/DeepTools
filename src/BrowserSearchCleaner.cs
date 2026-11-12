using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    // Яндекс.Браузер (и его установщик) умеет прописывать поиск Яндекса в другие
    // браузеры и в оболочку Windows. Штатный деинсталлятор этого не убирает:
    // в Chrome/Edge остаётся поисковик по умолчанию, в Firefox - движок, в IE -
    // SearchScope. Здесь находим и аккуратно удаляем только яндексовые записи.
    //
    // Chromium-браузеры хранят поиск в Preferences (JSON). Firefox - в prefs.js и
    // search.json.mozlz4. Перед изменением делается резервная копия
    // *.deeptools.bak. Если браузер запущен, файл не трогаем: он всё равно
    // перезапишет настройки при выходе.
    public static class BrowserSearchCleaner
    {
        private static readonly JavaScriptSerializer Json =
            new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private class ChromiumBrowser
        {
            public string Name;
            public string UserData;
            public string ProcessName;
        }

        // ---- Поиск ----

        public static void Scan(List<YandexItem> items)
        {
            ScanChromium(items);
            ScanFirefox(items);
            ScanIeSearchScopes(items);
            ScanUrlAssociations(items);
        }

        private static List<ChromiumBrowser> GetChromiumBrowsers()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var list = new List<ChromiumBrowser>();

            AddBrowser(list, "Chrome", Path.Combine(local, "Google", "Chrome", "User Data"), "chrome");
            AddBrowser(list, "Chrome Beta", Path.Combine(local, "Google", "Chrome Beta", "User Data"), "chrome");
            AddBrowser(list, "Chrome Dev", Path.Combine(local, "Google", "Chrome Dev", "User Data"), "chrome");
            AddBrowser(list, "Chrome Canary", Path.Combine(local, "Google", "Chrome SxS", "User Data"), "chrome");
            AddBrowser(list, "Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"), "msedge");
            AddBrowser(list, "Edge Beta", Path.Combine(local, "Microsoft", "Edge Beta", "User Data"), "msedge");
            AddBrowser(list, "Edge Dev", Path.Combine(local, "Microsoft", "Edge Dev", "User Data"), "msedge");
            AddBrowser(list, "Edge Canary", Path.Combine(local, "Microsoft", "Edge SxS", "User Data"), "msedge");
            AddBrowser(list, "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"), "brave");
            AddBrowser(list, "Vivaldi", Path.Combine(local, "Vivaldi", "User Data"), "vivaldi");
            AddBrowser(list, "Opera", Path.Combine(roaming, "Opera Software", "Opera Stable"), "opera");
            AddBrowser(list, "Opera GX", Path.Combine(roaming, "Opera Software", "Opera GX Stable"), "opera");
            AddBrowser(list, "Chromium", Path.Combine(local, "Chromium", "User Data"), "chromium");
            AddBrowser(list, "Yandex", Path.Combine(local, "Yandex", "YandexBrowser", "User Data"), "browser");

            return list;
        }

        private static void AddBrowser(List<ChromiumBrowser> list, string name, string userData, string process)
        {
            if (Directory.Exists(userData))
            {
                list.Add(new ChromiumBrowser { Name = name, UserData = userData, ProcessName = process });
            }
        }

        private static void ScanChromium(List<YandexItem> items)
        {
            List<ChromiumBrowser> browsers = GetChromiumBrowsers();
            for (int b = 0; b < browsers.Count; b++)
            {
                ChromiumBrowser browser = browsers[b];
                List<string> prefs = FindChromiumPreferences(browser.UserData);
                for (int i = 0; i < prefs.Count; i++)
                {
                    string path = prefs[i];
                    string profileDir = Path.GetDirectoryName(path);
                    bool hasYandex = ChromiumContainsYandex(path);
                    if (!hasYandex && !string.IsNullOrEmpty(profileDir))
                        hasYandex = ChromiumWebDataContainsYandex(Path.Combine(profileDir, "Web Data"));
                    if (!hasYandex) continue;

                    string profile = Path.GetFileName(Path.GetDirectoryName(path));
                    bool running = IsProcessRunning(browser.ProcessName);
                    items.Add(new YandexItem
                    {
                        Kind = "search",
                        Title = Lang.T("Поиск Яндекса в ", "Yandex search in ") + browser.Name +
                                " (" + profile + ")" +
                                (running ? Lang.T(" - закрой браузер", " - close the browser") : ""),
                        Path = path,
                        ValueName = "chromium|" + browser.Name + "|" + browser.ProcessName,
                        Size = 0
                    });
                }
            }
        }

        private static List<string> FindChromiumPreferences(string userData)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddPrefs(result, seen, userData);
            string[] dirs = SafeDirectories(userData);
            for (int i = 0; i < dirs.Length; i++)
            {
                AddPrefs(result, seen, dirs[i]);
                string[] subs = SafeDirectories(dirs[i]);
                for (int j = 0; j < subs.Length; j++) AddPrefs(result, seen, subs[j]);
            }
            return result;
        }

        private static void AddPrefs(List<string> result, HashSet<string> seen, string dir)
        {
            try
            {
                string path = Path.Combine(dir, "Preferences");
                if (File.Exists(path) && seen.Add(path)) result.Add(path);
            }
            catch { }
        }

        private static string[] SafeDirectories(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return new string[0]; }
        }

        private static bool ChromiumContainsYandex(string path)
        {
            try
            {
                var root = Json.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                if (root == null) return false;
                return ContainsYandex(Get(root, "default_search_provider_data")) ||
                       ContainsYandex(Get(root, "search_provider_overrides"));
            }
            catch { return false; }
        }

        // internal - чтобы можно было покрыть тестом без установленного браузера
        internal static bool RemoveChromiumPreferences(string path)
        {
            if (!File.Exists(path)) return false;

            Dictionary<string, object> root;
            try { root = Json.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>; }
            catch { return false; }
            if (root == null) return false;

            bool changed = false;
            bool defaultWasYandex = ContainsYandex(Get(root, "default_search_provider_data"));

            if (defaultWasYandex)
            {
                root.Remove("default_search_provider_data");
                changed = true;
            }

            object overrides = Get(root, "search_provider_overrides");
            if (ContainsYandex(overrides))
            {
                IList list = overrides as IList;
                var kept = new List<object>();
                if (list != null)
                {
                    foreach (object entry in list)
                        if (!ContainsYandex(entry)) kept.Add(entry);
                }

                if (kept.Count == 0) root.Remove("search_provider_overrides");
                else root["search_provider_overrides"] = kept;
                changed = true;
            }

            if (!changed)
            {
                // Яндекс может быть только в Web Data, без записи в Preferences.
                string onlyProfileDir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(onlyProfileDir)) return false;
                return RemoveChromiumWebData(Path.Combine(onlyProfileDir, "Web Data"));
            }

            // default_search_provider - это GUID. Сбрасываем его только если
            // яндексовым был именно поисковик по умолчанию: если Яндекс был
            // лишь дополнительным движком, настройку пользователя не трогаем.
            if (defaultWasYandex) root.Remove("default_search_provider");

            try
            {
                Backup(path);
                WriteAtomic(path, new UTF8Encoding(false).GetBytes(Json.Serialize(root)));
                // Проверяем, что файл остался валидным JSON
                Json.DeserializeObject(File.ReadAllText(path));

                // Chrome/Edge хранят список поисковиков ещё и в SQLite-базе
                // Web Data. Если она есть и браузер закрыт - убираем оттуда.
                try
                {
                    string profileDir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(profileDir))
                        RemoveChromiumWebData(Path.Combine(profileDir, "Web Data"));
                }
                catch { }

                return true;
            }
            catch { return false; }
        }

        // Chrome/Edge/Brave/Vivaldi/Opera хранят список поисковиков в SQLite
        // (Web Data, таблица keywords). Удаляем только строки с yandex/яндекс.
        // Используется системная winsqlite3.dll из Windows 10/11; если её нет,
        // молча пропускаем - Preferences уже очищены.
        internal static bool RemoveChromiumWebData(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            IntPtr db = IntPtr.Zero;
            try
            {
                int rc = sqlite3_open_v2(path, out db, SqliteOpenReadWrite, IntPtr.Zero);
                if (rc != 0)
                {
                    if (db != IntPtr.Zero) sqlite3_close(db);
                    return false;
                }

                BackupSqlite(path);
                sqlite3_busy_timeout(db, 4000);

                int removed = 0;
                removed += ExecScalar(db,
                    "DELETE FROM keywords WHERE lower(short_name) LIKE '%yandex%' " +
                    "OR lower(keyword) LIKE '%yandex%' " +
                    "OR short_name LIKE '%Яндекс%' OR short_name LIKE '%яндекс%' OR short_name LIKE '%ЯНДЕКС%'");
                removed += ExecScalar(db,
                    "DELETE FROM keywords WHERE lower(search_url) LIKE '%yandex%'");
                removed += ExecScalar(db,
                    "DELETE FROM keywords WHERE lower(display_url) LIKE '%yandex%'");
                removed += ExecScalar(db,
                    "DELETE FROM keywords WHERE lower(url) LIKE '%yandex%'");
                removed += ExecScalar(db,
                    "DELETE FROM keywords WHERE lower(favicon_url) LIKE '%yandex%'");
                return removed > 0;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch { return false; }
            finally
            {
                if (db != IntPtr.Zero) { try { sqlite3_close(db); } catch { } }
            }
        }

        internal static bool ChromiumWebDataContainsYandex(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            IntPtr db = IntPtr.Zero;
            IntPtr stmt = IntPtr.Zero;
            try
            {
                if (sqlite3_open_v2(path, out db, SqliteOpenReadOnly, IntPtr.Zero) != 0)
                {
                    if (db != IntPtr.Zero) sqlite3_close(db);
                    return false;
                }

                string sql =
                    "SELECT 1 FROM keywords WHERE lower(short_name) LIKE '%yandex%' " +
                    "OR lower(keyword) LIKE '%yandex%' " +
                    "OR short_name LIKE '%Яндекс%' OR short_name LIKE '%яндекс%' " +
                    "OR short_name LIKE '%ЯНДЕКС%' LIMIT 1";
                if (sqlite3_prepare_v2(db, sql, -1, out stmt, IntPtr.Zero) != 0) return false;
                return sqlite3_step(stmt) == SqliteRow;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
            catch { return false; }
            finally
            {
                if (stmt != IntPtr.Zero) { try { sqlite3_finalize(stmt); } catch { } }
                if (db != IntPtr.Zero) { try { sqlite3_close(db); } catch { } }
            }
        }

        private static int ExecScalar(IntPtr db, string sql)
        {
            IntPtr err = IntPtr.Zero;
            try
            {
                int rc = sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out err);
                if (rc != 0) return 0;
                return sqlite3_changes(db);
            }
            finally
            {
                if (err != IntPtr.Zero) { try { sqlite3_free(err); } catch { } }
            }
        }

        private static void BackupSqlite(string path)
        {
            try
            {
                string bak = path + ".deeptools.bak";
                if (!File.Exists(bak)) File.Copy(path, bak, true);
                string wal = path + "-wal";
                if (File.Exists(wal) && !File.Exists(bak + "-wal")) File.Copy(wal, bak + "-wal", true);
                string shm = path + "-shm";
                if (File.Exists(shm) && !File.Exists(bak + "-shm")) File.Copy(shm, bak + "-shm", true);
            }
            catch { }
        }

        private const int SqliteOpenReadWrite = 0x00000002;
        private const int SqliteOpenReadOnly = 0x00000001;
        private const int SqliteRow = 100;

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
            out IntPtr db, int flags, IntPtr vfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(
            IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
            IntPtr callback, IntPtr arg, out IntPtr errmsg);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_changes(IntPtr db);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(
            IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
            int numBytes, out IntPtr stmt, IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr ptr);

        private static void ScanFirefox(List<YandexItem> items)
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");
            string[] profiles = SafeDirectories(root);
            bool running = IsProcessRunning("firefox");

            for (int i = 0; i < profiles.Length; i++)
            {
                string profile = profiles[i];
                string prefs = Path.Combine(profile, "prefs.js");
                string search = Path.Combine(profile, "search.json.mozlz4");
                bool found = false;

                if (File.Exists(prefs)) found = FirefoxPrefsContainsYandex(prefs);
                if (!found && File.Exists(search)) found = FirefoxSearchContainsYandex(search);
                if (!found) continue;

                items.Add(new YandexItem
                {
                    Kind = "search",
                    Title = Lang.T("Поиск Яндекса в Firefox (", "Yandex search in Firefox (") +
                            Path.GetFileName(profile) + ")" +
                            (running ? Lang.T(" - закрой браузер", " - close the browser") : ""),
                    Path = profile,
                    ValueName = "firefox|firefox",
                    Size = 0
                });
            }
        }

        private static bool FirefoxPrefsContainsYandex(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line.IndexOf("browser.search.", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        LooksLikeYandex(line)) return true;
                    if (line.IndexOf("browser.urlbar.placeholderName", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        LooksLikeYandex(line)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool FirefoxSearchContainsYandex(string path)
        {
            try
            {
                byte[] jsonBytes = MozLz4Decompress(path);
                if (jsonBytes == null) return false;
                string text = Encoding.UTF8.GetString(jsonBytes);
                return text.IndexOf("yandex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       text.IndexOf("яндекс", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        internal static bool RemoveFirefoxProfile(string profileDir)
        {
            if (string.IsNullOrEmpty(profileDir) || !Directory.Exists(profileDir)) return false;

            bool changed = false;
            string prefs = Path.Combine(profileDir, "prefs.js");
            if (File.Exists(prefs))
            {
                try
                {
                    string[] lines = File.ReadAllLines(prefs, Encoding.UTF8);
                    var kept = new List<string>();
                    bool prefsChanged = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        bool searchLine = line.IndexOf("browser.search.", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool barLine = line.IndexOf("browser.urlbar.placeholderName", StringComparison.OrdinalIgnoreCase) >= 0;
                        if ((searchLine || barLine) && LooksLikeYandex(line))
                        {
                            prefsChanged = true;
                            continue;
                        }
                        kept.Add(line);
                    }

                    if (prefsChanged)
                    {
                        Backup(prefs);
                        WriteAtomic(prefs, new UTF8Encoding(false).GetBytes(string.Join("\r\n", kept.ToArray()) + "\r\n"));
                        changed = true;
                    }
                }
                catch { }
            }

            string search = Path.Combine(profileDir, "search.json.mozlz4");
            if (File.Exists(search))
            {
                try
                {
                    byte[] jsonBytes = MozLz4Decompress(search);
                    if (jsonBytes != null)
                    {
                        var root = Json.DeserializeObject(Encoding.UTF8.GetString(jsonBytes)) as Dictionary<string, object>;
                        if (root != null)
                        {
                            var removedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            bool removed = RemoveYandexEngines(root, "engines", removedIds);
                            if (RemoveYandexEngines(root, "privateEngines", removedIds)) removed = true;

                            if (removed)
                            {
                                ClearYandexDefaults(root, removedIds);
                                Backup(search);
                                WriteAtomic(search, MozLz4Compress(
                                    new UTF8Encoding(false).GetBytes(Json.Serialize(root))));
                                changed = true;
                            }
                        }
                    }
                }
                catch { }
            }

            return changed;
        }

        // Убирает яндексовые движки из массива engines/privateEngines и
        // запоминает их id, чтобы потом сбросить ссылки на движок по умолчанию.
        private static bool RemoveYandexEngines(Dictionary<string, object> root, string key, HashSet<string> removedIds)
        {
            IList engines = Get(root, key) as IList;
            if (engines == null) return false;

            var kept = new List<object>();
            bool removed = false;
            foreach (object engine in engines)
            {
                if (ContainsYandex(engine))
                {
                    removed = true;
                    var d = engine as Dictionary<string, object>;
                    if (d != null)
                    {
                        string id = Text(d, "id");
                        if (!string.IsNullOrEmpty(id)) removedIds.Add(id);
                        string name = Text(d, "_name");
                        if (string.IsNullOrEmpty(name)) name = Text(d, "name");
                        if (!string.IsNullOrEmpty(name)) removedIds.Add(name);
                    }
                }
                else kept.Add(engine);
            }

            if (removed)
            {
                if (kept.Count == 0) root.Remove(key);
                else root[key] = kept;
            }
            return removed;
        }

        // Если поисковик Яндекса был движком по умолчанию, сбрасываем ссылку:
        // Firefox сам выберет штатный движок при следующем старте.
        private static void ClearYandexDefaults(Dictionary<string, object> root, HashSet<string> removedIds)
        {
            ClearYandexDefault(root, "defaultEngineId", "defaultEngineIdHash", removedIds);

            var meta = Get(root, "metaData") as Dictionary<string, object>;
            if (meta != null)
            {
                ClearYandexDefault(meta, "defaultEngineId", "defaultEngineIdHash", removedIds);
                ClearYandexDefault(meta, "appDefaultEngineId", null, removedIds);
            }
        }

        private static void ClearYandexDefault(Dictionary<string, object> dict, string idKey, string hashKey, HashSet<string> removedIds)
        {
            string id = Text(dict, idKey);
            if (string.IsNullOrEmpty(id)) return;
            if (!LooksLikeYandex(id) && !removedIds.Contains(id)) return;

            dict.Remove(idKey);
            if (!string.IsNullOrEmpty(hashKey)) dict.Remove(hashKey);
        }

        private static string Text(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v == null ? "" : Convert.ToString(v);
        }

        private static void ScanIeSearchScopes(List<YandexItem> items)
        {
            ScanIeScopes(items, RegistryHive.CurrentUser, Registry.CurrentUser,
                @"Software\Microsoft\Internet Explorer\SearchScopes");
            ScanIeScopes(items, RegistryHive.LocalMachine, Registry.LocalMachine,
                @"Software\Microsoft\Internet Explorer\SearchScopes");
            ScanIeScopes(items, RegistryHive.LocalMachine, Registry.LocalMachine,
                @"Software\Wow6432Node\Microsoft\Internet Explorer\SearchScopes");
        }

        private static void ScanIeScopes(List<YandexItem> items, RegistryHive hive, RegistryKey root, string baseSub)
        {
            try
            {
                using (RegistryKey scopes = root.OpenSubKey(baseSub))
                {
                    if (scopes == null) return;
                    string[] names = scopes.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        try
                        {
                            using (RegistryKey scope = scopes.OpenSubKey(names[i]))
                            {
                                if (scope == null) continue;
                                string display = scope.GetValue("DisplayName") as string;
                                string url = scope.GetValue("URL") as string;
                                string def = scope.GetValue("") as string;
                                if (!LooksLikeYandex(display) && !LooksLikeYandex(url) &&
                                    !LooksLikeYandex(def) && !LooksLikeYandex(names[i])) continue;

                                items.Add(new YandexItem
                                {
                                    Kind = "searchscope",
                                    Title = Lang.T("Поиск в IE: ", "IE search: ") +
                                            (string.IsNullOrEmpty(display) ? names[i] : display),
                                    Hive = hive,
                                    SubKey = baseSub + "\\" + names[i],
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

        private static void ScanUrlAssociations(List<YandexItem> items)
        {
            string[] protocols = { "http", "https" };
            for (int i = 0; i < protocols.Length; i++)
                ScanUserChoice(items,
                    @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations",
                    protocols[i], Lang.T("ссылок ", "links "), protocols[i]);

            string[] exts = { ".html", ".htm" };
            for (int i = 0; i < exts.Length; i++)
                ScanUserChoice(items,
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts",
                    exts[i], Lang.T("файлов ", "files "), exts[i]);
        }

        private static void ScanUserChoice(List<YandexItem> items, string baseSub, string name, string label, string shown)
        {
            try
            {
                string sub = baseSub + "\\" + name + "\\UserChoice";
                using (RegistryKey choice = Registry.CurrentUser.OpenSubKey(sub))
                {
                    if (choice == null) return;
                    string progId = choice.GetValue("ProgId") as string;
                    if (!LooksLikeYandex(progId)) return;

                    items.Add(new YandexItem
                    {
                        Kind = "userchoice",
                        Title = Lang.T("Яндекс по умолчанию для ", "Yandex is default for ") +
                                label + shown + " (" + progId + ")",
                        Hive = RegistryHive.CurrentUser,
                        SubKey = sub,
                        Size = 0
                    });
                }
            }
            catch { }
        }

        // ---- Удаление ----

        // Возвращает true, если что-то реально изменилось.
        public static bool Remove(YandexItem item)
        {
            if (item == null || item.Kind != "search") return false;

            string[] parts = (item.ValueName ?? "").Split('|');
            if (parts.Length >= 3 && parts[0] == "chromium")
            {
                if (IsProcessRunning(parts[2])) return false;
                return RemoveChromiumPreferences(item.Path);
            }

            if (parts.Length >= 2 && parts[0] == "firefox")
            {
                if (IsProcessRunning("firefox")) return false;
                return RemoveFirefoxProfile(item.Path);
            }

            return false;
        }

        private static bool IsProcessRunning(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                Process[] processes = Process.GetProcessesByName(name);
                bool any = processes.Length > 0;
                for (int i = 0; i < processes.Length; i++)
                {
                    try { processes[i].Dispose(); } catch { }
                }
                return any;
            }
            catch { return false; }
        }

        // ---- JSON / файлы ----

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static bool ContainsYandex(object value)
        {
            if (value == null) return false;

            string s = value as string;
            if (s != null) return LooksLikeYandex(s);

            var dict = value as Dictionary<string, object>;
            if (dict != null)
            {
                foreach (KeyValuePair<string, object> pair in dict)
                {
                    if (LooksLikeYandex(pair.Key) || ContainsYandex(pair.Value)) return true;
                }
                return false;
            }

            var list = value as IList;
            if (list != null)
            {
                foreach (object item in list)
                    if (ContainsYandex(item)) return true;
            }
            return false;
        }

        private static bool LooksLikeYandex(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("yandex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   s.IndexOf("яндекс", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Backup(string path)
        {
            try
            {
                string bak = path + ".deeptools.bak";
                if (!File.Exists(bak)) File.Copy(path, bak, true);
            }
            catch { }
        }

        private static void WriteAtomic(string path, byte[] data)
        {
            string dir = Path.GetDirectoryName(path);
            string tmp = Path.Combine(dir, Path.GetFileName(path) + ".deeptools.tmp");
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
            File.WriteAllBytes(tmp, data);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch
            {
                try { File.Copy(tmp, path, true); } catch { }
                try { File.Delete(tmp); } catch { }
            }
        }

        // ---- Mozilla mozLz4 ----
        //
        // Формат: "mozLz40\0" + int32 размер распакованных данных + LZ4-блок.
        // Это именно LZ4 (не zlib/deflate): распаковка через DeflateStream
        // выдавала мусор и могла испортить search.json.mozlz4.

        internal static byte[] MozLz4Decompress(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 13) return null;
            if (data[0] != (byte)'m' || data[1] != (byte)'o' || data[2] != (byte)'z' ||
                data[3] != (byte)'L' || data[4] != (byte)'z' || data[5] != (byte)'4' ||
                data[6] != (byte)'0' || data[7] != 0) return null;

            int size = BitConverter.ToInt32(data, 8);
            if (size < 0 || size > 64 * 1024 * 1024) return null;
            return Lz4Decompress(data, 12, data.Length - 12, size);
        }

        internal static byte[] MozLz4Compress(byte[] jsonBytes)
        {
            if (jsonBytes == null) jsonBytes = new byte[0];
            using (var output = new MemoryStream())
            {
                output.Write(new byte[] { (byte)'m', (byte)'o', (byte)'z', (byte)'L',
                                          (byte)'z', (byte)'4', (byte)'0', 0 }, 0, 8);
                output.Write(BitConverter.GetBytes(jsonBytes.Length), 0, 4);
                byte[] block = Lz4Compress(jsonBytes);
                output.Write(block, 0, block.Length);
                return output.ToArray();
            }
        }

        // Распаковка одного LZ4-блока (raw block format, без фрейма).
        private static byte[] Lz4Decompress(byte[] input, int offset, int length, int outputSize)
        {
            byte[] output = new byte[outputSize];
            int ip = offset;
            int end = offset + length;
            int op = 0;

            while (ip < end)
            {
                int token = input[ip++];

                int literalLength = token >> 4;
                if (literalLength == 15)
                {
                    int b;
                    do
                    {
                        if (ip >= end) return null;
                        b = input[ip++];
                        literalLength += b;
                    } while (b == 255);
                }

                if (literalLength > 0)
                {
                    if (ip + literalLength > end || op + literalLength > outputSize) return null;
                    Buffer.BlockCopy(input, ip, output, op, literalLength);
                    ip += literalLength;
                    op += literalLength;
                }

                // Последняя последовательность содержит только литералы.
                if (ip >= end) break;

                if (ip + 2 > end) return null;
                int matchOffset = input[ip] | (input[ip + 1] << 8);
                ip += 2;
                if (matchOffset == 0 || matchOffset > op) return null;

                int matchLength = token & 0x0F;
                if (matchLength == 15)
                {
                    int b;
                    do
                    {
                        if (ip >= end) return null;
                        b = input[ip++];
                        matchLength += b;
                    } while (b == 255);
                }
                matchLength += 4;

                if (op + matchLength > outputSize) return null;
                for (int i = 0; i < matchLength; i++)
                {
                    output[op] = output[op - matchOffset];
                    op++;
                }
            }

            if (op != outputSize) return null;
            return output;
        }

        // Простейший корректный LZ4-блок: все данные - одна литеральная
        // последовательность, без совпадений. Для search.json этого достаточно:
        // файл маленький, а валидность важнее степени сжатия.
        private static byte[] Lz4Compress(byte[] input)
        {
            using (var output = new MemoryStream())
            {
                int length = input.Length;
                if (length < 15)
                {
                    output.WriteByte((byte)(length << 4));
                }
                else
                {
                    output.WriteByte(0xF0);
                    int remaining = length - 15;
                    while (remaining >= 255)
                    {
                        output.WriteByte(255);
                        remaining -= 255;
                    }
                    output.WriteByte((byte)remaining);
                }
                output.Write(input, 0, length);
                return output.ToArray();
            }
        }
    }
}
