using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    // Одна ошибка в агрегированной статистике.
    public class OnlineErrorStat
    {
        public string Code = "";
        public string Title = "";
        public string Module = "";
        public int Count;
        public int Users;
        public long LastSeen;
    }

    public class OnlineVersionStat
    {
        public string Version = "";
        public int Users;
        public int Launches;
    }

    public class OnlineCountryStat
    {
        public string Country = "";
        public int Users;
    }

    // Ответ /api/v1/stats. Поля специально простые: панель не зависит от
    // сторонних JSON-библиотек.
    public class OnlineStats
    {
        public bool Ok;
        public string Error = "";
        public long GeneratedAt;
        public int OnlineNow;
        public int Online24h;
        public int UsersTotal;
        public int LaunchesToday;
        public int LaunchesTotal;
        public int Errors24h;
        public int ErrorsTotal;
        public int UsersWithErrors24h;
        public List<OnlineErrorStat> TopErrors = new List<OnlineErrorStat>();
        public List<OnlineVersionStat> Versions = new List<OnlineVersionStat>();
        public List<OnlineCountryStat> Countries = new List<OnlineCountryStat>();
    }

    // Анонимная телеметрия: случайный install_id, версия, ОС, язык, тип события
    // и подпись ошибки. Никаких имён, путей, содержимого файлов и IP.
    //
    // Сервер не является частью exe: его нужно развернуть отдельно
    // (см. server/README.md) и указать адрес в telemetry_url.
    public static class Telemetry
    {
        // Адрес сервера по умолчанию. Обычно он задаётся при сборке:
        // build.ps1 -TelemetryUrl https://stats.example.com
        //
        // Пустая строка = телеметрия не отправляется, а счётчик на главной
        // честно показывает «—».
        private const string DefaultEndpoint = "";

        private static readonly object sync = new object();
        private static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static System.Threading.Timer heartbeatTimer;
        private static string installId;
        private static string sessionId;
        private static bool started;
        private static int sending;
        private static string lastError = "";

        static Telemetry()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                // pages.dev у части провайдеров не пропускает TLS 1.2: нужен
                // TLS 1.3. Он есть в Windows 11 / .NET Framework 4.8.1.
                // Числовое значение (12288) не требует enum Tls13 при сборке
                // на более старом .NET Framework, а Enum.IsDefined не даёт
                // включить флаг там, где runtime его не знает.
                bool hasTls13 = false;
                try { hasTls13 = Enum.IsDefined(typeof(System.Security.Authentication.SslProtocols), 12288); }
                catch { }
                if (hasTls13 && Environment.OSVersion.Version.Build >= 22000)
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)12288;
            }
            catch { }
        }

        public static bool Enabled
        {
            get { return AppConfig.GetBool("telemetry_enabled", true); }
            set
            {
                // При выключении сначала отправляем exit (пока Enabled ещё true),
                // затем сохраняем настройку. При включении — наоборот.
                if (value)
                {
                    AppConfig.SetBool("telemetry_enabled", true);
                    Start();
                }
                else
                {
                    Stop();
                    AppConfig.SetBool("telemetry_enabled", false);
                }
            }
        }

        public static string Endpoint
        {
            get
            {
                // Приоритет: config.txt -> переменная окружения -> значение,
                // вшитое при сборке -> запасная константа.
                string v = AppConfig.Get("telemetry_url", "");
                if (string.IsNullOrEmpty(v))
                {
                    try { v = Environment.GetEnvironmentVariable("DEEPTOOLS_TELEMETRY_URL") ?? ""; }
                    catch { v = ""; }
                }
                if (string.IsNullOrEmpty(v)) v = BuildSecrets.TelemetryUrl;
                if (string.IsNullOrEmpty(v)) v = DefaultEndpoint;
                v = (v ?? "").Trim();
                if (v.EndsWith("/")) v = v.Substring(0, v.Length - 1);
                return v;
            }
        }

        // Ключ просмотра полной панели. В обычной сборке пуст: пользователю
        // доступен только счётчик «Сейчас пользуются DeepTools». Отдельный
        // DeepToolsStats.exe собирается с -StatsToken и показывает полную
        // статистику, если сервер требует DT_VIEW_TOKEN.
        public static string ViewToken
        {
            get
            {
                string v = AppConfig.Get("stats_view_token", "");
                if (string.IsNullOrEmpty(v))
                {
                    try { v = Environment.GetEnvironmentVariable("DEEPTOOLS_STATS_TOKEN") ?? ""; }
                    catch { v = ""; }
                }
                if (string.IsNullOrEmpty(v)) v = BuildSecrets.StatsToken;
                return (v ?? "").Trim();
            }
        }

        public static bool Configured
        {
            get { return Endpoint.Length > 0; }
        }

        public static string LastError
        {
            get { lock (sync) { return lastError; } }
        }

        public static string InstallId
        {
            get
            {
                lock (sync)
                {
                    if (installId == null)
                    {
                        installId = AppConfig.Get("telemetry_install_id", "");
                        if (!IsValidId(installId))
                        {
                            installId = Guid.NewGuid().ToString("N");
                            AppConfig.Set("telemetry_install_id", installId);
                        }
                    }
                    return installId;
                }
            }
        }

        public static string SessionId
        {
            get
            {
                lock (sync)
                {
                    if (sessionId == null) sessionId = Guid.NewGuid().ToString("N");
                    return sessionId;
                }
            }
        }

        private static bool IsValidId(string v)
        {
            if (string.IsNullOrEmpty(v) || v.Length < 8 || v.Length > 64) return false;
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') ||
                          (c >= 'A' && c <= 'F') || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        public static void Start()
        {
            // Без настроенного сервера не начинаем сессию: иначе после
            // настройки telemetry_url в config.txt старт уже не отправится.
            if (!Enabled || !Configured) return;

            lock (sync)
            {
                if (started) return;
                started = true;
            }

            SendEvent("start", true);
            lock (sync)
            {
                if (heartbeatTimer == null)
                {
                    heartbeatTimer = new System.Threading.Timer(
                        HeartbeatTick, null, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
                }
            }
        }

        public static void Stop()
        {
            bool wasStarted;
            lock (sync)
            {
                wasStarted = started;
                started = false;
            }
            StopTimer();
            // Короткий синхронный exit: приложение уже закрывается, но сервер
            // успевает снять сессию с онлайна.
            if (wasStarted) SendEvent("exit", true);
        }

        private static void StopTimer()
        {
            lock (sync)
            {
                if (heartbeatTimer != null)
                {
                    try { heartbeatTimer.Dispose(); } catch { }
                    heartbeatTimer = null;
                }
            }
        }

        private static void HeartbeatTick(object state)
        {
            SendEvent("heartbeat", false);
        }

        public static void Track(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return;
            SendEvent(eventName, false);
        }

        public static void ReportError(string moduleName, Exception ex)
        {
            if (ex == null || !Enabled || !Configured) return;

            string module = string.IsNullOrEmpty(moduleName) ? "Unknown" : moduleName;
            if (module.Length > 64) module = module.Substring(0, 64);

            string type = ex.GetType().FullName ?? ex.GetType().Name;
            string signature = module + "|" + type + "|" + ex.HResult.ToString("X8");
            string code = Sha256Hex(signature);
            if (code.Length > 16) code = code.Substring(0, 16);

            string title = module + ": " + ex.GetType().Name + " [0x" + ex.HResult.ToString("X8") + "]";
            if (title.Length > 120) title = title.Substring(0, 120);

            Dictionary<string, object> payload = BasePayload("error");
            payload["error_code"] = code;
            payload["error_title"] = title;
            payload["error_module"] = module;
            Queue(payload);
        }

        private static Dictionary<string, object> BasePayload(string eventName)
        {
            var d = new Dictionary<string, object>();
            d["install_id"] = InstallId;
            d["session_id"] = SessionId;
            d["event"] = eventName;
            d["version"] = FullVersion();
            d["os"] = OsName();
            d["lang"] = Lang.IsEn ? "en" : "ru";
            return d;
        }

        private static void SendEvent(string eventName, bool synchronous)
        {
            if (!Enabled || !Configured) return;
            Dictionary<string, object> payload = BasePayload(eventName);
            if (synchronous) Send(payload);
            else Queue(payload);
        }

        private static void Queue(Dictionary<string, object> payload)
        {
            try { ThreadPool.QueueUserWorkItem(delegate(object state) { Send((Dictionary<string, object>)state); }, payload); }
            catch { }
        }

        private static void Send(Dictionary<string, object> payload)
        {
            if (!Enabled || !Configured) return;

            // Не отправляем больше одного запроса одновременно: heartbeat не
            // должен копиться в очереди на медленной сети.
            if (Interlocked.CompareExchange(ref sending, 1, 0) != 0) return;
            try
            {
                string endpoint = Endpoint;
                if (endpoint.Length == 0) return;

                var request = (HttpWebRequest)WebRequest.Create(endpoint + "/api/v1/event");
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.UserAgent = "DeepTools/" + FullVersion();
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;
                request.KeepAlive = false;

                byte[] body = Encoding.UTF8.GetBytes(json.Serialize(payload));
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    // Тело ответа не нужно, но поток надо закрыть
                    using (Stream stream = response.GetResponseStream())
                        if (stream != null) stream.Close();
                }
                lock (sync) { lastError = ""; }
            }
            catch (Exception ex)
            {
                lock (sync) { lastError = ex.GetType().Name + ": " + ex.Message; }
            }
            finally
            {
                Interlocked.Exchange(ref sending, 0);
            }
        }

        // Лёгкий запрос для главной страницы: только «сколько людей сейчас
        // пользуются DeepTools». Полную статистику обычный клиент не запрашивает.
        // Возвращает -1, если сервер не настроен или недоступен.
        public static int FetchOnlineCount()
        {
            string endpoint = Endpoint;
            if (endpoint.Length == 0) return -1;

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(endpoint + "/api/v1/online");
                request.Method = "GET";
                request.UserAgent = "DeepTools/" + FullVersion();
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                request.KeepAlive = false;

                string text;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    text = reader.ReadToEnd();

                var root = json.DeserializeObject(text) as Dictionary<string, object>;
                if (root == null || !ToBool(root, "ok")) return -1;
                return ToInt(root, "online_now");
            }
            catch { return -1; }
        }

        // Синхронно забирает статистику для панели. Вызывать из фонового потока.
        public static OnlineStats FetchStats()
        {
            var stats = new OnlineStats();
            string endpoint = Endpoint;
            if (endpoint.Length == 0)
            {
                stats.Error = Lang.T("Сервер статистики не настроен", "Stats server is not configured");
                return stats;
            }

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(endpoint + "/api/v1/stats");
                request.Method = "GET";
                request.UserAgent = "DeepTools/" + FullVersion();
                request.Timeout = 10000;
                request.ReadWriteTimeout = 10000;
                request.KeepAlive = false;

                string token = ViewToken;
                if (token.Length > 0)
                    request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;

                string text;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    text = reader.ReadToEnd();

                var root = json.DeserializeObject(text) as Dictionary<string, object>;
                if (root == null) throw new Exception("bad json");

                stats.Ok = ToBool(root, "ok");
                stats.GeneratedAt = ToLong(root, "generated_at");
                stats.OnlineNow = ToInt(root, "online_now");
                stats.Online24h = ToInt(root, "online_24h");
                stats.UsersTotal = ToInt(root, "users_total");
                stats.LaunchesToday = ToInt(root, "launches_today");
                stats.LaunchesTotal = ToInt(root, "launches_total");
                stats.Errors24h = ToInt(root, "errors_24h");
                stats.ErrorsTotal = ToInt(root, "errors_total");
                stats.UsersWithErrors24h = ToInt(root, "users_with_errors_24h");

                foreach (object o in AsList(Get(root, "top_errors")))
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var e = new OnlineErrorStat();
                    e.Code = ToStringValue(d, "error_code");
                    e.Title = ToStringValue(d, "error_title");
                    e.Module = ToStringValue(d, "error_module");
                    e.Count = ToInt(d, "count");
                    e.Users = ToInt(d, "users");
                    e.LastSeen = ToLong(d, "last_seen");
                    stats.TopErrors.Add(e);
                }

                foreach (object o in AsList(Get(root, "versions")))
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var v = new OnlineVersionStat();
                    v.Version = ToStringValue(d, "version");
                    v.Users = ToInt(d, "users");
                    v.Launches = ToInt(d, "launches");
                    stats.Versions.Add(v);
                }

                foreach (object o in AsList(Get(root, "countries")))
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var c = new OnlineCountryStat();
                    c.Country = ToStringValue(d, "country");
                    c.Users = ToInt(d, "users");
                    stats.Countries.Add(c);
                }
            }
            catch (Exception ex)
            {
                stats.Ok = false;
                var web = ex as WebException;
                var response = web == null ? null : web.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.Forbidden)
                {
                    stats.Error = Lang.T(
                        "Сервер требует ключ просмотра. Собери DeepToolsStats.exe с -StatsToken.",
                        "The server requires a view token. Build DeepToolsStats.exe with -StatsToken.");
                }
                else
                {
                    stats.Error = ex.Message;
                }
            }

            return stats;
        }

        public static void OpenPrivacyPolicy()
        {
            PrivacyPolicy.OpenPolicy(null);
        }

        private static string FullVersion()
        {
            try { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
            catch { return "0.0.0.0"; }
        }

        private static string OsName()
        {
            string name = "Windows";
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        string product = k.GetValue("ProductName") as string;
                        string display = k.GetValue("DisplayVersion") as string;
                        string build = k.GetValue("CurrentBuildNumber") as string;
                        if (!string.IsNullOrEmpty(product)) name = product;
                        if (!string.IsNullOrEmpty(display)) name += " " + display;
                        if (!string.IsNullOrEmpty(build)) name += " (" + build + ")";
                    }
                }
            }
            catch { }
            if (name.Length > 64) name = name.Substring(0, 64);
            return name;
        }

        private static string Sha256Hex(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // ---- Аккуратные помощники для JSON ----

        private static object Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        private static IList AsList(object value)
        {
            if (value == null) return new object[0];
            var list = value as IList;
            if (list != null) return list;
            return new object[0];
        }

        private static string ToStringValue(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            return v == null ? "" : Convert.ToString(v);
        }

        private static int ToInt(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        private static long ToLong(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null) return 0;
            try { return Convert.ToInt64(v); } catch { return 0; }
        }

        private static bool ToBool(Dictionary<string, object> d, string key)
        {
            object v = Get(d, key);
            if (v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }
    }
}
