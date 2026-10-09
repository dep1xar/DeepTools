using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DeepTools
{
    // Мини-бенчмарк "до/после": CPU (1 поток и все), RAM, диск.
    // Результат сохраняется в конфиг, при следующем запуске показывается разница -
    // так видно, дали ли твики реальный прирост
    public class BenchmarkForm : Form
    {
        private Label[] currentLabels = new Label[4];
        private Label[] prevLabels = new Label[4];
        private RoundedButton runBtn;
        private Label statusLabel;
        private bool running = false;

        private static readonly string[] ConfigKeys = { "bench_cpu1", "bench_cpu_all", "bench_ram", "bench_disk" };
        // Для всех метрик больше = лучше
        private static readonly string[] Units = { " Mops", " Mops", " GB/s", " MB/s" };

        private Point dragStart;
        private bool draggingForm = false;

        public BenchmarkForm()
        {
            Text = "DeepTools Benchmark";
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(460, 400);
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
                Text = Lang.T("⚡ Бенчмарк", "⚡ Benchmark"),
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
            closeBtn.Click += (s, e) => { if (!running) Close(); };
            closeBtn.MouseEnter += (s, e) => closeBtn.ForeColor = Theme.Danger;
            closeBtn.MouseLeave += (s, e) => closeBtn.ForeColor = Theme.TextDim;
            titleBar.Controls.Add(closeBtn);

            var card = Theme.MakeCard(this, new Point(16, 52), new Size(428, 250));

            var headName = MakeLabel(card, Lang.T("Тест", "Test"), new Point(16, 12), Theme.TextDim, 8.5F, false);
            var headCur = MakeLabel(card, Lang.T("Сейчас", "Now"), new Point(200, 12), Theme.TextDim, 8.5F, false);
            var headPrev = MakeLabel(card, Lang.T("Прошлый раз", "Last time"), new Point(310, 12), Theme.TextDim, 8.5F, false);

            string[] names = {
                Lang.T("CPU (1 поток)", "CPU (1 thread)"),
                Lang.T("CPU (все потоки)", "CPU (all threads)"),
                Lang.T("Память (копирование)", "RAM (copy)"),
                Lang.T("Диск (запись)", "Disk (write)")
            };

            for (int i = 0; i < 4; i++)
            {
                int y = 44 + i * 50;
                MakeLabel(card, names[i], new Point(16, y + 4), Theme.TextMain, 9.5F, false);
                currentLabels[i] = MakeLabel(card, "—", new Point(200, y), Theme.Accent, 11F, true);
                prevLabels[i] = MakeLabel(card, LoadPrevText(i), new Point(310, y + 4), Theme.TextDim, 9F, false);
            }

            runBtn = new RoundedButton
            {
                Text = Lang.T("Запустить тест (~10 сек)", "Run test (~10 s)"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(16, 316),
                Size = new Size(210, 38)
            };
            runBtn.Click += (s, e) => RunBenchmark();
            Controls.Add(runBtn);

            statusLabel = new Label
            {
                Text = Lang.T("Закрой тяжёлые программы для честного результата", "Close heavy apps for a fair result"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 364),
                Size = new Size(428, 18)
            };
            Controls.Add(statusLabel);
        }

        private Label MakeLabel(Control parent, string text, Point loc, Color color, float size, bool bold)
        {
            var l = new Label
            {
                Text = text,
                ForeColor = color,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Location = loc,
                AutoSize = true
            };
            parent.Controls.Add(l);
            return l;
        }

        private string LoadPrevText(int i)
        {
            string saved = AppConfig.Get(ConfigKeys[i], "");
            if (saved == "") return "—";
            double v;
            if (!double.TryParse(saved, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return "—";
            return v.ToString("0.#") + Units[i];
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => statusLabel.Text = text)); }
            else statusLabel.Text = text;
        }

        private void SetResult(int i, double value)
        {
            BeginInvoke((Action)(() => {
                currentLabels[i].Text = value.ToString("0.#") + Units[i];

                string saved = AppConfig.Get(ConfigKeys[i], "");
                double prev;
                if (saved != "" && double.TryParse(saved, NumberStyles.Any, CultureInfo.InvariantCulture, out prev) && prev > 0)
                {
                    double deltaPct = (value - prev) / prev * 100;
                    string sign = deltaPct >= 0 ? "+" : "";
                    prevLabels[i].Text = prev.ToString("0.#") + Units[i] + "  (" + sign + deltaPct.ToString("0") + "%)";
                    prevLabels[i].ForeColor = deltaPct >= 0 ? Theme.On : Theme.Off;
                }
            }));
        }

        private void RunBenchmark()
        {
            if (running) return;
            running = true;
            runBtn.Text = Lang.T("Тестируем...", "Testing...");
            for (int i = 0; i < 4; i++) currentLabels[i].Text = "—";

            var t = new Thread(() => {
                double[] results = new double[4];

                SetStatus(Lang.T("Тест CPU (1 поток)...", "Testing CPU (1 thread)..."));
                results[0] = CpuTest(1);
                SetResult(0, results[0]);

                SetStatus(Lang.T("Тест CPU (все потоки)...", "Testing CPU (all threads)..."));
                results[1] = CpuTest(Environment.ProcessorCount);
                SetResult(1, results[1]);

                SetStatus(Lang.T("Тест памяти...", "Testing RAM..."));
                results[2] = RamTest();
                SetResult(2, results[2]);

                SetStatus(Lang.T("Тест диска...", "Testing disk..."));
                results[3] = DiskTest();
                SetResult(3, results[3]);

                // Сохраняем как "прошлый раз" для следующего сравнения
                for (int i = 0; i < 4; i++)
                {
                    AppConfig.Set(ConfigKeys[i], results[i].ToString("0.##", CultureInfo.InvariantCulture));
                }

                BeginInvoke((Action)(() => {
                    running = false;
                    runBtn.Text = Lang.T("Запустить ещё раз", "Run again");
                    statusLabel.Text = Lang.T("Готово. Результат сохранён - примени твики и сравни!", "Done. Result saved - apply tweaks and compare!");
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        // Целочисленная + плавающая арифметика ~1.2 сек на N потоках. Результат - миллионы операций/сек
        private double CpuTest(int threads)
        {
            long totalOps = 0;
            object opsLock = new object();
            var workers = new Thread[threads];
            var sw = Stopwatch.StartNew();

            for (int t = 0; t < threads; t++)
            {
                workers[t] = new Thread(() => {
                    ulong x = 88172645463325252UL;
                    double acc = 1.0001;
                    long ops = 0;
                    while (sw.ElapsedMilliseconds < 1200)
                    {
                        for (int i = 0; i < 100000; i++)
                        {
                            x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                            acc = acc * 1.0000001 + (x & 0xFF);
                        }
                        ops += 100000;
                    }
                    if (acc == 0) Console.WriteLine(acc); // чтобы оптимизатор не выкинул цикл
                    lock (opsLock) { totalOps += ops; }
                });
                workers[t].IsBackground = true;
                workers[t].Start();
            }
            for (int t = 0; t < threads; t++) workers[t].Join();

            return totalOps / sw.Elapsed.TotalSeconds / 1000000.0;
        }

        // Копирование 64 МБ блока ~1.2 сек. Результат - ГБ/с
        private double RamTest()
        {
            const int size = 64 * 1024 * 1024;
            byte[] src = new byte[size];
            byte[] dst = new byte[size];
            new Random(42).NextBytes(src);

            long copied = 0;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1200)
            {
                Buffer.BlockCopy(src, 0, dst, 0, size);
                copied += size;
            }
            return copied / sw.Elapsed.TotalSeconds / 1024 / 1024 / 1024;
        }

        // Запись 128 МБ во временный файл блоками по 4 МБ. Результат - МБ/с
        private double DiskTest()
        {
            string path = Path.Combine(Path.GetTempPath(), "deeptools_bench.tmp");
            const int blockSize = 4 * 1024 * 1024;
            const int blocks = 32;
            byte[] block = new byte[blockSize];
            new Random(7).NextBytes(block);

            try
            {
                var sw = Stopwatch.StartNew();
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, blockSize))
                {
                    for (int i = 0; i < blocks; i++) fs.Write(block, 0, blockSize);
                    fs.Flush(true); // дожидаемся, пока данные реально уйдут на диск
                }
                sw.Stop();
                return (double)blockSize * blocks / sw.Elapsed.TotalSeconds / 1024 / 1024;
            }
            catch
            {
                return 0;
            }
            finally
            {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
