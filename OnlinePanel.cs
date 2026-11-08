using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

#if DT_STATS_VIEWER
namespace DeepTools
{
    // Полная панель «Онлайн DeepTools» для DeepToolsStats.exe: живая статистика
    // сообщества с сервера телеметрии. В публичную сборку не попадает.
    public class OnlinePanel : Panel
    {
        private Label statusDot;
        private Label statusLabel;
        private Label endpointLabel;
        private Label onlineNowValue;
        private Label online24Value;
        private Label usersValue;
        private Label launchesTodayValue;
        private Label launchesTotalValue;
        private Label errors24Value;
        private Label affectedValue;
        private Label errorsTotalValue;
        private Panel errorRows;
        private Label versionsLabel;
        private RoundedButton refreshBtn;
        private ToggleSwitch telemetryToggle;
        private System.Windows.Forms.Timer refreshTimer;
        private bool busy;
        private bool firstLoad = true;
        private readonly bool viewerMode;

        public OnlinePanel() : this(false) { }

        public OnlinePanel(bool viewer)
        {
            viewerMode = viewer;
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;
            BuildUi();

            refreshTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            refreshTimer.Tick += (s, e) => { if (Visible && !busy) RefreshStats(); };
            VisibleChanged += (s, e) =>
            {
                if (Visible)
                {
                    refreshTimer.Start();
                    if (firstLoad)
                    {
                        firstLoad = false;
                        RefreshStats();
                    }
                }
                else refreshTimer.Stop();
            };
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = Lang.T("Онлайн DeepTools", "DeepTools Online"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                Location = new Point(24, 20),
                AutoSize = true
            };
            Controls.Add(title);

            var subtitle = new Label
            {
                Text = viewerMode
                    ? Lang.T(
                        "Полная панель для автора и помощников. Показывает онлайн, запуски и ошибки сообщества.",
                        "Full dashboard for the author and helpers. Shows community online, launches and errors.")
                    : Lang.T(
                        "Анонимная статистика сообщества: кто сейчас онлайн, сколько запусков и какие ошибки чаще всего встречаются.",
                        "Anonymous community stats: who is online now, how many launches and which errors happen most often."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(26, 52),
                Size = new Size(700, 20),
                AutoEllipsis = true
            };
            Controls.Add(subtitle);

            // ---- Статус сервера ----
            var statusCard = Theme.MakeCard(this, new Point(24, 82), new Size(712, 104));

            statusDot = new Label
            {
                Text = "●",
                ForeColor = Theme.Warning,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 14F),
                Location = new Point(16, 12),
                Size = new Size(20, 22)
            };
            statusCard.Controls.Add(statusDot);

            statusLabel = new Label
            {
                Text = Lang.T("Проверяю сервер…", "Checking the server…"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(42, 14),
                Size = new Size(470, 20),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(statusLabel);

            endpointLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(44, 38),
                Size = new Size(480, 18),
                AutoEllipsis = true
            };
            statusCard.Controls.Add(endpointLabel);

            refreshBtn = new RoundedButton
            {
                Text = Lang.T("Обновить", "Refresh"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(532, 12),
                Size = new Size(158, 30)
            };
            refreshBtn.Click += (s, e) => RefreshStats();
            statusCard.Controls.Add(refreshBtn);

            var privacyBtn = new RoundedButton
            {
                Text = Lang.T("Политика", "Privacy"),
                ButtonColor = Theme.InputColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextDim,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(18, 68),
                Size = new Size(140, 26)
            };
            privacyBtn.Click += (s, e) => Telemetry.OpenPrivacyPolicy();
            statusCard.Controls.Add(privacyBtn);

            var telemetryLabel = new Label
            {
                Text = viewerMode
                    ? Lang.T("Режим просмотра", "Viewer mode")
                    : Lang.T("Анонимная телеметрия", "Anonymous telemetry"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(420, 72),
                Size = new Size(180, 20),
                TextAlign = ContentAlignment.MiddleRight
            };
            statusCard.Controls.Add(telemetryLabel);

            telemetryToggle = new ToggleSwitch
            {
                Location = new Point(610, 68),
                Checked = Telemetry.Enabled,
                Visible = !viewerMode
            };
            telemetryToggle.CheckedChanged += (s, e) =>
            {
                Telemetry.Enabled = telemetryToggle.Checked;
                if (telemetryToggle.Checked)
                {
                    Telemetry.Start();
                    RefreshStats();
                }
                else
                {
                    statusDot.ForeColor = Theme.Warning;
                    statusLabel.ForeColor = Theme.TextMain;
                    statusLabel.Text = Lang.T("Анонимная статистика выключена", "Anonymous stats are off");
                    endpointLabel.Text = Lang.T("Данные на сервер не отправляются.", "Nothing is sent to the server.");
                }
            };
            statusCard.Controls.Add(telemetryToggle);

            // ---- Плитки ----
            MakeStatTile(new Point(24, 198), Lang.T("Сейчас онлайн", "Online now"), out onlineNowValue);
            MakeStatTile(new Point(206, 198), Lang.T("За 24 часа", "Last 24 hours"), out online24Value);
            MakeStatTile(new Point(388, 198), Lang.T("Всего пользователей", "Total users"), out usersValue);
            MakeStatTile(new Point(570, 198), Lang.T("Запусков сегодня", "Launches today"), out launchesTodayValue);
            MakeStatTile(new Point(24, 290), Lang.T("Всего запусков", "Total launches"), out launchesTotalValue);
            MakeStatTile(new Point(206, 290), Lang.T("Ошибок за 24 часа", "Errors in 24h"), out errors24Value);
            MakeStatTile(new Point(388, 290), Lang.T("Людей с ошибками", "Users with errors"), out affectedValue);
            MakeStatTile(new Point(570, 290), Lang.T("Всего ошибок", "Total errors"), out errorsTotalValue);

            // ---- Топ ошибок ----
            var errorsCard = Theme.MakeCard(this, new Point(24, 388), new Size(712, 148));

            var errorsTitle = new Label
            {
                Text = Lang.T("Топ ошибок за 24 часа", "Top errors in the last 24 hours"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            errorsCard.Controls.Add(errorsTitle);

            var errorsHint = new Label
            {
                Text = Lang.T("Группировка по модулю и типу исключения. Текст исключения и пути не отправляются.",
                              "Grouped by module and exception type. Exception text and paths are never sent."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.8F),
                Location = new Point(16, 34),
                Size = new Size(670, 16),
                AutoEllipsis = true
            };
            errorsCard.Controls.Add(errorsHint);

            errorRows = new Panel
            {
                Location = new Point(16, 56),
                Size = new Size(680, 82),
                BackColor = Color.Transparent
            };
            errorsCard.Controls.Add(errorRows);

            versionsLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 546),
                Size = new Size(712, 30),
                AutoEllipsis = true
            };
            Controls.Add(versionsLabel);

            var footer = new Label
            {
                Text = Lang.T("Данные обновляются каждые 30 секунд. Статистика анонимная — подробности в PRIVACY.md.",
                              "Refreshes every 30 seconds. Stats are anonymous — see PRIVACY.md."),
                ForeColor = Theme.Lerp(Theme.BgColor, Theme.TextDim, 0.65f),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.8F),
                Location = new Point(24, 588),
                Size = new Size(712, 18),
                AutoEllipsis = true
            };
            Controls.Add(footer);
        }

        private void MakeStatTile(Point loc, string title, out Label valueLabel)
        {
            var card = Theme.MakeCard(this, loc, new Size(166, 84));

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(14, 12),
                AutoSize = true
            };
            card.Controls.Add(titleLbl);

            valueLabel = new Label
            {
                Text = "—",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(12, 36),
                Size = new Size(142, 30),
                AutoEllipsis = true
            };
            card.Controls.Add(valueLabel);
        }

        private void RefreshStats()
        {
            if (busy) return;

            if (!Telemetry.Configured)
            {
                ShowNotConfigured();
                return;
            }

            busy = true;
            refreshBtn.Enabled = false;
            statusDot.ForeColor = Theme.Warning;
            statusLabel.ForeColor = Theme.TextMain;
            statusLabel.Text = Lang.T("Обновляю статистику…", "Refreshing stats…");
            endpointLabel.Text = Telemetry.Endpoint;

            var worker = new BackgroundWorker();
            worker.DoWork += (s, e) => e.Result = Telemetry.FetchStats();
            worker.RunWorkerCompleted += (s, e) =>
            {
                busy = false;
                if (IsDisposed) return;
                refreshBtn.Enabled = true;

                if (e.Error != null)
                {
                    ShowError(e.Error.Message);
                    return;
                }

                OnlineStats stats = e.Result as OnlineStats;
                if (stats == null) { ShowError("empty response"); return; }
                if (!stats.Ok) { ShowError(stats.Error); return; }
                Apply(stats);
            };
            worker.RunWorkerAsync();
        }

        private void ShowNotConfigured()
        {
            statusDot.ForeColor = Theme.Warning;
            statusLabel.ForeColor = Theme.TextMain;
            statusLabel.Text = Lang.T("Сервер статистики не настроен", "Stats server is not configured");
            endpointLabel.Text = Lang.T(
                "Собери программу с параметром -TelemetryUrl.",
                "Build the app with the -TelemetryUrl parameter.");
            SetAllValues("—");
            ClearErrorRows();
            AddErrorRow(Lang.T(
                "Чтобы видеть онлайн и ошибки, укажи адрес сервера при сборке.",
                "To see online users and errors, set the server address at build time."), "", Theme.TextDim);
            versionsLabel.Text = "";
        }

        private void ShowError(string message)
        {
            statusDot.ForeColor = Theme.Danger;
            statusLabel.ForeColor = Theme.TextMain;
            statusLabel.Text = Lang.T("Нет связи с сервером", "Cannot reach the server");
            endpointLabel.Text = string.IsNullOrEmpty(message) ? Telemetry.Endpoint : message;
            SetAllValues("—");
            ClearErrorRows();
            AddErrorRow(Lang.T("Проверь адрес сервера и интернет.",
                               "Check the server address and your connection."), "", Theme.TextDim);
            versionsLabel.Text = "";
        }

        private void Apply(OnlineStats stats)
        {
            statusDot.ForeColor = Theme.On;
            statusLabel.ForeColor = Theme.TextMain;
            statusLabel.Text = Lang.T("Сервер онлайн", "Server is online");
            endpointLabel.Text = Telemetry.Endpoint + "  ·  " +
                DateTime.Now.ToString("HH:mm:ss");

            onlineNowValue.Text = Format(stats.OnlineNow);
            online24Value.Text = Format(stats.Online24h);
            usersValue.Text = Format(stats.UsersTotal);
            launchesTodayValue.Text = Format(stats.LaunchesToday);
            launchesTotalValue.Text = Format(stats.LaunchesTotal);
            errors24Value.Text = Format(stats.Errors24h);
            affectedValue.Text = Format(stats.UsersWithErrors24h);
            errorsTotalValue.Text = Format(stats.ErrorsTotal);

            ClearErrorRows();
            if (stats.TopErrors.Count == 0)
            {
                AddErrorRow(Lang.T("За последние 24 часа ошибок нет.", "No errors in the last 24 hours."), "", Theme.On);
            }
            else
            {
                int rows = Math.Min(5, stats.TopErrors.Count);
                for (int i = 0; i < rows; i++)
                {
                    OnlineErrorStat err = stats.TopErrors[i];
                    string title = string.IsNullOrEmpty(err.Title) ? err.Module : err.Title;
                    string counts = err.Users + Lang.T(" чел. · ", " users · ") + err.Count + Lang.T(" раз", " times");
                    AddErrorRow(title, counts, Theme.TextMain);
                }
            }

            versionsLabel.Text = BuildVersionsLine(stats);
        }

        private void SetAllValues(string text)
        {
            onlineNowValue.Text = text;
            online24Value.Text = text;
            usersValue.Text = text;
            launchesTodayValue.Text = text;
            launchesTotalValue.Text = text;
            errors24Value.Text = text;
            affectedValue.Text = text;
            errorsTotalValue.Text = text;
        }

        private void ClearErrorRows()
        {
            while (errorRows.Controls.Count > 0)
            {
                Control c = errorRows.Controls[0];
                errorRows.Controls.RemoveAt(0);
                try { c.Dispose(); } catch { }
            }
        }

        private void AddErrorRow(string title, string counts, Color color)
        {
            int y = errorRows.Controls.Count * 20;

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(0, y),
                Size = new Size(510, 18),
                AutoEllipsis = true
            };
            errorRows.Controls.Add(titleLbl);

            if (!string.IsNullOrEmpty(counts))
            {
                var countLbl = new Label
                {
                    Text = counts,
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.5F),
                    Location = new Point(516, y),
                    Size = new Size(160, 18),
                    TextAlign = ContentAlignment.MiddleRight
                };
                errorRows.Controls.Add(countLbl);
            }
        }

        private static string BuildVersionsLine(OnlineStats stats)
        {
            var versions = new List<string>();
            for (int i = 0; i < stats.Versions.Count && i < 4; i++)
                versions.Add(stats.Versions[i].Version + " — " + stats.Versions[i].Users);

            var countries = new List<string>();
            for (int i = 0; i < stats.Countries.Count && i < 4; i++)
                countries.Add(stats.Countries[i].Country + " — " + stats.Countries[i].Users);

            string v = versions.Count == 0 ? "—" : string.Join(", ", versions.ToArray());
            string c = countries.Count == 0 ? "—" : string.Join(", ", countries.ToArray());
            return Lang.T("Версии: ", "Versions: ") + v +
                   "    ·    " + Lang.T("Страны: ", "Countries: ") + c;
        }

        private static string Format(int value)
        {
            try { return value.ToString("N0"); }
            catch { return value.ToString(); }
        }
    }
}
#endif
