using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeepTools
{
    // Уведомления из трея. Icon задаёт MainForm при старте.
    // Кулдаун 5 минут, чтобы алерты (например о перегреве) не спамили
    public static class TrayNotify
    {
        private static NotifyIcon _icon;
        private static DateTime lastShown = DateTime.MinValue;

        // Действие, которое выполнится при клике по текущему уведомлению
        // (например, открыть страницу загрузки при обновлении)
        private static Action pendingClick;

        public static NotifyIcon Icon
        {
            get { return _icon; }
            set
            {
                _icon = value;
                if (_icon != null)
                {
                    _icon.BalloonTipClicked += (s, e) =>
                    {
                        Action a = pendingClick;
                        pendingClick = null;
                        if (a != null) { try { a(); } catch { } }
                    };
                }
            }
        }

        public static void Warn(string title, string message)
        {
            if (Icon == null) return;
            if ((DateTime.Now - lastShown).TotalMinutes < 5) return;
            lastShown = DateTime.Now;
            pendingClick = null;
            try { Icon.ShowBalloonTip(6000, title, message, ToolTipIcon.Warning); } catch { }
        }

        // Информационное уведомление без кулдауна (отчёт после игры и т.п.)
        public static void Info(string title, string message)
        {
            Info(title, message, null);
        }

        // Вариант с действием по клику
        public static void Info(string title, string message, Action onClick)
        {
            if (Icon == null) return;
            pendingClick = onClick;
            try { Icon.ShowBalloonTip(8000, title, message, ToolTipIcon.Info); } catch { }
        }
    }

    // Последние показания мониторинга. Обновляет HealthCheckPanel (его таймер
    // работает всегда, даже когда вкладка скрыта), читает меню трея и главная страница
    public static class SystemStats
    {
        public static string CpuLoad = "—";
        public static string CpuTemp = "—";
        public static string RamLoad = "—";
        public static string GpuLoad = "—";
        public static string GpuTemp = "—";

        // Числовые версии для трекера игровых сессий (-1 = нет данных)
        public static float CpuLoadNum = -1;
        public static int CpuTempNum = -1;
        public static int GpuTempNum = -1;
    }

    public class TrayMenuItem
    {
        public string Icon;
        public string Text;
        public Action OnClick;
        public bool Danger;

        public TrayMenuItem(string icon, string text, Action onClick)
        {
            Icon = icon;
            Text = text;
            OnClick = onClick;
        }
    }

    // Кастомное меню трея в стиле приложения вместо стандартного ContextMenuStrip
    public class TrayMenuForm : Form
    {
        private const int Pad = 6;
        private const int HeaderH = 48;
        private const int StatsH = 46;
        private const int ItemH = 36;
        private const int MenuWidth = 200;

        private List<TrayMenuItem> items;
        private int hoverIndex = -1;
        private System.Windows.Forms.Timer fadeTimer;

        public TrayMenuForm(List<TrayMenuItem> menuItems)
        {
            items = menuItems;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.SidebarColor;
            DoubleBuffered = true;
            Width = MenuWidth;
            Height = HeaderH + StatsH + Pad + items.Count * ItemH + Pad;
            Opacity = 0;

            Deactivate += (s, e) => Close();
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            MouseMove += OnMenuMouseMove;
            MouseLeave += (s, e) => { hoverIndex = -1; Invalidate(); };
            MouseUp += OnMenuMouseUp;

            Load += (s, e) => ApplyRoundedRegion();
        }

        // Показывает меню возле курсора, не вылезая за края экрана
        public static void Popup(List<TrayMenuItem> menuItems)
        {
            var f = new TrayMenuForm(menuItems);
            Point cur = Cursor.Position;
            Rectangle wa = Screen.FromPoint(cur).WorkingArea;

            int x = Math.Max(wa.Left + 4, Math.Min(cur.X, wa.Right - f.Width - 4));
            int y = cur.Y - f.Height - 6;
            if (y < wa.Top + 4) y = Math.Min(cur.Y + 6, wa.Bottom - f.Height - 4);

            f.Location = new Point(x, y);
            f.Show();
            f.Activate();
            f.StartFadeIn();
        }

        private void StartFadeIn()
        {
            fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
            fadeTimer.Tick += (s, e) => {
                // Форму могли закрыть (Deactivate/Выход) до конца анимации -
                // тогда обращение к Opacity кинет ObjectDisposedException
                if (IsDisposed || Disposing) { fadeTimer.Stop(); fadeTimer.Dispose(); return; }
                Opacity = Math.Min(1.0, Opacity + 0.18);
                if (Opacity >= 1.0) { fadeTimer.Stop(); fadeTimer.Dispose(); }
            };
            fadeTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Гасим таймер анимации, чтобы он не тикал по закрытой форме
            if (fadeTimer != null)
            {
                try { fadeTimer.Stop(); fadeTimer.Dispose(); } catch { }
                fadeTimer = null;
            }
            base.OnFormClosed(e);
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

        private Rectangle ItemRect(int index)
        {
            return new Rectangle(Pad, HeaderH + StatsH + Pad + index * ItemH, Width - Pad * 2, ItemH);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (ItemRect(i).Contains(p)) return i;
            }
            return -1;
        }

        private void OnMenuMouseMove(object sender, MouseEventArgs e)
        {
            int idx = HitTest(e.Location);
            if (idx != hoverIndex)
            {
                hoverIndex = idx;
                Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        private void OnMenuMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int idx = HitTest(e.Location);
            if (idx < 0) return;

            Action action = items[idx].OnClick;
            Close();
            if (action != null) action();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Фон и рамка
            var bgRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Theme.RoundedRect(bgRect, 12))
            using (var bgBrush = new SolidBrush(Theme.SidebarColor))
            using (var borderPen = new Pen(Theme.BorderColor))
            {
                g.FillPath(bgBrush, path);
                g.DrawPath(borderPen, path);
            }

            // Шапка: точка-логотип, название, подпись
            using (var glow = new SolidBrush(Color.FromArgb(50, Theme.Accent)))
                g.FillEllipse(glow, 12, 13, 16, 16);
            using (var dotBrush = new SolidBrush(Theme.Accent))
                g.FillEllipse(dotBrush, 15, 16, 10, 10);

            using (var titleFont = new Font("Segoe UI", 10F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "DeepTools", titleFont, new Point(32, 8), Theme.TextMain, TextFormatFlags.NoPadding);
            }

            using (var subFont = new Font("Segoe UI", 7.5F))
            {
                TextRenderer.DrawText(g, Lang.T("работает в фоне", "running in background"), subFont, new Point(33, 27), Theme.TextDim, TextFormatFlags.NoPadding);
            }

            // Разделитель под шапкой
            using (var divPen = new Pen(Theme.BorderColor))
            {
                g.DrawLine(divPen, Pad + 4, HeaderH, Width - Pad - 4, HeaderH);
            }

            // Блок мониторинга: загрузка и температура CPU/GPU
            using (var statLabelFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (var statValueFont = new Font("Segoe UI", 8.5F))
            {
                int sy = HeaderH + 6;
                TextRenderer.DrawText(g, "CPU", statLabelFont, new Point(16, sy), Theme.TextDim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, SystemStats.CpuLoad + "  •  " + SystemStats.CpuTemp, statValueFont, new Point(56, sy), Theme.TextMain, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "GPU", statLabelFont, new Point(16, sy + 19), Theme.TextDim, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, SystemStats.GpuLoad + "  •  " + SystemStats.GpuTemp, statValueFont, new Point(56, sy + 19), Theme.TextMain, TextFormatFlags.NoPadding);
            }

            using (var divPen2 = new Pen(Theme.BorderColor))
            {
                g.DrawLine(divPen2, Pad + 4, HeaderH + StatsH, Width - Pad - 4, HeaderH + StatsH);
            }

            // Пункты меню
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var rect = ItemRect(i);
                bool hovered = i == hoverIndex;

                if (hovered)
                {
                    Color hoverBg = item.Danger
                        ? Color.FromArgb(45, Theme.Danger)
                        : Theme.NavActiveBg;
                    using (var hPath = Theme.RoundedRect(rect, 8))
                    using (var hBrush = new SolidBrush(hoverBg))
                    {
                        g.FillPath(hBrush, hPath);
                    }
                }

                Color textColor = item.Danger
                    ? (hovered ? Theme.Danger : Theme.TextDim)
                    : (hovered ? Theme.Accent : Theme.TextMain);

                using (var iconFont = new Font("Segoe UI", 10F))
                {
                    TextRenderer.DrawText(g, item.Icon, iconFont, new Rectangle(rect.X + 4, rect.Y, 28, rect.Height), textColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }

                using (var textFont = new Font("Segoe UI", 9.5F, hovered ? FontStyle.Bold : FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, item.Text, textFont, new Rectangle(rect.X + 36, rect.Y, rect.Width - 40, rect.Height), textColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
