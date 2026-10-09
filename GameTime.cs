using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    // Трекер игровых сессий. GameBoosterPanel сообщает о найденной игре (детект уже есть),
    // трекер копит статистику (средний CPU, макс. температуры, FPS из PresentTracer),
    // а когда процесс игры завершается - шлёт отчёт в трей и дописывает сессию в журнал.
    // Журнал: AppData\DeepTools\gametime.log,
    // строка = имя|дата|секунды|срCPU|максCPU°|максGPU°|срFPS|1%low
    public static class GameSessionTracker
    {
        private const int MinSessionSec = 180; // случайные развороты окон на пару минут не считаем

        private static Process proc;
        private static int procId;
        private static string procName;
        private static DateTime start;
        private static double cpuSum;
        private static int cpuSamples;
        private static int maxCpuTemp = -1;
        private static int maxGpuTemp = -1;
        private static double fpsSum;
        private static int fpsSamples;
        private static int worstLowFps = -1; // худший 1% low за сессию = самый жёсткий статтер

        // Сейчас идёт игровая сессия? (автоочистка не трогает кэши шейдеров под игрой)
        public static bool IsGameRunning
        {
            get
            {
                if (proc == null) return false;
                try { return !proc.HasExited; } catch { return false; }
            }
        }

        public static string LogPath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools"),
                    "gametime.log");
            }
        }

        // Зовётся из детекта GameBooster, когда полноэкранная игра на переднем плане
        public static void OnGameDetected(Process p)
        {
            if (proc != null)
            {
                // Уже следим за игрой; новую начинаем, только если старая закрылась
                bool exited = true;
                try { exited = proc.HasExited; } catch { }
                if (!exited) return;
                Finish();
            }

            proc = p;
            try { procId = p.Id; } catch { procId = 0; }
            try { procName = p.ProcessName; } catch { procName = "game"; }
            start = DateTime.Now;
            cpuSum = 0;
            cpuSamples = 0;
            maxCpuTemp = -1;
            maxGpuTemp = -1;
            fpsSum = 0;
            fpsSamples = 0;
            worstLowFps = -1;
        }

        // Зовётся каждый тик таймера детекта (~1.5 сек), даже когда игра свёрнута
        public static void Tick()
        {
            if (proc == null) return;

            bool exited = true;
            try { exited = proc.HasExited; } catch { }
            if (exited)
            {
                Finish();
                return;
            }

            if (SystemStats.CpuLoadNum >= 0)
            {
                cpuSum += SystemStats.CpuLoadNum;
                cpuSamples++;
            }
            if (SystemStats.CpuTempNum > maxCpuTemp) maxCpuTemp = SystemStats.CpuTempNum;
            if (SystemStats.GpuTempNum > maxGpuTemp) maxGpuTemp = SystemStats.GpuTempNum;

            // FPS игры из ETW-трейсера (GameBooster держит его включённым).
            // Снапшот есть, только пока игра реально рисует - свёрнутая не портит среднее
            PresentTracer.Snap snap = PresentTracer.SnapshotForPid(procId);
            if (snap != null && snap.Fps > 0)
            {
                fpsSum += snap.Fps;
                fpsSamples++;
                if (snap.LowFps > 0 && (worstLowFps < 0 || snap.LowFps < worstLowFps))
                    worstLowFps = snap.LowFps;
            }

            // Тот же трейсер отдаёт сырые кадры: из них копится статистика «за
            // последние 30 минут / час / 2 часа» с микрофризами
            FrameLog.Tick(procId, procName);
        }

        private static void Finish()
        {
            string name = procName;
            double durationSec = (DateTime.Now - start).TotalSeconds;
            double avgCpu = cpuSamples > 0 ? cpuSum / cpuSamples : -1;
            int cpuT = maxCpuTemp;
            int gpuT = maxGpuTemp;
            int avgFps = fpsSamples > 0 ? (int)Math.Round(fpsSum / fpsSamples) : -1;
            int lowFps = worstLowFps;
            proc = null;
            procId = 0;

            // Отмечаем конец данных: в отчёте это разрыв, а не падение FPS до нуля
            FrameLog.MarkGap(name);

            if (durationSec < MinSessionSec) return;

            try
            {
                string dir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string line = name.Replace("|", "_") + "|" + start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + "|" + (int)durationSec + "|" + (avgCpu < 0 ? "-1" : avgCpu.ToString("0", CultureInfo.InvariantCulture))
                    + "|" + cpuT + "|" + gpuT + "|" + avgFps + "|" + lowFps;
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }

            // Отчёт после игры: сначала FPS (самое интересное), затем нагрев
            string stats = "";
            if (avgFps > 0) stats += Lang.T("средний FPS ", "avg FPS ") + avgFps;
            if (lowFps > 0) stats += (stats == "" ? "" : ", ") + "1% low " + lowFps;
            if (avgCpu >= 0) stats += (stats == "" ? "" : ", ") + Lang.T("средний CPU ", "avg CPU ") + avgCpu.ToString("0") + "%";
            if (cpuT > 0) stats += (stats == "" ? "" : ", ") + Lang.T("макс. CPU ", "max CPU ") + cpuT + "°C";
            if (gpuT > 0) stats += (stats == "" ? "" : ", ") + Lang.T("макс. GPU ", "max GPU ") + gpuT + "°C";

            TrayNotify.Info(
                name + " — " + FormatDuration((int)durationSec),
                stats == "" ? Lang.T("Сессия записана в статистику", "Session saved to statistics") : stats);
        }

        public static string FormatDuration(int seconds)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            if (h > 0) return h + Lang.T(" ч ", " h ") + m + Lang.T(" мин", " min");
            if (m > 0) return m + Lang.T(" мин", " min");
            return seconds + Lang.T(" сек", " s");
        }
    }

    // Окно статистики: сколько времени в какой игре, всего и за неделю
    public class GameTimeForm : Form
    {
        private class GameStat
        {
            public string Name;
            public int TotalSec;
            public int WeekSec;
            public int Sessions;
            public DateTime LastPlayed;
            public double FpsWeightedSum; // средний FPS сессии * её длительность
            public int FpsSec;            // сумма длительностей сессий, где FPS был замерен
        }

        private Point dragStart;
        private bool draggingForm = false;

        private FlowLayoutPanel list;
        private Label summaryLabel;
        private RoundedButton purgeBtn;
        private Label ignoreLabel;
        private int totalAll;
        private int totalWeek;

        public GameTimeForm()
        {
            Text = "DeepTools Game Time";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(560, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            var path = new GraphicsPath();
            int r = 12, d = r * 2;
            var rect = new Rectangle(0, 0, Width, Height);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        private void BuildUi()
        {
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.BgColor };
            titleBar.MouseDown += (s, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) => {
                if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("⏱ Время в играх", "⏱ Game time"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            };
            titleBar.Controls.Add(titleLbl);

            var closeBtn = new Label
            {
                Text = "✕",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F),
                Size = new Size(30, 26),
                Location = new Point(Width - 42, 7),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => Close();
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            List<GameStat> stats = LoadStats();

            totalAll = 0;
            totalWeek = 0;
            foreach (GameStat g in stats) { totalAll += g.TotalSec; totalWeek += g.WeekSec; }

            // Отчёт за последние 30 минут / час / 2 часа с микрофризами
            var statsBtn = new RoundedButton
            {
                Text = Lang.T("▤ Отчёт", "▤ Report"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(Width - 312, 6),
                Size = new Size(128, 28)
            };
            statsBtn.Click += (s, e) => { using (var f = new GameStatsForm()) f.ShowDialog(this); };
            DarkTip.Set(statsBtn, Lang.T("Кадры за последние 30 минут, час или 2 часа",
                                        "Frames for the last 30 minutes, hour or 2 hours"));
            titleBar.Controls.Add(statsBtn);

            // PNG-карточка со спекой и топом игр - в буфер и в Изображения\DeepTools
            var shareBtn = new RoundedButton
            {
                Text = Lang.T("⧉ Поделиться", "⧉ Share"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(Width - 178, 6),
                Size = new Size(126, 28),
                Enabled = stats.Count > 0
            };
            shareBtn.Click += (s, e) => {
                shareBtn.Enabled = false;
                shareBtn.Text = Lang.T("Создаём...", "Creating...");

                var lines = new List<ShareCard.GameLine>();
                foreach (GameStat g in stats)
                {
                    lines.Add(new ShareCard.GameLine
                    {
                        Name = g.Name,
                        TotalSec = g.TotalSec,
                        AvgFps = g.FpsSec > 0 ? (int)Math.Round(g.FpsWeightedSum / g.FpsSec) : -1
                    });
                }
                ShareCard.CreateGameTimeCard(lines, totalAll, totalWeek, path => {
                    shareBtn.Enabled = true;
                    shareBtn.Text = Lang.T("⧉ Поделиться", "⧉ Share");
                    if (path != null)
                        TrayNotify.Info(
                            Lang.T("Карточка готова", "Card is ready"),
                            Lang.T("Скопирована в буфер и сохранена. Нажми, чтобы открыть папку", "Copied to clipboard and saved. Click to open the folder"),
                            () => { try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { } });
                    else
                        TrayNotify.Info(Lang.T("Не получилось создать карточку", "Failed to create the card"), "");
                });
            };
            titleBar.Controls.Add(shareBtn);

            summaryLabel = new Label
            {
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(18, 48),
                Size = new Size(524, 34)
            };
            Controls.Add(summaryLabel);

            var card = Theme.MakeCard(this, new Point(16, 88), new Size(528, 368));

            var header = new Label
            {
                Text = Lang.T("Игра", "Game"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(16, 10),
                AutoSize = true
            };
            card.Controls.Add(header);

            var headerTotal = new Label
            {
                Text = Lang.T("Всего / за неделю", "Total / this week"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(270, 10),
                AutoSize = true
            };
            card.Controls.Add(headerTotal);

            list = new FlowLayoutPanel
            {
                Location = new Point(10, 30),
                Size = new Size(508, 328),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            // Низ окна: чистка статистики от не-игр и список игнорируемых
            purgeBtn = new RoundedButton
            {
                Text = Lang.T("♻ Убрать не-игры", "♻ Remove non-games"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Location = new Point(16, 466),
                Size = new Size(170, 28)
            };
            purgeBtn.Click += (s, e) => PurgeNonGames();
            Controls.Add(purgeBtn);

            var ignoreBtn = new RoundedButton
            {
                Text = Lang.T("⚙ Кого не считаем игрой", "⚙ Ignored apps"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Location = new Point(194, 466),
                Size = new Size(196, 28)
            };
            ignoreBtn.Click += (s, e) =>
            {
                using (var f = new GameIgnoreForm()) f.ShowDialog(this);
                RebuildList();
            };
            Controls.Add(ignoreBtn);

            ignoreLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(398, 470),
                Size = new Size(146, 20),
                TextAlign = ContentAlignment.MiddleRight,
                AutoEllipsis = true
            };
            Controls.Add(ignoreLabel);

            RebuildList();
        }

        // Перестраивает список игр и подписи: вызывается после удаления записей
        // или изменения игнор-листа
        private void RebuildList()
        {
            if (list == null) return;
            List<GameStat> stats = LoadStats();

            totalAll = 0;
            totalWeek = 0;
            foreach (GameStat g in stats) { totalAll += g.TotalSec; totalWeek += g.WeekSec; }

            summaryLabel.Text = stats.Count == 0
                ? Lang.T("Пока пусто. Сессии от 3 минут записываются автоматически, когда GameBooster видит игру",
                         "Nothing yet. Sessions over 3 minutes are recorded automatically when GameBooster detects a game")
                : Lang.T("Всего: ", "Total: ") + GameSessionTracker.FormatDuration(totalAll)
                    + Lang.T("   За 7 дней: ", "   Last 7 days: ") + GameSessionTracker.FormatDuration(totalWeek);
            summaryLabel.ForeColor = stats.Count == 0 ? Theme.TextDim : Theme.Accent;

            int nonGames = GameFilter.CountInLog();
            purgeBtn.Visible = nonGames > 0;
            purgeBtn.Text = Lang.T("♻ Убрать не-игры (", "♻ Remove non-games (") + nonGames + ")";

            List<string> ignored = GameFilter.UserIgnored();
            ignoreLabel.Text = ignored.Count == 0
                ? Lang.T("игнор: стандартный список", "ignored: default list")
                : Lang.T("игнор: ", "ignored: ") + string.Join(", ", ignored.ToArray());

            list.SuspendLayout();
            list.Controls.Clear();

            foreach (GameStat g in stats)
            {
                var row = new HoverRow { Size = new Size(484, 40), Margin = new Padding(2, 1, 0, 1) };

                var nameLbl = new Label
                {
                    Text = g.Name + ".exe",
                    ForeColor = Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    Location = new Point(6, 3),
                    Size = new Size(218, 18),
                    AutoEllipsis = true
                };
                row.Controls.Add(nameLbl);

                var sessLbl = new Label
                {
                    Text = Lang.T("сессий: ", "sessions: ") + g.Sessions
                        + Lang.T(", последняя ", ", last ") + g.LastPlayed.ToString("dd.MM HH:mm")
                        + (g.FpsSec > 0 ? Lang.T(", ср. FPS ", ", avg FPS ") + (int)Math.Round(g.FpsWeightedSum / g.FpsSec) : ""),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 7.5F),
                    Location = new Point(6, 22),
                    AutoSize = true
                };
                row.Controls.Add(sessLbl);

                var timeLbl = new Label
                {
                    Text = GameSessionTracker.FormatDuration(g.TotalSec) + " / " + GameSessionTracker.FormatDuration(g.WeekSec),
                    ForeColor = Theme.Accent,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Location = new Point(232, 10),
                    Size = new Size(126, 18),
                    AutoSize = false,
                    AutoEllipsis = true
                };
                row.Controls.Add(timeLbl);

                // История FPS: есть смысл показывать только там, где FPS замерялся
                if (g.FpsSec > 0)
                {
                    string gameName = g.Name;
                    var histBtn = MakeRowButton("≡", Lang.T("История FPS", "FPS history"), 366, Theme.Accent);
                    histBtn.Click += (s, e) => {
                        using (var f = new FpsHistoryForm(gameName)) f.ShowDialog(this);
                    };
                    row.Controls.Add(histBtn);
                }

                // Убрать не-игру из статистики и больше её игрой не считать
                string rowName = g.Name;
                var ignoreBtn = MakeRowButton("⊘", Lang.T("Больше не считать игрой", "Never count as a game"), 396, Theme.Danger);
                ignoreBtn.Click += (s, e) =>
                {
                    int removed = GameFilter.RemoveFromLog(rowName);
                    GameFilter.Ignore(rowName);
                    TrayNotify.Info(
                        rowName + Lang.T(" больше не игра", " is no longer a game"),
                        Lang.T("Убрано сессий: ", "Sessions removed: ") + removed);
                    RebuildList();
                };
                row.Controls.Add(ignoreBtn);

                var delBtn = MakeRowButton("✕", Lang.T("Убрать из статистики", "Remove from statistics"), 426, Theme.Danger);
                delBtn.Click += (s, e) =>
                {
                    int removed = GameFilter.RemoveFromLog(rowName);
                    TrayNotify.Info(
                        Lang.T("Записи удалены", "Records removed"),
                        rowName + Lang.T(": сессий убрано ", ": sessions removed ") + removed);
                    RebuildList();
                };
                row.Controls.Add(delBtn);

                list.Controls.Add(row);
            }
            list.ResumeLayout();
        }

        // Мелкая иконка-кнопка справа в строке игры
        private Label MakeRowButton(string glyph, string tip, int x, Color hover)
        {
            var btn = new Label
            {
                Text = glyph,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F),
                Size = new Size(26, 28),
                Location = new Point(x, 6),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            DarkTip.Set(btn, tip);
            btn.MouseEnter += (s, e) => btn.ForeColor = hover;
            btn.MouseLeave += (s, e) => btn.ForeColor = Theme.TextDim;
            return btn;
        }

        // Одной кнопкой убирает из журнала все записи, которые не считаются играми
        private void PurgeNonGames()
        {
            int removed = GameFilter.PurgeFromLog();
            if (removed > 0)
            {
                TrayNotify.Info(
                    Lang.T("Статистика почищена", "Statistics cleaned"),
                    Lang.T("Убрано сессий не-игр: ", "Non-game sessions removed: ") + removed);
            }
            RebuildList();
        }

        private List<GameStat> LoadStats()
        {
            var byName = new Dictionary<string, GameStat>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(GameSessionTracker.LogPath))
                {
                    string[] lines = File.ReadAllLines(GameSessionTracker.LogPath);
                    DateTime weekAgo = DateTime.Now.AddDays(-7);

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] parts = lines[i].Split('|');
                        if (parts.Length < 3) continue;

                        DateTime when;
                        int sec;
                        if (!DateTime.TryParseExact(parts[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) continue;
                        if (!int.TryParse(parts[2], out sec)) continue;

                        GameStat stat;
                        if (!byName.TryGetValue(parts[0], out stat))
                        {
                            stat = new GameStat { Name = parts[0] };
                            byName[parts[0]] = stat;
                        }
                        stat.TotalSec += sec;
                        if (when >= weekAgo) stat.WeekSec += sec;
                        stat.Sessions++;
                        if (when > stat.LastPlayed) stat.LastPlayed = when;

                        // FPS появился в v1.6: старые строки короче, у новых может стоять -1
                        int fps;
                        if (parts.Length > 6 && int.TryParse(parts[6], out fps) && fps > 0)
                        {
                            stat.FpsWeightedSum += (double)fps * sec;
                            stat.FpsSec += sec;
                        }
                    }
                }
            }
            catch { }

            var result = new List<GameStat>(byName.Values);
            result.Sort((a, b) => b.TotalSec.CompareTo(a.TotalSec));
            return result;
        }
    }

    // Список «кого не считаем игрой»: всё, что фильтр отсеивает (и стандартный
    // список, и ручные пометки). Любое имя можно вернуть в статистику - например,
    // javaw.exe у кого-то это Minecraft, а не «просто Java»
    public class GameIgnoreForm : Form
    {
        private Point dragStart;
        private bool dragging;
        private FlowLayoutPanel list;

        public GameIgnoreForm()
        {
            Text = "DeepTools Game Filter";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(460, 420);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => ApplyRoundedRegion();
        }

        private void ApplyRoundedRegion()
        {
            var path = new GraphicsPath();
            int r = 12, d = r * 2;
            var rect = new Rectangle(0, 0, Width, Height);
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        private void BuildUi()
        {
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Theme.BgColor };
            titleBar.MouseDown += (s, e) => { dragging = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) =>
            {
                if (dragging) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s, e) => { dragging = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("⚙ Кого не считаем игрой", "⚙ Ignored apps"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 9),
                AutoSize = true
            };
            titleBar.Controls.Add(titleLbl);

            var closeBtn = new Label
            {
                Text = "✕",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 11F),
                Size = new Size(30, 26),
                Location = new Point(Width - 42, 7),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => Close();
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            var hint = new Label
            {
                Text = Lang.T("Полноэкранные мессенджеры, браузеры и терминалы не попадают в статистику игр. Если это всё-таки игра — верни её кнопкой ↩",
                              "Fullscreen messengers, browsers and terminals are kept out of game statistics. If it really is a game, bring it back with ↩"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 44),
                Size = new Size(424, 34)
            };
            Controls.Add(hint);

            var card = Theme.MakeCard(this, new Point(16, 84), new Size(428, 282));
            list = new FlowLayoutPanel
            {
                Location = new Point(10, 10),
                Size = new Size(408, 262),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            card.Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            var resetBtn = new RoundedButton
            {
                Text = Lang.T("Сбросить мои пометки", "Reset my marks"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Location = new Point(16, 376),
                Size = new Size(180, 28)
            };
            resetBtn.Click += (s, e) =>
            {
                GameFilter.ResetUserLists();
                Fill();
            };
            Controls.Add(resetBtn);

            var doneBtn = new RoundedButton
            {
                Text = "OK",
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                CornerRadius = 10,
                Location = new Point(Width - 106, 376),
                Size = new Size(90, 28)
            };
            doneBtn.Click += (s, e) => Close();
            Controls.Add(doneBtn);

            Fill();
        }

        private void Fill()
        {
            list.SuspendLayout();
            list.Controls.Clear();

            // Ручные пометки + всё из журнала, что отсеивает стандартный список
            var names = new List<string>();
            foreach (string n in GameFilter.UserIgnored()) if (!names.Contains(n)) names.Add(n);
            foreach (string n in GameFilter.IgnoredInLog()) if (!names.Contains(n)) names.Add(n);

            if (names.Count == 0)
            {
                list.Controls.Add(new Label
                {
                    Text = Lang.T("Пока никого: не-игры из журнала уже убраны",
                                  "Nobody yet: non-games are already cleaned from the log"),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(6, 6),
                    Size = new Size(390, 40)
                });
                list.ResumeLayout();
                return;
            }

            foreach (string name in names)
            {
                var row = new HoverRow { Size = new Size(384, 32), Margin = new Padding(2, 1, 0, 1) };

                var nameLbl = new Label
                {
                    Text = name + ".exe",
                    ForeColor = Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    Location = new Point(8, 6),
                    Size = new Size(300, 20),
                    AutoEllipsis = true
                };
                row.Controls.Add(nameLbl);

                string captured = name;
                var back = new Label
                {
                    Text = "↩",
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 11F),
                    Size = new Size(30, 26),
                    Location = new Point(344, 3),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Cursor = Cursors.Hand
                };
                DarkTip.Set(back, Lang.T("Это всё-таки игра — вернуть в статистику",
                                         "This is a game after all — bring it back"));
                back.MouseEnter += (s, e) => back.ForeColor = Theme.Accent;
                back.MouseLeave += (s, e) => back.ForeColor = Theme.TextDim;
                back.Click += (s, e) =>
                {
                    GameFilter.Allow(captured);
                    Fill();
                };
                row.Controls.Add(back);

                list.Controls.Add(row);
            }
            list.ResumeLayout();
        }
    }
}
