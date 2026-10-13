using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace DeepTools
{
    public static class UpdateChecker
    {
        private const string DefaultRepo = "dep1xar/DeepTools";

        // Разрешённые репозитории для автообновления — защита от подмены через config.txt
        private static readonly string[] AllowedRepos = new string[]
        {
            "dep1xar/DeepTools",
        };

        public static string Repo
        {
            get
            {
                string v = AppConfig.Get("github_repo", DefaultRepo);
                // Только разрешённые репо; формат owner/repo с безопасными символами
                if (!IsAllowedRepo(v)) return DefaultRepo;
                return v;
            }
        }

        private static bool IsAllowedRepo(string repo)
        {
            if (string.IsNullOrWhiteSpace(repo)) return false;
            string r = repo.Trim();
            // формат owner/name, только буквы/цифры/дефис/подчёркивание/точка
            if (!Regex.IsMatch(r, @"^[a-zA-Z0-9_.\-]+/[a-zA-Z0-9_.\-]+$")) return false;
            foreach (string a in AllowedRepos)
                if (string.Equals(r, a, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string ReleasesUrl
        {
            get { return "https://github.com/" + Repo + "/releases/latest"; }
        }

        public static void CheckInBackground(bool silent, Action<string> callback)
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => { e.Result = FetchLatestRelease(); };
            worker.RunWorkerCompleted += (s, e) => {
                string[] release = e.Result as string[];

                if (release == null || string.IsNullOrEmpty(release[0]))
                {
                    if (!silent && callback != null)
                        callback(Lang.T("Не удалось проверить (нет сети или репозиторий недоступен)", "Check failed (no network or repo unavailable)"));
                    return;
                }

                string latestTag = release[0];
                string assetUrl = release[1];

                Version latest = ParseVersion(latestTag);
                Version current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

                if (latest != null && latest > current)
                {
                    TrayNotify.Info(
                        Lang.T("Доступно обновление ", "Update available ") + latestTag,
                        assetUrl != ""
                            ? Lang.T("Нажми сюда, чтобы скачать и установить", "Click here to download and install")
                            : Lang.T("Нажми сюда, чтобы открыть страницу загрузки", "Click here to open the download page"),
                        () => {
                            if (assetUrl != "") SelfUpdater.DownloadAndInstall(assetUrl, latestTag);
                            else OpenReleasesPage();
                        });
                    if (callback != null)
                        callback(Lang.T("Доступна новая версия: ", "New version available: ") + latestTag);
                }
                else
                {
                    if (!silent && callback != null)
                        callback(Lang.T("У тебя последняя версия ✓", "You are on the latest version ✓"));
                }
            };
            worker.RunWorkerAsync();
        }

        private static string[] FetchLatestRelease()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                request.UserAgent = "DeepTools-UpdateChecker";
                request.Timeout = 10000;

                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    string json = reader.ReadToEnd();
                    Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    if (!tag.Success) return null;

                    Match asset = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]+\\.exe)\"");
                    string url = asset.Success ? asset.Groups[1].Value : "";
                    // Проверяем что URL указывает на github.com и на разрешённый репо
                    if (!string.IsNullOrEmpty(url))
                    {
                        if (!IsValidAssetUrl(url)) url = "";
                    }
                    return new string[] { tag.Groups[1].Value, url };
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool IsValidAssetUrl(string url)
        {
            try
            {
                Uri u = new Uri(url);
                if (u.Scheme != "https") return false;
                string host = u.Host.ToLowerInvariant();
                if (host != "github.com" && !host.EndsWith(".github.com") && host != "objects.githubusercontent.com" && !host.EndsWith(".githubusercontent.com") && host != "release-assets.githubusercontent.com")
                    return false;
                return true;
            }
            catch { return false; }
        }

        private static Version ParseVersion(string tag)
        {
            try
            {
                string clean = tag.TrimStart('v', 'V');
                string[] parts = clean.Split('.');
                int major = parts.Length > 0 ? int.Parse(parts[0]) : 0;
                int minor = parts.Length > 1 ? int.Parse(parts[1]) : 0;
                int build = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                return new Version(major, minor, build, 0);
            }
            catch
            {
                return null;
            }
        }

        public static void OpenReleasesPage()
        {
            try { Process.Start(ReleasesUrl); } catch { }
        }
    }

    public static class SelfUpdater
    {
        private static int busy = 0;
        private static readonly object updateLock = new object();

        public static void DownloadAndInstall(string url, string tag)
        {
            // атомарная защита от двойного запуска
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;

            TrayNotify.Info(Lang.T("Скачиваю обновление ", "Downloading update ") + tag + "…", "");

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                // Случайное имя в TEMP — защита от предсказуемого перехвата
                string path = Path.Combine(Path.GetTempPath(), "DeepTools_update_" + Guid.NewGuid().ToString("N") + ".exe");
                // Валидация URL перед скачиванием
                Uri uri;
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https")
                    throw new InvalidOperationException("Invalid update URL");
                string host = uri.Host.ToLowerInvariant();
                if (host != "github.com" && !host.EndsWith(".github.com") && host != "objects.githubusercontent.com" && !host.EndsWith(".githubusercontent.com") && host != "release-assets.githubusercontent.com")
                    throw new InvalidOperationException("Update host not allowed");

                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "DeepTools-Updater");
                    wc.DownloadFile(url, path);
                }
                e.Result = path;
            };
            worker.RunWorkerCompleted += (s, e) => {
                Interlocked.Exchange(ref busy, 0);

                if (e.Error != null)
                {
                    DTDialog.Show(
                        Lang.T("Не удалось скачать обновление: ", "Failed to download update: ") + e.Error.Message,
                        "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    UpdateChecker.OpenReleasesPage();
                    return;
                }

                string path = e.Result as string;
                long size = 0;
                try { if (path != null) size = new FileInfo(path).Length; } catch { }

                if (path == null || size < 1024 * 1024)
                {
                    try { if (path != null && File.Exists(path)) File.Delete(path); } catch { }
                    DTDialog.Show(
                        Lang.T("Не удалось скачать обновление. Открой страницу релизов и скачай вручную.",
                               "Failed to download the update. Open the releases page and download manually."),
                        "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    UpdateChecker.OpenReleasesPage();
                    return;
                }

                // Проверка что скачанный файл — валидный PE (начинается с MZ) — базовая защита от HTML-ошибок
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        byte[] header = new byte[2];
                        if (fs.Read(header, 0, 2) != 2 || header[0] != 0x4D || header[1] != 0x5A)
                        {
                            File.Delete(path);
                            DTDialog.Show(
                                Lang.T("Скачанный файл не является исполняемым. Попробуй скачать вручную.",
                                       "Downloaded file is not executable. Try manual download."),
                                "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            UpdateChecker.OpenReleasesPage();
                            return;
                        }
                    }
                }
                catch { }

                DialogResult r = DTDialog.Show(
                    Lang.T("Обновление ", "Update ") + tag + Lang.T(" скачано. Установить и перезапустить DeepTools сейчас?",
                           " downloaded. Install and restart DeepTools now?"),
                    Lang.T("Обновление готово", "Update ready"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (r == DialogResult.Yes) Apply(path);
                else { try { File.Delete(path); } catch { } }
            };
            worker.RunWorkerAsync();
        }

        private static void Apply(string updatePath)
        {
            try
            {
                string target = Application.ExecutablePath;
                int pid = Process.GetCurrentProcess().Id;

                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":wait");
                sb.AppendLine("tasklist /fi \"PID eq " + pid + "\" 2>nul | find \"" + pid + "\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)");
                sb.AppendLine("move /y \"" + updatePath + "\" \"" + target + "\"");
                sb.AppendLine("start \"\" \"" + target + "\"");
                sb.AppendLine("del \"%~f0\"");

                string bat = Path.Combine(Path.GetTempPath(), "deeptools_update_" + Guid.NewGuid().ToString("N") + ".cmd");

                Encoding enc;
                try { enc = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { enc = Encoding.Default; }
                File.WriteAllText(bat, sb.ToString(), enc);

                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"")
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                Application.Exit();
            }
            catch (Exception ex)
            {
                DTDialog.Show(
                    Lang.T("Не удалось запустить установку: ", "Failed to start the install: ") + ex.Message,
                    "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
