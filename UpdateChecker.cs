using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DeepTools
{
    // Проверка обновлений через GitHub Releases.
    // Спрашиваем API последний релиз, сравниваем тег (v1.2.3) с версией сборки.
    // Есть новее - уведомление из трея; клик по нему скачивает exe и
    // устанавливает через самоподмену (SelfUpdater). Если у релиза нет
    // exe-ассета - фолбэк на открытие страницы загрузки.
    // Репозиторий можно переопределить в конфиге ключом github_repo
    public static class UpdateChecker
    {
        private const string DefaultRepo = "dep1xar/DeepTools";

        public static string Repo
        {
            get { return AppConfig.Get("github_repo", DefaultRepo); }
        }

        public static string ReleasesUrl
        {
            get { return "https://github.com/" + Repo + "/releases/latest"; }
        }

        // silent = true: молча, алерт только если есть обновление (автопроверка при старте).
        // silent = false: ручная проверка из настроек, ответ приходит в callback в любом случае
        public static void CheckInBackground(bool silent, Action<string> callback)
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => { e.Result = FetchLatestRelease(); };
            worker.RunWorkerCompleted += (s, e) => {
                string[] release = e.Result as string[]; // [тег, url exe-ассета или ""]

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

        // Возвращает [тег, url exe-ассета] или null. Url пустой, если exe к релизу не приложен
        private static string[] FetchLatestRelease()
        {
            try
            {
                // GitHub требует TLS 1.2+, а .NET Framework по умолчанию может ходить старым
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
                    return new string[] { tag.Groups[1].Value, asset.Success ? asset.Groups[1].Value : "" };
                }
            }
            catch
            {
                return null;
            }
        }

        // "v1.2.3" или "1.2.3" -> Version. Дополняем до четырёх чисел, чтобы сравнение не врало
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

    // Самообновление: качаем новый exe во временную папку, подтверждаем у юзера,
    // затем cmd-скрипт ждёт завершения процесса, подменяет exe и запускает новый.
    // Скрипт нужен потому, что Windows не даёт перезаписать запущенный exe
    public static class SelfUpdater
    {
        private static bool busy = false;

        public static void DownloadAndInstall(string url, string tag)
        {
            if (busy) return;
            busy = true;

            TrayNotify.Info(Lang.T("Скачиваю обновление ", "Downloading update ") + tag + "…", "");

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                string path = Path.Combine(Path.GetTempPath(), "DeepTools_update.exe");
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "DeepTools-Updater");
                    wc.DownloadFile(url, path);
                }
                e.Result = path;
            };
            worker.RunWorkerCompleted += (s, e) => {
                busy = false;

                string path = e.Error == null ? e.Result as string : null;
                long size = 0;
                try { if (path != null) size = new FileInfo(path).Length; } catch { }

                // Обрубленная загрузка или страница-ошибка вместо exe - не устанавливаем
                if (path == null || size < 1024 * 1024)
                {
                    DTDialog.Show(
                        Lang.T("Не удалось скачать обновление. Открой страницу релизов и скачай вручную.",
                               "Failed to download the update. Open the releases page and download manually."),
                        "DeepTools", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    UpdateChecker.OpenReleasesPage();
                    return;
                }

                DialogResult r = DTDialog.Show(
                    Lang.T("Обновление ", "Update ") + tag + Lang.T(" скачано. Установить и перезапустить DeepTools сейчас?",
                           " downloaded. Install and restart DeepTools now?"),
                    Lang.T("Обновление готово", "Update ready"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (r == DialogResult.Yes) Apply(path);
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

                string bat = Path.Combine(Path.GetTempPath(), "deeptools_update.cmd");

                // cmd читает батники в OEM-кодировке: без неё русские буквы в пути
                // (C:\Users\Пользователь\...) превращаются в кракозябры и move не находит файл
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

                Application.Exit(); // скрипт ждёт наш PID, подменит exe и перезапустит
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
