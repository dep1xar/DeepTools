using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Окно «Полное удаление Яндекс.Браузера»: показывает всё найденное
    // (процессы, папки, ключи реестра, задачи, службы, ярлыки) и сносит одним
    // нажатием. Штатный деинсталлятор оставляет профиль в AppData — здесь
    // добираем и его.
    public class YandexCleanerForm : Form
    {
        private List<YandexItem> items = new List<YandexItem>();
        private ListBox list;
        private Label statusLabel;
        private Label countLabel;
        private RoundedButton scanBtn;
        private RoundedButton removeBtn;
        private CheckBox includeOther;
        private bool busy;
        // После удаления пересканируем и честно показываем, что осталось
        private bool pendingRemovalReport;

        private Point dragStart;
        private bool draggingForm;

        public YandexCleanerForm()
        {
            Text = "DeepTools Yandex Cleaner";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(580, 600);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => { ApplyRoundedRegion(); StartScan(); };
        }

        private void ApplyRoundedRegion()
        {
            try
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
            catch { }
        }

        private void BuildUi()
        {
            var titleBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Theme.BgColor };
            titleBar.MouseDown += (s, e) => { draggingForm = true; dragStart = new Point(e.X, e.Y); };
            titleBar.MouseMove += (s, e) =>
            {
                if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("🧹 Полное удаление Яндекс.Браузера", "🧹 Remove Yandex Browser completely"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                Location = new Point(18, 10),
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
                Location = new Point(Width - 42, 8),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => { if (!busy) Close(); };
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            var hint = new Label
            {
                Text = Lang.T(
                    "Обычный деинсталлятор оставляет профиль и кэш в AppData. Здесь удаляется всё: процессы (browser.exe), папки, реестр, задачи и ярлыки.",
                    "The stock uninstaller leaves the profile and cache in AppData. This removes everything: processes (browser.exe), folders, registry, tasks and shortcuts."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 50),
                Size = new Size(544, 44)
            };
            Controls.Add(hint);

            includeOther = new CheckBox
            {
                Text = Lang.T("Заодно остальные продукты Яндекса (Диск, Мессенджер и т.п.)",
                              "Also other Yandex products (Disk, Messenger, etc.)"),
                ForeColor = Theme.Warning,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(18, 96),
                Size = new Size(544, 20),
                Checked = false
            };
            includeOther.CheckedChanged += (s, e) => { if (!busy) StartScan(); };
            Controls.Add(includeOther);

            var card = Theme.MakeCard(this, new Point(16, 122), new Size(548, 360));

            list = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(524, 336),
                BackColor = Theme.SidebarColor,
                ForeColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F),
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                SelectionMode = SelectionMode.One
            };
            card.Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            scanBtn = new RoundedButton
            {
                Text = Lang.T("Проверить снова", "Scan again"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 494),
                Size = new Size(170, 38)
            };
            scanBtn.Click += (s, e) => StartScan();
            Controls.Add(scanBtn);

            removeBtn = new RoundedButton
            {
                Text = Lang.T("Удалить всё", "Remove all"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(196, 494),
                Size = new Size(180, 38),
                Enabled = false
            };
            removeBtn.Click += (s, e) => StartRemove();
            Controls.Add(removeBtn);

            countLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 540),
                Size = new Size(548, 20)
            };
            Controls.Add(countLabel);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 562),
                Size = new Size(548, 20),
                AutoEllipsis = true
            };
            Controls.Add(statusLabel);
        }

        // ---- Сканирование ----

        private void StartScan()
        {
            if (busy) return;
            busy = true;
            scanBtn.Enabled = false;
            removeBtn.Enabled = false;
            list.Items.Clear();
            items.Clear();
            countLabel.Text = "";
            statusLabel.ForeColor = Theme.TextDim;
            statusLabel.Text = Lang.T("Ищем следы Яндекса…", "Looking for Yandex traces…");

            bool other = includeOther.Checked;

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) =>
            {
                e.Result = YandexCleaner.Scan(other, msg =>
                {
                    try { BeginInvoke(new Action(() => statusLabel.Text = msg)); } catch { }
                });
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                busy = false;
                scanBtn.Enabled = true;
                if (IsDisposed) return;

                if (e.Error != null)
                {
                    statusLabel.ForeColor = Theme.Danger;
                    statusLabel.Text = Lang.T("Ошибка поиска: ", "Scan failed: ") + e.Error.Message;
                    return;
                }

                items = (List<YandexItem>)e.Result ?? new List<YandexItem>();
                FillList();
            };
            worker.RunWorkerAsync();
        }

        private void FillList()
        {
            list.BeginUpdate();
            list.Items.Clear();
            long totalSize = 0;
            int folders = 0;

            for (int i = 0; i < items.Count; i++)
            {
                YandexItem it = items[i];
                if (it.Kind == "folder") { totalSize += it.Size; folders++; }
                list.Items.Add(Prefix(it.Kind) + it.Title);
            }
            list.EndUpdate();

            if (items.Count == 0)
            {
                countLabel.Text = Lang.T("Следов Яндекса не найдено", "No Yandex traces found");
                countLabel.ForeColor = Theme.On;
                if (pendingRemovalReport)
                {
                    pendingRemovalReport = false;
                    statusLabel.ForeColor = Theme.On;
                    statusLabel.Text = Lang.T("Яндекс удалён полностью ✓", "Yandex removed completely ✓");
                }
                else
                {
                    statusLabel.Text = Lang.T("Похоже, браузер уже удалён", "The browser seems to be already removed");
                }
                removeBtn.Enabled = false;
                return;
            }

            countLabel.ForeColor = Theme.TextMain;
            countLabel.Text = Lang.T("Найдено: ", "Found: ") + items.Count
                + Lang.T(" объектов · папок: ", " items · folders: ") + folders
                + Lang.T(" · на диске: ", " · on disk: ") + YandexCleaner.FormatSize(totalSize);

            if (pendingRemovalReport)
            {
                pendingRemovalReport = false;
                statusLabel.ForeColor = Theme.Warning;
                statusLabel.Text = Lang.T(
                    "Не всё удалилось: осталось " + items.Count + ". Закрой браузер или запусти DeepTools от администратора.",
                    "Not everything was removed: " + items.Count + " left. Close the browser or run DeepTools as administrator.");
            }
            else
            {
                statusLabel.ForeColor = Theme.Warning;
                statusLabel.Text = Lang.T("Проверь список перед удалением. Закрой браузер, если он открыт.",
                                          "Check the list before removing. Close the browser if it is running.");
            }
            removeBtn.Enabled = true;
        }

        private static string Prefix(string kind)
        {
            switch (kind)
            {
                case "process": return Lang.T("[процесс] ", "[process] ");
                case "folder": return Lang.T("[папка] ", "[folder] ");
                case "registry": return Lang.T("[реестр] ", "[registry] ");
                case "runvalue": return Lang.T("[автозагрузка] ", "[startup] ");
                case "task": return Lang.T("[задача] ", "[task] ");
                case "service": return Lang.T("[служба] ", "[service] ");
                case "shortcut": return Lang.T("[ярлык] ", "[shortcut] ");
                default: return "";
            }
        }

        // ---- Удаление ----

        private void StartRemove()
        {
            if (busy || items.Count == 0) return;

            DialogResult r = DTDialog.Show(
                Lang.T("Удалить всё найденное (" + items.Count + " объектов)?\n\n" +
                       "Процессы будут закрыты, папки и записи реестра удалены без возможности отката.",
                       "Remove all found items (" + items.Count + ")?\n\n" +
                       "Processes will be closed, folders and registry entries deleted without undo."),
                Lang.T("Полное удаление Яндекса", "Full Yandex removal"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            busy = true;
            scanBtn.Enabled = false;
            removeBtn.Enabled = false;
            statusLabel.ForeColor = Theme.TextDim;
            statusLabel.Text = Lang.T("Удаляем…", "Removing…");

            var snapshot = new List<YandexItem>(items);

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) =>
            {
                e.Result = YandexCleaner.Remove(snapshot, msg =>
                {
                    try { BeginInvoke(new Action(() => statusLabel.Text = msg)); } catch { }
                });
            };
            worker.RunWorkerCompleted += (s, e) =>
            {
                busy = false;
                scanBtn.Enabled = true;
                if (IsDisposed) return;

                long freed = (e.Error == null && e.Result is long) ? (long)e.Result : 0;

                if (e.Error != null)
                {
                    statusLabel.ForeColor = Theme.Danger;
                    statusLabel.Text = Lang.T("Ошибка удаления: ", "Removal failed: ") + e.Error.Message;
                }
                else
                {
                    statusLabel.ForeColor = Theme.On;
                    statusLabel.Text = Lang.T("Удалено. Освобождено: ", "Removed. Freed: ") + YandexCleaner.FormatSize(freed);
                }

                // Перепроверяем, что осталось
                pendingRemovalReport = true;
                StartScan();
            };
            worker.RunWorkerAsync();
        }
    }
}
