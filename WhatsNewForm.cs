using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // «Что нового» - окно, которое само показывается один раз после обновления:
    // при первом запуске новой версии сравниваем версию из конфига с текущей.
    // Список изменений редактируется тут же (Notes) при каждом релизе
    public static class WhatsNew
    {
        // Ревизия списка изменений: позволяет показать окно ещё раз,
        // когда фичи доехали без смены номера версии
        private const string NotesRev = "1.8.0-b";

        public static void ShowIfUpdated(Form owner)
        {
            try
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string current = v.Major + "." + v.Minor + "." + v.Build;
                string seen = AppConfig.Get("last_seen_version", "");
                string seenRev = AppConfig.Get("last_seen_notes_rev", "");
                if (seen == current && seenRev == NotesRev) return;
                AppConfig.Set("last_seen_version", current);
                AppConfig.Set("last_seen_notes_rev", NotesRev);

                // seen == "" - либо чистая установка, либо обновление со старой
                // версии, где этого ключа ещё не было: показываем как приветствие
                using (var form = new WhatsNewForm(current, seen == ""))
                    form.ShowDialog(owner);
            }
            catch { }
        }
    }

    public class WhatsNewForm : Form
    {
        // Что показываем в списке изменений текущей версии
        private static string[][] Notes
        {
            get
            {
                return new string[][]
                {
                    new[] { "🌐", Lang.T("Хирург латентности: режет фоновый трафик во время игры, приоритизирует пакеты через QoS и показывает packet loss в оверлее",
                                         "Latency Surgeon: kills background traffic during games, re-maps priorities via QoS and shows packet loss in the FPS overlay") },
                    new[] { "⚡", Lang.T("Адаптивный профиль питания: сам переключает план по контексту — максималка в игре, баланс в браузере, экономия на простое",
                                         "Adaptive Power Profile: switches power plan by context — max performance in a game, balanced in a browser, saver on idle") },
                    new[] { "🌈", Lang.T("RGB-реакция: цвет подсветки следует температуре CPU/GPU через OpenRGB, пульсирует под нагрузкой и мигает при просадке FPS",
                                         "RGB Reactive: lighting colour tracks CPU/GPU temperature via OpenRGB, pulses under load and flashes red when FPS drops") },
                    new[] { "🎮", Lang.T("Дефрагментация VRAM: очищает фрагментированную видеопамять через DirectX без перезапуска игры",
                                         "VRAM Defrag: flushes fragmented video memory via DirectX without restarting the game") },
                    new[] { "📸", Lang.T("Мгновенный скриншот: прямой GPU→диск через DirectStorage — ноль фризов даже в самых тяжёлых сценах",
                                         "Zero-Latency Screenshot: direct GPU→Disk via DirectStorage — zero frametime spike even in the heaviest scenes") },
                    new[] { "🔀", Lang.T("DNS-переключатель: Cloudflare / Google / AdGuard или авто (DHCP) на все активные адаптеры в один клик, с моментальным сбросом кэша DNS",
                                         "DNS switcher: Cloudflare / Google / AdGuard or Auto (DHCP) on all active adapters in one click, with an instant DNS cache flush") },
                };
            }
        }

        private readonly Timer confettiTimer;
        private readonly List<Confetto> confetti = new List<Confetto>();
        private int confettiTicks = 0;

        private class Confetto
        {
            public float X, Y, Vy, Sway, Phase, Size;
            public Color Color;
        }

        public WhatsNewForm(string version, bool firstRun)
        {
            Text = Lang.T("Что нового", "What's new");
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(470, 236 + Notes.Length * 44);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            ShowInTaskbar = false;
            DoubleBuffered = true;

            Load += (s, e) =>
            {
                var path = new GraphicsPath();
                int r = 14, d = r * 2;
                var rect = new Rectangle(0, 0, Width, Height);
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                Region = new Region(path);
            };

            var title = new Label
            {
                Text = firstRun
                    ? "🎉 " + Lang.T("Добро пожаловать в DeepTools!", "Welcome to DeepTools!")
                    : "🎉 " + Lang.T("DeepTools обновился!", "DeepTools got an update!"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(28, 30),
                AutoSize = true
            };
            Controls.Add(title);

            var verLabel = new Label
            {
                Text = Lang.T("Версия ", "Version ") + version,
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(30, 66),
                AutoSize = true
            };
            Controls.Add(verLabel);

            int y = 108;
            foreach (string[] note in Notes)
            {
                var icon = new Label
                {
                    Text = note[0],
                    ForeColor = Theme.Accent,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 13F),
                    Location = new Point(28, y),
                    AutoSize = true
                };
                Controls.Add(icon);

                var text = new Label
                {
                    Text = note[1],
                    ForeColor = Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9.5F),
                    Location = new Point(64, y + 2),
                    Size = new Size(376, 38)
                };
                Controls.Add(text);
                y += 44;
            }

            var okBtn = new RoundedButton
            {
                Text = Lang.T("Круто!", "Nice!"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Color.FromArgb(10, 20, 15),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(140, 36),
                Location = new Point((470 - 140) / 2, y + 18)
            };
            okBtn.Click += (s, e) => Close();
            Controls.Add(okBtn);

            // Конфетти: падает несколько секунд и затихает
            var rnd = new Random();
            Color[] palette = { Theme.Accent, Theme.Warning, Color.FromArgb(90, 160, 255), Color.FromArgb(235, 110, 170) };
            for (int i = 0; i < 70; i++)
            {
                confetti.Add(new Confetto
                {
                    X = rnd.Next(10, 460),
                    Y = -rnd.Next(0, 300),
                    Vy = 1.6f + (float)rnd.NextDouble() * 2.4f,
                    Sway = 0.6f + (float)rnd.NextDouble() * 1.4f,
                    Phase = (float)rnd.NextDouble() * 6.28f,
                    Size = 4 + rnd.Next(4),
                    Color = palette[rnd.Next(palette.Length)]
                });
            }
            confettiTimer = new Timer { Interval = 33 };
            confettiTimer.Tick += (s, e) =>
            {
                confettiTicks++;
                foreach (Confetto c in confetti)
                {
                    c.Y += c.Vy;
                    c.X += (float)Math.Sin(c.Phase + confettiTicks * 0.08f) * c.Sway;
                }
                // Через ~6 секунд всё уже упало за край - хватит перерисовывать
                if (confettiTicks > 180) confettiTimer.Stop();
                Invalidate();
            };
            confettiTimer.Start();
            FormClosed += (s, e) => confettiTimer.Dispose();

            var esc = new Button { Size = new Size(0, 0), TabStop = false };
            esc.Click += (s, e) => Close();
            Controls.Add(esc);
            CancelButton = esc;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (Confetto c in confetti)
            {
                if (c.Y < -10 || c.Y > Height + 10) continue;
                using (var b = new SolidBrush(Color.FromArgb(200, c.Color)))
                    e.Graphics.FillEllipse(b, c.X, c.Y, c.Size, c.Size);
            }
            using (var pen = new Pen(Theme.BorderColor))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
