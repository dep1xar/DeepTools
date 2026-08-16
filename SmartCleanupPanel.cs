using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    // Одна категория для очистки (например Lang.T("Temp пользователя", "User Temp"))
    public class CleanupCategory
    {
        public string DisplayName;
        public string Path;
        public bool RequiresAdmin;
        public long SizeBytes;
        public int FileCount;
        public bool Scanned;
    }

    // Движок очистки: список категорий и работа с папками.
    // Общий для панели SmartCleanup и планировщика автоочистки
    public static class CleanupEngine
    {
        public static List<CleanupCategory> BuildCategories()
        {
            var categories = new List<CleanupCategory>();

            CleanupCategory temp = new CleanupCategory();
            temp.DisplayName = Lang.T("Temp пользователя", "User Temp");
            temp.Path = System.IO.Path.GetTempPath();
            temp.RequiresAdmin = false;
            categories.Add(temp);

            CleanupCategory winTemp = new CleanupCategory();
            winTemp.DisplayName = "Windows Temp";
            winTemp.Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            winTemp.RequiresAdmin = true;
            categories.Add(winTemp);

            CleanupCategory prefetch = new CleanupCategory();
            prefetch.DisplayName = "Prefetch";
            prefetch.Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
            prefetch.RequiresAdmin = true;
            categories.Add(prefetch);

            // Кэши шейдеров: разрастаются до десятков ГБ, а "протухший" кэш вызывает
            // статтеры. После очистки игры пересоберут их сами (первый запуск чуть дольше)
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            CleanupCategory dxCache = new CleanupCategory();
            dxCache.DisplayName = Lang.T("Кэш шейдеров DirectX", "DirectX shader cache");
            dxCache.Path = System.IO.Path.Combine(localAppData, "D3DSCache");
            dxCache.RequiresAdmin = false;
            categories.Add(dxCache);

            CleanupCategory nvCache = new CleanupCategory();
            nvCache.DisplayName = Lang.T("Кэш шейдеров NVIDIA", "NVIDIA shader cache");
            nvCache.Path = System.IO.Path.Combine(localAppData, "NVIDIA\\DXCache");
            nvCache.RequiresAdmin = false;
            categories.Add(nvCache);

            CleanupCategory steamShader = new CleanupCategory();
            steamShader.DisplayName = Lang.T("Кэш шейдеров Steam", "Steam shader cache");
            steamShader.Path = FindSteamShaderCache();
            steamShader.RequiresAdmin = false;
            categories.Add(steamShader);

            return categories;
        }

        // Папка shadercache в установке Steam; ищем по стандартным путям и конфигу
        private static string FindSteamShaderCache()
        {
            string custom = AppConfig.Get("steam_path", "");
            string[] roots = {
                custom,
                "C:\\Program Files (x86)\\Steam",
                "C:\\Program Files\\Steam"
            };
            for (int i = 0; i < roots.Length; i++)
            {
                if (string.IsNullOrEmpty(roots[i])) continue;
                string cache = System.IO.Path.Combine(roots[i], "steamapps\\shadercache");
                if (Directory.Exists(cache)) return cache;
            }
            // не нашли - вернём стандартный путь, категория честно покажет 0 байт
            return "C:\\Program Files (x86)\\Steam\\steamapps\\shadercache";
        }

        // Считает суммарный размер файлов внутри папки (рекурсивно), пропуская недоступные файлы
        public static void GetDirectorySize(string path, out long size, out int count)
        {
            size = 0;
            count = 0;
            if (!Directory.Exists(path)) return;

            string[] files;
            try { files = Directory.GetFiles(path, "*", SearchOption.AllDirectories); }
            catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    FileInfo info = new FileInfo(files[i]);
                    size += info.Length;
                    count++;
                }
                catch
                {
                    // файл мог исчезнуть или быть недоступен - пропускаем
                }
            }
        }

        // Удаляет содержимое папки, саму папку оставляет. Возвращает сколько байт реально освободили
        public static long CleanFolderContents(string path)
        {
            long freed = 0;
            if (!Directory.Exists(path)) return freed;

            // Удаляем файлы во всех подпапках
            string[] files;
            try { files = Directory.GetFiles(path, "*", SearchOption.AllDirectories); }
            catch { return freed; }

            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    FileInfo info = new FileInfo(files[i]);
                    long len = info.Length;
                    File.Delete(files[i]);
                    freed += len;
                }
                catch
                {
                    // файл занят или недоступен - пропускаем
                }
            }

            // Пытаемся удалить пустые подпапки
            try
            {
                string[] dirs = Directory.GetDirectories(path);
                for (int i = 0; i < dirs.Length; i++)
                {
                    try
                    {
                        Directory.Delete(dirs[i], true);
                    }
                    catch { }
                }
            }
            catch { }

            return freed;
        }

        public static string FormatSize(long bytes)
        {
            double kb = bytes / 1024.0;
            double mb = kb / 1024.0;
            double gb = mb / 1024.0;

            if (gb >= 1) return gb.ToString("0.##") + Lang.T(" ГБ", " GB");
            if (mb >= 1) return mb.ToString("0.#") + Lang.T(" МБ", " MB");
            if (kb >= 1) return kb.ToString("0.#") + Lang.T(" КБ", " KB");
            return bytes + Lang.T(" Б", " B");
        }
    }


    // Планировщик автоочистки: раз в N дней тихо чистит выбранные категории
    // и сообщает результат из трея. Настройки в конфиге:
    //   autoclean_enabled - вкл/выкл
    //   autoclean_days    - интервал в днях (1..30)
    //   autoclean_cats    - какие категории чистить (битовая маска по индексам)
    //   autoclean_last    - когда чистили в прошлый раз
    public static class AutoCleanup
    {
        private static System.Windows.Forms.Timer timer;
        private static bool cleaning;

        public static bool Enabled
        {
            get { return AppConfig.GetBool("autoclean_enabled", false); }
            set { AppConfig.SetBool("autoclean_enabled", value); }
        }

        public static int IntervalDays
        {
            get
            {
                int d;
                if (!int.TryParse(AppConfig.Get("autoclean_days", "7"), out d)) d = 7;
                if (d < 1) d = 1;
                if (d > 30) d = 30;
                return d;
            }
            set { AppConfig.Set("autoclean_days", value.ToString()); }
        }

        // Битовая маска выбранных категорий; по умолчанию все
        public static int CategoryMask
        {
            get
            {
                int m;
                if (!int.TryParse(AppConfig.Get("autoclean_cats", "-1"), out m)) m = -1;
                return m;
            }
            set { AppConfig.Set("autoclean_cats", value.ToString()); }
        }

        public static DateTime LastRun
        {
            get
            {
                DateTime t;
                if (DateTime.TryParseExact(AppConfig.Get("autoclean_last", ""), "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out t))
                    return t;
                return DateTime.MinValue;
            }
            set { AppConfig.Set("autoclean_last", value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)); }
        }

        // Зовётся один раз при старте программы
        public static void Start()
        {
            if (timer != null) return;
            timer = new System.Windows.Forms.Timer { Interval = 10 * 60 * 1000 }; // проверка каждые 10 минут
            timer.Tick += (s, e) => CheckDue();
            timer.Start();

            // Первая проверка через минуту после старта, не сразу -
            // пусть программа спокойно поднимется
            var firstCheck = new System.Windows.Forms.Timer { Interval = 60 * 1000 };
            firstCheck.Tick += (s, e) => { firstCheck.Stop(); firstCheck.Dispose(); CheckDue(); };
            firstCheck.Start();
        }

        private static void CheckDue()
        {
            if (!Enabled || cleaning) return;

            DateTime last = LastRun;
            // Первое включение: точку отсчёта ставим сейчас, чтобы очистка
            // не сработала внезапно в момент включения тумблера
            if (last == DateTime.MinValue) { LastRun = DateTime.Now; return; }
            if ((DateTime.Now - last).TotalDays < IntervalDays) return;

            // Не чистим кэши шейдеров под запущенной игрой
            if (GameSessionTracker.IsGameRunning) return;

            RunSilent();
        }

        private static void RunSilent()
        {
            cleaning = true;
            int mask = CategoryMask;
            bool isAdmin = Program.IsRunningAsAdmin();

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => {
                long freed = 0;
                List<CleanupCategory> cats = CleanupEngine.BuildCategories();
                for (int i = 0; i < cats.Count; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    if (cats[i].RequiresAdmin && !isAdmin) continue;
                    freed += CleanupEngine.CleanFolderContents(cats[i].Path);
                }
                e.Result = freed;
            };
            worker.RunWorkerCompleted += (s, e) => {
                cleaning = false;
                LastRun = DateTime.Now;
                long freed = e.Result is long ? (long)e.Result : 0;
                TrayNotify.Info(
                    Lang.T("Автоочистка выполнена", "Auto cleanup finished"),
                    Lang.T("Освобождено ", "Freed ") + CleanupEngine.FormatSize(freed));
            };
            worker.RunWorkerAsync();
        }
    }

    public class SmartCleanupPanel : Panel
    {
        private List<CleanupCategory> categories;
        private List<CheckBox> categoryChecks = new List<CheckBox>();
        private List<Label> categorySizeLabels = new List<Label>();
        private Label totalLabel;
        private Label statusLabel;

        public SmartCleanupPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;
            AutoScroll = true;
            NativeMethods.ApplyDarkScrollbar(this);

            categories = CleanupEngine.BuildCategories();
            BuildUi();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = "SmartCleanup",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                Location = new Point(24, 24),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            int y = 70;
            for (int i = 0; i < categories.Count; i++)
            {
                CleanupCategory cat = categories[i];
                var card = Theme.MakeCard(this, new Point(24, y), new Size(650, 54));

                bool blocked = cat.RequiresAdmin && !Program.IsRunningAsAdmin();

                var check = new CheckBox
                {
                    Text = cat.DisplayName,
                    ForeColor = blocked ? Theme.TextDim : Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    Location = new Point(20, 6),
                    AutoSize = true,
                    Checked = !blocked,
                    Enabled = !blocked
                };
                card.Controls.Add(check);
                categoryChecks.Add(check);

                var pathLbl = new Label
                {
                    Text = cat.Path,
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8F),
                    Location = new Point(40, 30),
                    AutoSize = true
                };
                card.Controls.Add(pathLbl);

                var sizeLbl = new Label
                {
                    Text = blocked ? Lang.T("Нужны права администратора", "Administrator rights required") : Lang.T("Не просканировано", "Not scanned"),
                    ForeColor = blocked ? Theme.Warning : Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(460, 16),
                    AutoSize = true
                };
                card.Controls.Add(sizeLbl);
                categorySizeLabels.Add(sizeLbl);

                y += 62;
            }

            // Ряд кнопок: суммарно должен влезать в 760px панели с отступом 24
            var scanBtn = new RoundedButton
            {
                Text = Lang.T("Сканировать", "Scan"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(24, y + 10),
                Size = new Size(130, 36)
            };
            scanBtn.Click += (s, e) => ScanAll();
            Controls.Add(scanBtn);

            var cleanBtn = new RoundedButton
            {
                Text = Lang.T("Очистить выбранное", "Clean selected"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(164, y + 10),
                Size = new Size(150, 36)
            };
            cleanBtn.Click += (s, e) => CleanSelected();
            Controls.Add(cleanBtn);

            var debloatBtn = new RoundedButton
            {
                Text = Lang.T("Встроенный мусор Windows", "Windows bloatware"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(324, y + 10),
                Size = new Size(200, 36)
            };
            debloatBtn.Click += (s, e) => {
                using (var f = new DebloatForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(debloatBtn);

            var uninstallBtn = new RoundedButton
            {
                Text = Lang.T("Деинсталлятор программ", "Program uninstaller"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(534, y + 10),
                Size = new Size(200, 36)
            };
            uninstallBtn.Click += (s, e) => {
                using (var f = new UninstallerForm())
                {
                    f.ShowDialog(FindForm());
                }
            };
            Controls.Add(uninstallBtn);

            totalLabel = new Label
            {
                Text = Lang.T("Всего к очистке: 0 Б", "Total to clean: 0 B"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(24, y + 54),
                AutoSize = true
            };
            Controls.Add(totalLabel);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, y + 78),
                AutoSize = true
            };
            Controls.Add(statusLabel);

            BuildSchedulerUi(y + 102);
            BuildAutoMaintenanceUi(y + 172);
        }

        // Две карточки авто-обслуживания: сброс кэша шейдеров при смене драйвера GPU
        // и авто-очистка standby-памяти. У каждой тумблер + кнопка «Очистить сейчас».
        private void BuildAutoMaintenanceUi(int y)
        {
            BuildMaintCard(y,
                Lang.T("Кэш шейдеров при смене драйвера GPU", "Shader cache on GPU driver change"),
                Lang.T("После обновления драйвера старый кэш вызывает фризы — чистим автоматически на старте.",
                       "After a driver update a stale cache causes stutter — cleared automatically on launch."),
                "shader_auto", false,
                Lang.T("Очистить сейчас", "Clean now"),
                () => { long mb = ShaderCacheGuard.CleanNow(); return Lang.T("Кэш шейдеров очищен: ", "Shader cache cleared: ") + mb + Lang.T(" МБ", " MB"); });

            BuildMaintCard(y + 70,
                Lang.T("Авто-очистка standby-памяти", "Auto standby memory cleanup"),
                Lang.T("Сбрасывает резервный кэш памяти при нехватке RAM — убирает микрофризы в тяжёлых играх.",
                       "Purges the standby cache when RAM runs low — removes micro-stutter in heavy games."),
                "standby_auto", true,
                Lang.T("Очистить сейчас", "Clean now"),
                () => { long mb = StandbyCleaner.Purge(); return Lang.T("Освобождено из standby: ", "Freed from standby: ") + mb + Lang.T(" МБ", " MB"); });
        }

        // startService: true = фича управляет фоновым сервисом StandbyCleaner,
        // false = разовое действие на старте (ShaderCacheGuard)
        private void BuildMaintCard(int y, string title, string desc, string cfgKey,
            bool startService, string btnText, Func<string> action)
        {
            var card = Theme.MakeCard(this, new Point(24, y), new Size(650, 58));

            var toggle = new ToggleSwitch
            {
                Location = new Point(16, 17),
                Checked = AppConfig.GetBool(cfgKey, false)
            };
            toggle.CheckedChanged += (s, e) => {
                AppConfig.SetBool(cfgKey, toggle.Checked);
                if (startService) StandbyCleaner.Enabled = toggle.Checked;
            };
            card.Controls.Add(toggle);

            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(74, 8),
                AutoSize = true
            };
            card.Controls.Add(titleLbl);

            var infoLbl = new Label
            {
                Text = desc,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(75, 32),
                Size = new Size(430, 18),
                AutoEllipsis = true
            };
            card.Controls.Add(infoLbl);

            var nowBtn = new RoundedButton
            {
                Text = btnText,
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(512, 15),
                Size = new Size(124, 28)
            };
            nowBtn.Click += (s, e) => {
                nowBtn.Enabled = false;
                string prevText = nowBtn.Text;
                nowBtn.Text = "...";
                var worker = new System.ComponentModel.BackgroundWorker();
                worker.DoWork += delegate(object s2, System.ComponentModel.DoWorkEventArgs e2) { e2.Result = action(); };
                worker.RunWorkerCompleted += delegate(object s2, System.ComponentModel.RunWorkerCompletedEventArgs e2) {
                    nowBtn.Enabled = true;
                    nowBtn.Text = prevText;
                    if (IsDisposed) return;
                    if (e2.Error == null && e2.Result != null)
                    {
                        statusLabel.Text = (string)e2.Result;
                        statusLabel.ForeColor = Theme.Accent;
                    }
                };
                worker.RunWorkerAsync();
            };
            card.Controls.Add(nowBtn);
        }

        // Карточка планировщика автоочистки: тумблер, интервал в днях, дата последней очистки.
        // Чистятся категории, отмеченные галочками выше (маска сохраняется в конфиг)
        private void BuildSchedulerUi(int y)
        {
            var card = Theme.MakeCard(this, new Point(24, y), new Size(650, 58));

            var toggle = new ToggleSwitch
            {
                Location = new Point(16, 17),
                Checked = AutoCleanup.Enabled
            };
            card.Controls.Add(toggle);

            var titleLbl = new Label
            {
                Text = Lang.T("Автоочистка по расписанию", "Scheduled auto cleanup"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(74, 8),
                AutoSize = true
            };
            card.Controls.Add(titleLbl);

            var infoLbl = new Label
            {
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(75, 32),
                AutoSize = true
            };
            card.Controls.Add(infoLbl);

            Action updateInfo = () => {
                DateTime last = AutoCleanup.LastRun;
                string lastText = last == DateTime.MinValue
                    ? Lang.T("ещё не выполнялась", "not run yet")
                    : Lang.T("последняя: ", "last run: ") + last.ToString("dd.MM HH:mm");
                infoLbl.Text = Lang.T("Тихо чистит отмеченные категории, отчёт из трея. ", "Silently cleans checked categories, tray report. ") + lastText;
            };
            updateInfo();

            // Степпер интервала: «раз в N дн.»
            int days = AutoCleanup.IntervalDays;

            var daysLbl = new Label
            {
                Text = Lang.T("раз в ", "every "),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(452, 20),
                AutoSize = true
            };
            card.Controls.Add(daysLbl);

            var valLbl = new Label
            {
                Text = days + Lang.T(" дн.", " d"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(520, 18),
                Size = new Size(44, 18),
                TextAlign = ContentAlignment.MiddleCenter
            };

            var minusBtn = new RoundedButton
            {
                Text = "−",
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(496, 15),
                Size = new Size(24, 24)
            };
            minusBtn.Click += (s, e) => {
                if (days > 1) { days--; valLbl.Text = days + Lang.T(" дн.", " d"); AutoCleanup.IntervalDays = days; }
            };
            card.Controls.Add(minusBtn);
            card.Controls.Add(valLbl);

            var plusBtn = new RoundedButton
            {
                Text = "+",
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(564, 15),
                Size = new Size(24, 24)
            };
            plusBtn.Click += (s, e) => {
                if (days < 30) { days++; valLbl.Text = days + Lang.T(" дн.", " d"); AutoCleanup.IntervalDays = days; }
            };
            card.Controls.Add(plusBtn);

            toggle.CheckedChanged += (s, e) => {
                AutoCleanup.Enabled = toggle.Checked;
                if (toggle.Checked)
                {
                    SaveCategoryMask();
                    // Отсчёт с момента включения, а не с эпохи - иначе чистка
                    // рванёт сразу же
                    if (AutoCleanup.LastRun == DateTime.MinValue) AutoCleanup.LastRun = DateTime.Now;
                }
                updateInfo();
            };

            // Смена галочек при включённом планировщике сразу обновляет маску
            for (int i = 0; i < categoryChecks.Count; i++)
            {
                categoryChecks[i].CheckedChanged += (s, e) => {
                    if (AutoCleanup.Enabled) SaveCategoryMask();
                };
            }
        }

        // Маска категорий для автоочистки - из текущих галочек панели
        private void SaveCategoryMask()
        {
            int mask = 0;
            for (int i = 0; i < categoryChecks.Count; i++)
            {
                if (categoryChecks[i].Checked && categoryChecks[i].Enabled) mask |= 1 << i;
            }
            AutoCleanup.CategoryMask = mask;
        }

        private void ScanAll()
        {
            long total = 0;
            for (int i = 0; i < categories.Count; i++)
            {
                CleanupCategory cat = categories[i];
                if (cat.RequiresAdmin && !Program.IsRunningAsAdmin()) continue;

                long size;
                int count;
                GetDirectorySize(cat.Path, out size, out count);
                cat.SizeBytes = size;
                cat.FileCount = count;
                cat.Scanned = true;

                categorySizeLabels[i].Text = FormatSize(size) + ", " + count + Lang.T(" файлов", " files");
                categorySizeLabels[i].ForeColor = Theme.TextMain;

                if (categoryChecks[i].Checked) total += size;
            }
            totalLabel.Text = Lang.T("Всего к очистке: ", "Total to clean: ") + FormatSize(total);
            statusLabel.Text = Lang.T("Сканирование завершено", "Scan complete");
        }

        private void CleanSelected()
        {
            long total = 0;
            List<CleanupCategory> toClean = new List<CleanupCategory>();
            for (int i = 0; i < categories.Count; i++)
            {
                if (categoryChecks[i].Checked && categoryChecks[i].Enabled)
                {
                    if (!categories[i].Scanned)
                    {
                        DTDialog.Show(Lang.T("Сначала нажми \"Сканировать\", чтобы увидеть, что будет удалено.", "Press Scan first to see what will be deleted."), "DeepTools",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    total += categories[i].SizeBytes;
                    toClean.Add(categories[i]);
                }
            }

            if (toClean.Count == 0)
            {
                statusLabel.Text = Lang.T("Нечего очищать - ничего не выбрано", "Nothing to clean - nothing selected");
                return;
            }

            DialogResult result = DTDialog.Show(
                Lang.T("Будет очищено примерно ", "Approximately ") + FormatSize(total) + Lang.T(".\n\nПродолжить?", " will be cleaned.\n\nContinue?"),
                Lang.T("Подтверждение очистки", "Cleanup confirmation"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            long freed = 0;
            for (int i = 0; i < toClean.Count; i++)
            {
                freed += CleanFolderContents(toClean[i].Path);
            }

            statusLabel.Text = Lang.T("Очищено ", "Cleaned ") + FormatSize(freed) + Lang.T(" (часть файлов могла быть занята и пропущена)", " (some files may have been in use and skipped)");
            ScanAll();
        }

        private void GetDirectorySize(string path, out long size, out int count)
        {
            CleanupEngine.GetDirectorySize(path, out size, out count);
        }

        private long CleanFolderContents(string path)
        {
            return CleanupEngine.CleanFolderContents(path);
        }

        private string FormatSize(long bytes)
        {
            return CleanupEngine.FormatSize(bytes);
        }
    }
}