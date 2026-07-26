using System;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

namespace DeepTools
{
    // Проверка обновлений через GitHub Releases.
    // Спрашиваем API последний релиз, сравниваем тег (v1.2.3) с версией сборки.
    // Есть новее - уведомление из трея и предложение открыть страницу загрузки.
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
            worker.DoWork += (s, e) => { e.Result = FetchLatestTag(); };
            worker.RunWorkerCompleted += (s, e) => {
                string latestTag = e.Result as string;

                if (string.IsNullOrEmpty(latestTag))
                {
                    if (!silent && callback != null)
                        callback(Lang.T("Не удалось проверить (нет сети или репозиторий недоступен)", "Check failed (no network or repo unavailable)"));
                    return;
                }

                Version latest = ParseVersion(latestTag);
                Version current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

                if (latest != null && latest > current)
                {
                    TrayNotify.Info(
                        Lang.T("Доступно обновление ", "Update available ") + latestTag,
                        Lang.T("Нажми сюда, чтобы открыть страницу загрузки", "Click here to open the download page"),
                        () => OpenReleasesPage());
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

        private static string FetchLatestTag()
        {
            try
            {
                // GitHub требует TLS 1.2+, а .NET Framework по умолчанию может ходить старым
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                var request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                request.UserAgent = "DeepTools-UpdateChecker";
                request.Timeout = 10000;

                using (var response = request.GetResponse())
                using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
                {
                    string json = reader.ReadToEnd();
                    Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    return m.Success ? m.Groups[1].Value : null;
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
}
