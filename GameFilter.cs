using System;
using System.Collections.Generic;
using System.IO;

namespace DeepTools
{
    // Фильтр «что считать игрой». Детект GameBooster ловит любую полноэкранную
    // программу с отрисовкой кадров, поэтому в статистику попадали мессенджеры,
    // браузеры и терминалы (discord.exe, msedge.exe, javaw.exe,
    // WindowsTerminal.exe). Здесь список известных не-игр плюс два пользовательских
    // списка: «больше не считать игрой» и обратное «это всё-таки игра».
    public static class GameFilter
    {
        private const string IgnoreKey = "game_ignore";
        private const string AllowKey = "game_allow";
        private const char Sep = ';';

        // Известные не-игры: имена процессов без .exe, в нижнем регистре.
        // Список намеренно широкий - ошибиться в сторону «не считать игрой» для
        // мессенджера безопаснее, чем тянуть его в статистику игр
        private static readonly string[] KnownNonGames =
        {
            // оболочка Windows
            "explorer", "dwm", "searchhost", "shellexperiencehost", "applicationframehost",
            "textinputhost", "startmenuexperiencehost", "lockapp", "sihost", "ctfmon",
            "taskmgr", "rundll32", "wscript", "cscript", "notepad", "wordpad", "mspaint",
            "snippingtool", "screensketch", "calculator", "explorer.exe",
            // браузеры (полноэкранное видео ловится как «игра»)
            "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi",
            "yandex", "browser", "iexplore", "msedgewebview2",
            // мессенджеры и звонки
            "discord", "discordptb", "discordcanary", "vesktop", "telegram", "whatsapp",
            "zoom", "skype", "teams", "ms-teams", "slack", "signal", "element",
            "telegramdesktop", "viber", "thunderbird",
            // терминалы, редакторы, IDE, java-рантайм (Minecraft при желании можно
            // вернуть кнопкой «это всё-таки игра»)
            "windowsterminal", "wt", "powershell", "pwsh", "cmd", "conhost", "openconsole",
            "code", "code - insiders", "devenv", "idea64", "pycharm64", "rider64",
            "goland64", "webstorm64", "clion64", "sublime_text", "notepad++", "eclipse",
            "netbeans", "javaw", "java", "javaws", "jabref", "mstsc",
            // плееры и музыка
            "vlc", "mpv", "mpc-hc", "mpc-hc64", "potplayer", "potplayermini64",
            "wmplayer", "spotify", "musicbee", "aimp", "foobar2000", "itunes",
            // лаунчеры и магазины (сама игра запускается своим exe)
            "steam", "steamwebhelper", "epicgameslauncher", "battle.net", "origin",
            "eaapp", "eadesktop", "uplay", "upc", "ubisoftconnect", "riotclientservices",
            "galaxyclient", "playnite", "itch", "goggalaxy",
            // запись, стрим, удалённый доступ
            "obs64", "obs32", "obs", "streamlabs", "xsplit", "anydesk", "teamviewer",
            "rustdesk", "parsec", "moonlight", "sunshine",
            // монтаж, графика, 3D и движки: они тоже рисуют кадры в полный экран
            "capcut", "premiere", "adobepremierepro", "afterfx", "photoshop", "gimp",
            "krita", "inkscape", "figma", "blender", "3dsmax", "maya", "resolve",
            "davinciresolve", "vegas", "vegaspro", "shotcut", "kdenlive", "filmora",
            "openshot", "movavi", "clipchamp", "handbrake", "unity", "unityhub",
            "unreal", "ue4editor", "ue5editor", "godot", "audition", "illustrator",
            // офис
            "winword", "excel", "powerpnt", "outlook", "onenote", "acrord32", "acrobat",
        };

        private static readonly HashSet<string> known =
            new HashSet<string>(KnownNonGames, StringComparer.OrdinalIgnoreCase);

        // Имя процесса к сравнению: без .exe, без пути, в нижнем регистре
        public static string Normalize(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return "";
            string n = processName.Trim();
            int slash = n.LastIndexOf('\\');
            if (slash >= 0) n = n.Substring(slash + 1);
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                n = n.Substring(0, n.Length - 4);
            return n.ToLowerInvariant();
        }

        public static bool IsBuiltInNonGame(string processName)
        {
            return known.Contains(Normalize(processName));
        }

