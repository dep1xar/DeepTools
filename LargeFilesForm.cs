using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    // Поиск больших файлов: сканирует выбранный диск и показывает топ самых
    // тяжёлых файлов. Можно открыть папку файла или удалить его.
    // Учитываем только файлы от 20 МБ, чтобы список был по делу и не жрал память
    public class LargeFilesForm : Form
    {
        private const long MinSize = 20L * 1024 * 1024; // 20 МБ
        private const int TopN = 100;

        private ComboBox driveBox;
        private RoundedButton scanBtn;
        private ListBox list;
        private Label statusLabel;
        private List<KeyValuePair<string, long>> found = new List<KeyValuePair<string, long>>();
        private BackgroundWorker worker;
        private volatile bool cancel = false;

        private Point dragStart;
        private bool draggingForm = false;

        public LargeFilesForm()
        {
            Text = "DeepTools Large Files";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(600, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            ShowInTaskbar = false;

            BuildUi();
            Load += (s, e) => ApplyRoundedRegion();
            FormClosing += (s, e) => { cancel = true; };
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
            titleBar.MouseMove += (s, e) => { if (draggingForm) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y); };
            titleBar.MouseUp += (s, e) => { draggingForm = false; };
            Controls.Add(titleBar);

            var titleLbl = new Label
            {
                Text = Lang.T("📦 Поиск больших файлов", "📦 Large file finder"),
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

            var driveLabel = new Label
            {
                Text = Lang.T("Диск:", "Drive:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(18, 52),
                AutoSize = true
            };
            Controls.Add(driveLabel);

            driveBox = new ComboBox
            {
                Location = new Point(64, 48),
                Size = new Size(90, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5F)
            };
            try
            {
                foreach (DriveInfo di in DriveInfo.GetDrives())
                    if (di.IsReady && di.DriveType == DriveType.Fixed) driveBox.Items.Add(di.Name);
            }
            catch { }
            if (driveBox.Items.Count > 0) driveBox.SelectedIndex = 0;
            Controls.Add(driveBox);

            scanBtn = new RoundedButton
            {
                Text = Lang.T("Сканировать", "Scan"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(170, 46),
                Size = new Size(130, 32)
            };
            scanBtn.Click += (s, e) => StartScan();
            Controls.Add(scanBtn);

            var card = Theme.MakeCard(this, new Point(16, 90), new Size(568, 380));
            list = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(544, 356),
                BackColor = Theme.SidebarColor,
                ForeColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F),
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.One,
                HorizontalScrollbar = true
            };
            list.DoubleClick += (s, e) => OpenSelected();
            card.Controls.Add(list);

            var openBtn = new RoundedButton
            {
                Text = Lang.T("Открыть папку", "Open folder"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 480),
                Size = new Size(160, 34)
            };
            openBtn.Click += (s, e) => OpenSelected();
            Controls.Add(openBtn);

            var delBtn = new RoundedButton
            {
                Text = Lang.T("Удалить файл", "Delete file"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(186, 480),
                Size = new Size(160, 34)
            };
            delBtn.Click += (s, e) => DeleteSelected();
            Controls.Add(delBtn);

            statusLabel = new Label
            {
                Text = Lang.T("Выбери диск и нажми «Сканировать» (от 20 МБ)", "Pick a drive and press Scan (20 MB+)"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(356, 488),
                Size = new Size(230, 30)
            };
            Controls.Add(statusLabel);
        }

        private void StartScan()
        {
            if (worker != null && worker.IsBusy) return;
            if (driveBox.SelectedItem == null) return;

            string root = driveBox.SelectedItem.ToString();
            cancel = false;
            found.Clear();
            list.Items.Clear();
            scanBtn.Text = Lang.T("Сканирую...", "Scanning...");
            statusLabel.Text = Lang.T("Идёт сканирование, подожди...", "Scanning, please wait...");

            worker = new BackgroundWorker();
            worker.DoWork += (s, e) => {
                var acc = new List<KeyValuePair<string, long>>();
                Walk(root, acc);
                acc.Sort((a, b) => b.Value.CompareTo(a.Value));
                if (acc.Count > TopN) acc = acc.GetRange(0, TopN);
                e.Result = acc;
            };
            worker.RunWorkerCompleted += (s, e) => {
                scanBtn.Text = Lang.T("Сканировать", "Scan");
                if (e.Error != null || e.Result == null)
                {
                    statusLabel.Text = Lang.T("Ошибка сканирования", "Scan failed");
                    return;
                }
                found = (List<KeyValuePair<string, long>>)e.Result;
                foreach (var f in found)
                    list.Items.Add(FormatSize(f.Value) + "   " + f.Key);
                statusLabel.Text = Lang.T("Найдено крупных файлов: ", "Large files found: ") + found.Count;
            };
            worker.RunWorkerAsync();
        }

        // Рекурсивный обход с пропуском недоступных папок и точек повторного анализа
        private void Walk(string dir, List<KeyValuePair<string, long>> acc)
        {
            if (cancel) return;

            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch { files = null; }
            if (files != null)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    if (cancel) return;
                    try
                    {
                        var fi = new FileInfo(files[i]);
                        if (fi.Length >= MinSize) acc.Add(new KeyValuePair<string, long>(files[i], fi.Length));
                    }
                    catch { }
                }
            }

            string[] dirs;
            try { dirs = Directory.GetDirectories(dir); }
            catch { return; }
            for (int i = 0; i < dirs.Length; i++)
            {
                if (cancel) return;
                try
                {
                    var attr = File.GetAttributes(dirs[i]);
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue; // не ходим по симлинкам
                }
                catch { continue; }
                Walk(dirs[i], acc);
            }
        }

        private string SelectedPath()
        {
            int idx = list.SelectedIndex;
            if (idx < 0 || idx >= found.Count) return null;
            return found[idx].Key;
        }

        private void OpenSelected()
        {
            string path = SelectedPath();
            if (path == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }

        private void DeleteSelected()
        {
            int idx = list.SelectedIndex;
            string path = SelectedPath();
            if (path == null) return;

            if (DTDialog.Show(
                Lang.T("Удалить файл безвозвратно?\n\n", "Delete this file permanently?\n\n") + path,
                "DeepTools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            try
            {
                File.Delete(path);
                found.RemoveAt(idx);
                list.Items.RemoveAt(idx);
                statusLabel.Text = Lang.T("Файл удалён", "File deleted");
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Не удалось удалить: ", "Failed to delete: ") + ex.Message;
            }
        }

        private static string FormatSize(long bytes)
        {
            double mb = bytes / 1024.0 / 1024.0;
            if (mb >= 1024) return (mb / 1024).ToString("0.##") + " GB";
            return mb.ToString("0") + " MB";
        }
    }
}
