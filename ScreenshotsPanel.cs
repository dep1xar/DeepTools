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

        public void CaptureScreen()
        {
            try
            {
                if (!Directory.Exists(ScreenshotsFolder)) Directory.CreateDirectory(ScreenshotsFolder);

                Rectangle bounds = SystemInformation.VirtualScreen;
                Bitmap bmp = new Bitmap(bounds.Width, bounds.Height);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                }

                string fileName = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                string fullPath = Path.Combine(ScreenshotsFolder, fileName);
                bmp.Save(fullPath, ImageFormat.Png);

                // Копируем в буфер обмена, чтобы сразу вставлять через Ctrl+V.
                // Clipboard.SetImage сам делает копию, поэтому bmp можно освобождать
                bool copied = false;
                try
                {
                    Clipboard.SetImage(bmp);
                    copied = true;
                }
                catch
                {
                    // буфер может быть занят другим приложением - скриншот всё равно сохранён
                }
                bmp.Dispose();

                captureHint.Text = Lang.T("Сохранено: ", "Saved: ") + fileName
                    + (copied ? Lang.T(" (скопировано в буфер - вставляй Ctrl+V)", " (copied to clipboard - paste with Ctrl+V)") : "");
                RefreshGallery();
            }
            catch (Exception ex)
            {
                captureHint.Text = Lang.T("Ошибка сохранения: ", "Save error: ") + ex.Message;
            }
        }

        private void RefreshGallery()
        {
            gallery.Controls.Clear();

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
            try { pic.Image = Image.FromFile(path); } catch { }
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