        // Считать ли процесс игрой. Логика: известные не-игры минус то, что
        // пользователь вернул вручную, плюс то, что он запретил вручную
        public static bool IsIgnored(string processName)
        {
            string n = Normalize(processName);
            if (n == "") return true;
            if (UserList(AllowKey).Contains(n)) return false;
            if (UserList(IgnoreKey).Contains(n)) return true;
            return known.Contains(n);
        }

        public static List<string> UserIgnored()
        {
            var list = new List<string>(UserList(IgnoreKey));
            list.Sort();
            return list;
        }

        // «Больше не считать игрой»
        public static void Ignore(string processName)
        {
            string n = Normalize(processName);
            if (n == "") return;
            var allow = UserList(AllowKey);
            allow.Remove(n);
            AppConfig.Set(AllowKey, string.Join(Sep.ToString(), allow.ToArray()));
            var ignore = UserList(IgnoreKey);
            if (!ignore.Contains(n)) ignore.Add(n);
            ignore.Sort();
            AppConfig.Set(IgnoreKey, string.Join(Sep.ToString(), ignore.ToArray()));
        }

        // «Это всё-таки игра» - снимает и наш список, и пользовательский запрет
        public static void Allow(string processName)
        {
            string n = Normalize(processName);
            if (n == "") return;
            var ignore = UserList(IgnoreKey);
            ignore.Remove(n);
            AppConfig.Set(IgnoreKey, string.Join(Sep.ToString(), ignore.ToArray()));
            var allow = UserList(AllowKey);
            if (!allow.Contains(n)) allow.Add(n);
            allow.Sort();
            AppConfig.Set(AllowKey, string.Join(Sep.ToString(), allow.ToArray()));
        }

        public static void ResetUserLists()
        {
            AppConfig.Set(IgnoreKey, "");
            AppConfig.Set(AllowKey, "");
        }

        private static List<string> UserList(string key)
        {
            var list = new List<string>();
            string raw = AppConfig.Get(key, "");
            if (raw == "") return list;
            foreach (string part in raw.Split(Sep))
            {
                string n = Normalize(part);
                if (n != "" && !list.Contains(n)) list.Add(n);
            }
            return list;
        }

        // ---- Чистка уже записанной статистики ----

        // Сколько записей в журнале сессий принадлежит не-играм
        public static int CountInLog()
        {
            return PurgeFromLog(true);
        }

        // Убирает сессии не-игр из журнала. dryRun = только посчитать.
        // Возвращает, сколько строк убрано (или было бы убрано)
        public static int PurgeFromLog()
        {
            return PurgeFromLog(false);
        }

        private static int PurgeFromLog(bool dryRun)
        {
            int removed = 0;
            try
            {
                string path = GameSessionTracker.LogPath;
                if (!File.Exists(path)) return 0;
                string[] lines = File.ReadAllLines(path);
                var keep = new List<string>(lines.Length);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == null || line.Trim() == "") continue;
                    string[] parts = line.Split('|');
                    if (parts.Length >= 3 && IsIgnored(parts[0])) { removed++; continue; }
                    keep.Add(line);
                }
                if (!dryRun && removed > 0)
                    File.WriteAllLines(path, keep.ToArray());
            }
            catch { }
            return removed;
        }

        // Имена из журнала, которые сейчас считаются не-играми: их показываем в
        // списке игнора, чтобы любую можно было вернуть («это всё-таки игра»)
        public static List<string> IgnoredInLog()
        {
            var found = new List<string>();
            try
            {
                string path = GameSessionTracker.LogPath;
                if (!File.Exists(path)) return found;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] parts = lines[i].Split('|');
                    if (parts.Length < 3) continue;
                    string n = Normalize(parts[0]);
                    if (n == "" || found.Contains(n)) continue;
                    if (IsIgnored(n)) found.Add(n);
                }
                found.Sort();
            }
            catch { }
            return found;
        }

        // Убирает из журнала сессии одного процесса (кнопка «удалить из статистики»)
        public static int RemoveFromLog(string processName)
        {
            string n = Normalize(processName);
            int removed = 0;
            try
            {
                string path = GameSessionTracker.LogPath;
                if (!File.Exists(path) || n == "") return 0;
                string[] lines = File.ReadAllLines(path);
                var keep = new List<string>(lines.Length);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (line == null || line.Trim() == "") continue;
                    string[] parts = line.Split('|');
                    if (parts.Length >= 3 && Normalize(parts[0]) == n) { removed++; continue; }
                    keep.Add(line);
                }
                if (removed > 0) File.WriteAllLines(path, keep.ToArray());
            }
            catch { }
            return removed;
        }
    }
}
