using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    // Тест скорости диска: последовательные чтение/запись + случайное чтение 4K.
    // SMART в Health Check говорит "жив ли диск", а этот тест - "быстр ли он":
    // умирающий HDD часто проседает по скорости задолго до ошибок SMART.
    // Чтение идёт с FILE_FLAG_NO_BUFFERING, иначе Windows отдаст файл из кэша RAM
    public class DiskSpeedForm : Form
    {
        private const int FileSizeMb = 256;
        private const int ChunkMb = 4;
        private const int RandomReads = 400;
        private const FileOptions NoBuffering = (FileOptions)0x20000000;

        private ComboBox driveCombo;
        private RoundedButton startBtn;
        private Label statusLabel;
        private Label writeValue;
        private Label readValue;
        private Label randomValue;
        private Label verdictLabel;
        private Point dragStart;
        private bool draggingForm = false;

        public DiskSpeedForm()
        {
            Text = "DeepTools Disk Speed";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(520, 400);
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
                Text = "💾 " + Lang.T("Скорость диска", "Disk speed"),
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

            driveCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                Font = new Font("Segoe UI", 10F),
                Location = new Point(20, 52),
                Size = new Size(310, 28)
            };
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    long freeGb = d.AvailableFreeSpace / (1024L * 1024 * 1024);
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? Lang.T("Диск", "Drive") : d.VolumeLabel;
                    driveCombo.Items.Add(d.Name + "  " + label + "  (" + Lang.T("свободно ", "free ") + freeGb + " GB)");
                }
                catch { }
            }
            if (driveCombo.Items.Count > 0) driveCombo.SelectedIndex = 0;
            Controls.Add(driveCombo);
            NativeMethods.ApplyDarkCombo(driveCombo);

            startBtn = new RoundedButton
            {
                Text = Lang.T("Запустить тест", "Run test"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(348, 50),
                Size = new Size(150, 32),
                Enabled = driveCombo.Items.Count > 0
            };
            startBtn.Click += (s, e) => RunTest();
            Controls.Add(startBtn);

            var card = Theme.MakeCard(this, new Point(16, 96), new Size(488, 214));

            MakeResultRow(card, 16, Lang.T("Последовательная запись", "Sequential write"), out writeValue);
            MakeResultRow(card, 80, Lang.T("Последовательное чтение", "Sequential read"), out readValue);
            MakeResultRow(card, 144, Lang.T("Случайное чтение 4K", "Random 4K read"), out randomValue);

            statusLabel = new Label
            {
                Text = Lang.T("Тест пишет и читает временный файл 256 МБ — на HDD займёт около минуты",
                              "The test writes and reads a 256 MB temp file — takes about a minute on an HDD"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 320),
                Size = new Size(480, 18)
            };
            Controls.Add(statusLabel);

            verdictLabel = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(20, 348),
                Size = new Size(480, 40)
            };
            Controls.Add(verdictLabel);

            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => Close();
            Controls.Add(esc);
            CancelButton = esc;
        }

        private void MakeResultRow(Panel card, int y, string title, out Label value)
        {
            var titleLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(16, y + 14),
                AutoSize = true
            };
            card.Controls.Add(titleLbl);

            value = new Label
            {
                Text = "—",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(300, y + 8),
                Size = new Size(172, 30),
                TextAlign = ContentAlignment.MiddleRight
            };
            card.Controls.Add(value);
        }

        private void RunTest()
        {
            if (driveCombo.SelectedIndex < 0) return;
            string drive = ((string)driveCombo.SelectedItem).Substring(0, 3); // "C:\"
            string testFile = Path.Combine(drive, "DeepTools_speedtest.tmp");

            startBtn.Enabled = false;
            driveCombo.Enabled = false;
            writeValue.Text = "…"; readValue.Text = "…"; randomValue.Text = "…";
            verdictLabel.Text = "";

            var worker = new System.ComponentModel.BackgroundWorker { WorkerReportsProgress = true };

            worker.DoWork += (s, e) => {
                double writeMbs = 0, readMbs = 0, randomMs = -1;
                var results = new double[3];

                byte[] chunk = new byte[ChunkMb * 1024 * 1024];
                new Random(42).NextBytes(chunk); // не нули: некоторые SSD жмут пустоту и врут

                // 1. Последовательная запись (WriteThrough - мимо кэша Windows)
                worker.ReportProgress(0, Lang.T("Записываю 256 МБ…", "Writing 256 MB…"));
                var sw = Stopwatch.StartNew();
                using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.Write, FileShare.None, chunk.Length, FileOptions.WriteThrough))
                {
                    for (int i = 0; i < FileSizeMb / ChunkMb; i++)
                        fs.Write(chunk, 0, chunk.Length);
                    fs.Flush(true);
                }
                sw.Stop();
                writeMbs = FileSizeMb / sw.Elapsed.TotalSeconds;
                results[0] = writeMbs;

                // 2. Последовательное чтение без кэша. Если система не даст NO_BUFFERING -
                // читаем как есть и честно помечаем результат
                worker.ReportProgress(1, Lang.T("Читаю 256 МБ…", "Reading 256 MB…"));
                bool unbuffered = true;
                sw.Restart();
                try
                {
                    using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, NoBuffering))
                    {
                        int read;
                        while ((read = fs.Read(chunk, 0, chunk.Length)) > 0) { }
                    }
                }
                catch
                {
                    unbuffered = false;
                    sw.Restart();
                    using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read, FileShare.Read, chunk.Length, FileOptions.SequentialScan))
                    {
                        int read;
                        while ((read = fs.Read(chunk, 0, chunk.Length)) > 0) { }
                    }
                }
                sw.Stop();
                readMbs = FileSizeMb / sw.Elapsed.TotalSeconds;
                results[1] = unbuffered ? readMbs : -readMbs; // минус = кэшированное чтение

                // 3. Случайное чтение 4K: главная метрика "отзывчивости" системы
                worker.ReportProgress(2, Lang.T("Случайные чтения…", "Random reads…"));
                try
                {
                    byte[] small = new byte[4096];
                    var rnd = new Random(1337);
                    long blocks = (long)FileSizeMb * 1024 * 1024 / 4096;
                    sw.Restart();
                    using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, NoBuffering))
                    {
                        for (int i = 0; i < RandomReads; i++)
                        {
                            fs.Seek((long)(rnd.NextDouble() * (blocks - 1)) * 4096, SeekOrigin.Begin);
                            fs.Read(small, 0, small.Length);
                        }
                    }
                    sw.Stop();
                    randomMs = sw.Elapsed.TotalMilliseconds / RandomReads;
                }
                catch { randomMs = -1; }
                results[2] = randomMs;

                try { File.Delete(testFile); } catch { }
                e.Result = results;
            };

            worker.ProgressChanged += (s, e) => {
                statusLabel.Text = (string)e.UserState;
            };

            worker.RunWorkerCompleted += (s, e) => {
                startBtn.Enabled = true;
                driveCombo.Enabled = true;

                if (e.Error != null)
                {
                    statusLabel.Text = Lang.T("Ошибка теста: ", "Test failed: ") + e.Error.Message;
                    writeValue.Text = "—"; readValue.Text = "—"; randomValue.Text = "—";
                    return;
                }

                double[] r = (double[])e.Result;
                double write = r[0];
                double read = Math.Abs(r[1]);
                bool cachedRead = r[1] < 0;
                double randMs = r[2];

                writeValue.Text = write.ToString("0") + " MB/s";
                readValue.Text = read.ToString("0") + " MB/s" + (cachedRead ? " *" : "");
                randomValue.Text = randMs >= 0 ? randMs.ToString("0.0") + Lang.T(" мс", " ms") : "—";

                statusLabel.Text = cachedRead
                    ? Lang.T("* чтение прошло через кэш Windows — цифра завышена", "* read went through the Windows cache — the number is inflated")
                    : Lang.T("Готово. Временный файл удалён.", "Done. Temp file removed.");

                // Грубая классификация по последовательному чтению и латентности 4K
                string verdict;
                if (read >= 900) verdict = Lang.T("Похоже на NVMe SSD — летает 🚀", "Looks like an NVMe SSD — it flies 🚀");
                else if (read >= 300) verdict = Lang.T("Похоже на SATA SSD — вполне бодро", "Looks like a SATA SSD — nice and quick");
                else if (read >= 60)
                    verdict = randMs > 10
                        ? Lang.T("Похоже на HDD. Для системы и игр лучше поставить SSD", "Looks like an HDD. An SSD would help a lot for the OS and games")
                        : Lang.T("Скорость на уровне HDD", "HDD-level speed");
                else verdict = Lang.T("⚠ Очень медленно — диск перегружен или умирает, проверь SMART в Health Check", "⚠ Very slow — the disk is overloaded or dying, check SMART in Health Check");

                verdictLabel.Text = verdict;
            };

            worker.RunWorkerAsync();
        }
    }
}
