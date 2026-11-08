using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Management;
using System.Windows.Forms;

namespace DeepTools
{
    // PNG-карточка «Поделиться»: спека ПК + топ игр со временем и средним FPS,
    // в стиле результатов спидтестов - для форумов и «смотри какой у меня ПК».
    // Картинка копируется в буфер и сохраняется в Изображения\DeepTools
    public static class ShareCard
    {
        public class GameLine
        {
            public string Name;
            public int TotalSec;
            public int AvgFps; // -1 если не замерялся
        }

        // Собирает спеку, рендерит карточку, кладёт в буфер и на диск.
        // callback(путь или null) приходит в UI-потоке
        public static void CreateGameTimeCard(List<GameLine> games, int totalSec, int weekSec, Action<string> callback)
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => { e.Result = CollectSpecs(); };
            worker.RunWorkerCompleted += (s, e) => {
                string[] specs = (string[])e.Result;
                string path = null;
                try
                {
                    using (Bitmap bmp = Render(specs, games, totalSec, weekSec))
                    {
                        try { Clipboard.SetImage(bmp); } catch { }

                        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DeepTools");
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        path = Path.Combine(dir, "gametime_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                        bmp.Save(path, ImageFormat.Png);
                    }
                }
                catch { path = null; }
                if (callback != null) callback(path);
            };
            worker.RunWorkerAsync();
        }

        // CPU / GPU / RAM одной строкой каждый (WMI, зовётся в фоне)
        private static string[] CollectSpecs()
        {
            string cpu = "", gpu = "", ram = "";
            try
            {
                var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    cpu = Convert.ToString(obj["Name"]).Trim();
                    break;
                }
            }
            catch { }
            try
            {
                // Берём видеокарту с максимальной памятью - дискретную, а не встройку
                long bestRam = -1;
                var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = Convert.ToString(obj["Name"]);
                    if (string.IsNullOrEmpty(name)) continue;
                    long vram = 0;
                    try { vram = Convert.ToInt64(obj["AdapterRAM"]); } catch { }
                    if (vram > bestRam) { bestRam = vram; gpu = name.Trim(); }
                }
            }
            catch { }
            try
            {
                double totalGb = 0;
                var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in searcher.Get())
                    totalGb += Convert.ToDouble(obj["Capacity"]) / 1024 / 1024 / 1024;
                if (totalGb > 0) ram = Math.Round(totalGb) + Lang.T(" ГБ RAM", " GB RAM");
            }
            catch { }
            return new[] { cpu, gpu, ram };
        }

        private static Bitmap Render(string[] specs, List<GameLine> games, int totalSec, int weekSec)
        {
            const int W = 860;
            int rows = Math.Min(games.Count, 5);
            int H = 300 + rows * 54;

            var bmp = new Bitmap(W, H);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                // Фон: фирменный тёмный градиент независимо от темы программы
                using (var bg = new LinearGradientBrush(new Rectangle(0, 0, W, H),
                    Color.FromArgb(16, 20, 30), Color.FromArgb(24, 30, 46), 55f))
                    g.FillRectangle(bg, 0, 0, W, H);

                Color accent = Color.FromArgb(64, 224, 168);
                Color text = Color.FromArgb(235, 240, 248);
                Color dim = Color.FromArgb(140, 150, 168);

                // Шапка
                using (var f = new Font("Segoe UI", 21F, FontStyle.Bold))
                    g.DrawString("DeepTools", f, new SolidBrush(accent), 36, 28);
                using (var f = new Font("Segoe UI", 10F))
                    g.DrawString(Lang.T("Время в играх", "Game time"), f, new SolidBrush(dim), 40, 70);

                using (var f = new Font("Segoe UI", 9F))
                {
                    string date = DateTime.Now.ToString("dd.MM.yyyy");
                    SizeF sz = g.MeasureString(date, f);
                    g.DrawString(date, f, new SolidBrush(dim), W - 36 - sz.Width, 34);
                }

                // Спека ПК
                int sy = 104;
                using (var f = new Font("Segoe UI", 9.5F))
                {
                    for (int i = 0; i < specs.Length; i++)
                    {
                        if (string.IsNullOrEmpty(specs[i])) continue;
                        g.FillEllipse(new SolidBrush(accent), 40, sy + 6, 6, 6);
                        g.DrawString(specs[i], f, new SolidBrush(text), 54, sy);
                        sy += 24;
                    }
                }

                // Итоги: всего и за неделю
                using (var f = new Font("Segoe UI", 10.5F, FontStyle.Bold))
                {
                    string totals = Lang.T("Всего: ", "Total: ") + GameSessionTracker.FormatDuration(totalSec)
                        + Lang.T("      За 7 дней: ", "      Last 7 days: ") + GameSessionTracker.FormatDuration(weekSec);
                    g.DrawString(totals, f, new SolidBrush(accent), 36, sy + 10);
                }

                // Топ игр
                int y = sy + 48;
                using (var nameF = new Font("Segoe UI", 11F, FontStyle.Bold))
                using (var statF = new Font("Segoe UI", 10F))
                using (var rankF = new Font("Segoe UI", 10F, FontStyle.Bold))
                {
                    for (int i = 0; i < rows; i++)
                    {
                        GameLine gl = games[i];

                        // Строка-плашка со скруглением
                        var rect = new Rectangle(36, y, W - 72, 46);
                        using (var path = Rounded(rect, 10))
                        using (var rowBg = new SolidBrush(Color.FromArgb(34, 42, 60)))
                            g.FillPath(rowBg, path);

                        g.DrawString("#" + (i + 1), rankF, new SolidBrush(dim), 52, y + 13);
                        g.DrawString(gl.Name + ".exe", nameF, new SolidBrush(text), 88, y + 11);

                        string right = GameSessionTracker.FormatDuration(gl.TotalSec);
                        if (gl.AvgFps > 0) right += "   ·   " + gl.AvgFps + " FPS";
                        SizeF sz = g.MeasureString(right, statF);
                        g.DrawString(right, statF, new SolidBrush(accent), W - 52 - sz.Width, y + 13);

                        y += 54;
                    }
                }

                // Футер
                using (var f = new Font("Segoe UI", 8.5F))
                    g.DrawString("github.com/" + UpdateChecker.Repo, f, new SolidBrush(dim), 36, H - 30);
            }
            return bmp;
        }

        private static GraphicsPath Rounded(Rectangle rect, int r)
        {
            int d = r * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
