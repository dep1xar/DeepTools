using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    public class ScreenshotsPanel : Panel
    {
        public string ScreenshotsFolder;
        public event EventHandler RequestHotkeyCapture;

        private Label hotkeyValueLabel;
        private RoundedButton regionBtn;
        private Label captureHint;
        private FlowLayoutPanel gallery;

        // Защита от повторного входа. Хоткей диспатчится из WndProc главного окна,
        // а ShowDialog крутит вложенный цикл сообщений - поэтому каждое лишнее
        // нажатие F6 открывало ЕЩЁ одно полноэкранное окно поверх предыдущего.
        // Отсюда была стопка оверлеев, чёрный экран (новое окно снимало уже
        // затемнённый предыдущим экран) и Esc, который закрывал только верхнее.
        private static bool regionBusy;
        private static bool screenBusy;

        public ScreenshotsPanel()
        {
            Size = new Size(700, 566);
            BackColor = Theme.BgColor;

            ScreenshotsFolder = Path.Combine(Application.StartupPath, "screenshots");
            try
            {
                if (!Directory.Exists(ScreenshotsFolder)) Directory.CreateDirectory(ScreenshotsFolder);
            }
            catch
            {
                // если не смогли создать папку - попробуем ещё раз при первом сохранении
            }

            BuildUi();
            RefreshGallery();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("Скриншоты", "Screenshots"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                Location = new Point(24, 24),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var topCard = Theme.MakeCard(this, new Point(24, 64), new Size(650, 66));

            var hotkeyLabel = new Label
            {
                Text = Lang.T("Хоткей скриншота:", "Screenshot hotkey:"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F),
                Location = new Point(20, 22),
                AutoSize = true
            };
            topCard.Controls.Add(hotkeyLabel);

            hotkeyValueLabel = new Label
            {
                Text = Hotkeys.Get(NativeMethods.HOTKEY_ID_SCREENSHOT).ToString(),
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(150, 21),
                AutoSize = true
            };
            topCard.Controls.Add(hotkeyValueLabel);

            var changeBtn = new RoundedButton
            {
                Text = Lang.T("Изменить", "Change"),
                ButtonColor = Theme.InputColor,
                HoverColor = Color.FromArgb(45, 54, 76),
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(220, 16),
                Size = new Size(90, 28)
            };
            changeBtn.Click += (s, e) => {
                if (RequestHotkeyCapture != null) RequestHotkeyCapture(this, EventArgs.Empty);
            };
            topCard.Controls.Add(changeBtn);

            regionBtn = new RoundedButton
            {
                Text = Lang.T("Область (", "Region (") + Hotkeys.Get(NativeMethods.HOTKEY_ID_REGION) + ")",
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(330, 16),
                Size = new Size(140, 28)
            };
            regionBtn.Click += (s, e) => CaptureRegion();
            topCard.Controls.Add(regionBtn);

            var captureBtn = new RoundedButton
            {
                Text = Lang.T("Весь экран", "Full screen"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(480, 16),
                Size = new Size(150, 28)
            };
            captureBtn.Click += (s, e) => CaptureScreen();
            topCard.Controls.Add(captureBtn);

            captureHint = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(20, 46),
                AutoSize = true
            };
            topCard.Controls.Add(captureHint);

            gallery = new FlowLayoutPanel
            {
                Location = new Point(24, 142),
                Size = new Size(650, 400),
                BackColor = Theme.BgColor,
                AutoScroll = true
            };
            Controls.Add(gallery);
            NativeMethods.ApplyDarkScrollbar(gallery);
            DarkScroll.Attach(gallery);
        }

        public void SetHotkeyDisplay(string text)
        {
            hotkeyValueLabel.Text = text;
        }

        // Обновить подпись кнопки области после переназначения клавиши в Настройках
        public void RefreshRegionHotkey()
        {
            regionBtn.Text = Lang.T("Область (", "Region (") + Hotkeys.Get(NativeMethods.HOTKEY_ID_REGION) + ")";
        }

        // Скриншот выделенной области с разметкой (стрелки, рамки, текст)
        public void CaptureRegion()
        {
            // Один захват за раз - иначе окна копятся стопкой.
            // Заодно не снимаем экран, пока открыт оверлей выделения:
            // иначе в кадр попадал сам оверлей (тот самый «чёрный экран»)
            if (regionBusy || screenBusy) return;
            regionBusy = true;
            try
            {
                using (var form = new RegionCaptureForm())
                {
                    if (form.ShowDialog() != DialogResult.OK || form.ResultImage == null) return;

                    try
                    {
                        if (!Directory.Exists(ScreenshotsFolder)) Directory.CreateDirectory(ScreenshotsFolder);

                        string fileName = "region_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                        string fullPath = Path.Combine(ScreenshotsFolder, fileName);
                        form.ResultImage.Save(fullPath, ImageFormat.Png);

                        bool copied = false;
                        try { Clipboard.SetImage(form.ResultImage); copied = true; } catch { }

                        captureHint.Text = Lang.T("Сохранено: ", "Saved: ") + fileName
                            + (copied ? Lang.T(" (в буфере - вставляй Ctrl+V)", " (in clipboard - paste with Ctrl+V)") : "");
                        RefreshGallery();
                    }
                    catch (Exception ex)
                    {
                        captureHint.Text = Lang.T("Ошибка сохранения: ", "Save error: ") + ex.Message;
                    }
                    finally
                    {
                        form.ResultImage.Dispose();
                    }
                }
            }
            finally
            {
                regionBusy = false;
            }
        }

        public void CaptureScreen()
        {
            // Спам хоткея не должен плодить параллельные захваты и сохранения.
            // Пока открыт оверлей выделения, полный снимок не делаем - в кадр
            // попал бы сам оверлей
            if (screenBusy || regionBusy) return;
            screenBusy = true;

            try
            {
                if (!Directory.Exists(ScreenshotsFolder)) Directory.CreateDirectory(ScreenshotsFolder);

                // Захватываем пиксели синхронно (нужен UI-поток),
                // но сохранение PNG делаем в фоне - это и есть zero-latency:
                // экран захвачен мгновенно, диск не блокирует игру
                Rectangle bounds = SystemInformation.VirtualScreen;
                Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size,
                        CopyPixelOperation.SourceCopy);
                }

                string fileName = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                string fullPath = Path.Combine(ScreenshotsFolder, fileName);

                captureHint.Text = Lang.T("Захват выполнен, сохраняем...", "Captured, saving...");
                captureHint.ForeColor = Theme.TextDim;

                // Копируем в буфер прямо сейчас (до фонового потока - Clipboard не thread-safe)
                bool copied = false;
                try { Clipboard.SetImage(bmp); copied = true; } catch { }

                // Сохранение PNG в фоне - игра не ощущает паузы
                var bw = new System.ComponentModel.BackgroundWorker();
                bw.DoWork += delegate(object s2, System.ComponentModel.DoWorkEventArgs e2) {
                    bmp.Save(fullPath, ImageFormat.Png);
                    bmp.Dispose();
                };
                bw.RunWorkerCompleted += delegate(object s2, System.ComponentModel.RunWorkerCompletedEventArgs e2) {
                    screenBusy = false;
                    if (IsDisposed) return;
                    captureHint.Text = Lang.T("Сохранено: ", "Saved: ") + fileName
                        + (copied ? Lang.T(" (скопировано в буфер - вставляй Ctrl+V)", " (copied to clipboard - paste with Ctrl+V)") : "");
                    captureHint.ForeColor = Theme.Accent;
                    RefreshGallery();
                };
                bw.RunWorkerAsync();
            }
            catch (Exception ex)
            {
                screenBusy = false;
                captureHint.Text = Lang.T("Ошибка сохранения: ", "Save error: ") + ex.Message;
            }
        }

        private void RefreshGallery()
        {
            // Controls.Clear() не освобождает картинки, а PictureBox не диспозит
            // свою Image - каждый Image.FromFile держал ещё и файловый handle.
            // Без явного Dispose хендлы накапливались и интерфейс начинал лагать
            for (int i = gallery.Controls.Count - 1; i >= 0; i--)
            {
                Control item = gallery.Controls[i];
                gallery.Controls.RemoveAt(i);
                DisposeThumbnails(item);
                item.Dispose();
            }

            if (!Directory.Exists(ScreenshotsFolder)) return;

            string[] files = Directory.GetFiles(ScreenshotsFolder, "*.png");
            Array.Sort(files);
            Array.Reverse(files);

            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                gallery.Controls.Add(MakeThumbnail(path));
            }
        }

        // Рекурсивно освобождает картинки превью перед удалением контролов
        private static void DisposeThumbnails(Control parent)
        {
            if (parent == null) return;
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control child = parent.Controls[i];
                PictureBox pb = child as PictureBox;
                if (pb != null && pb.Image != null)
                {
                    try { pb.Image.Dispose(); } catch { }
                    pb.Image = null;
                }
                DisposeThumbnails(child);
            }
        }

        private Panel MakeThumbnail(string path)
        {
            var item = new Panel { Size = new Size(140, 110), BackColor = Theme.CardColor, Margin = new Padding(6) };

            var pic = new PictureBox
            {
                Size = new Size(132, 78),
                Location = new Point(4, 4),
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Hand
            };
            // Читаем через поток и сразу снимаем копию: Image.FromFile держит
            // файл открытым, из-за чего его нельзя было удалить/перезаписать
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var tmp = Image.FromStream(fs))
                {
                    pic.Image = new Bitmap(tmp);
                }
            }
            catch { }
            pic.Click += (s, e) => {
                try { Process.Start(path); } catch { }
            };
            item.Controls.Add(pic);

            var nameLbl = new Label
            {
                Text = Path.GetFileNameWithoutExtension(path),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7F),
                Location = new Point(4, 86),
                Size = new Size(132, 14),
                TextAlign = ContentAlignment.MiddleCenter
            };
            item.Controls.Add(nameLbl);

            var menu = new ContextMenuStrip();
            menu.Items.Add(Lang.T("Открыть", "Open"), null, (s, e) => { try { Process.Start(path); } catch { } });
            menu.Items.Add(Lang.T("Удалить", "Delete"), null, (s, e) => {
                try
                {
                    if (pic.Image != null) { pic.Image.Dispose(); }
                    File.Delete(path);
                }
                catch { }
                RefreshGallery();
            });
            item.ContextMenuStrip = menu;
            pic.ContextMenuStrip = menu;

            return item;
        }
    }
}
