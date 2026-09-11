using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace DeepTools
{
    // Заметки-стикеры на рабочем столе. Живут независимо от главного окна,
    // сохраняются в AppData и восстанавливаются при запуске программы.
    // Файл заметки: первая строка "x|y|w|h|напоминание" (5-е поле опционально,
    // формат yyyy-MM-dd HH:mm), остальное - текст
    public static class NotesManager
    {
        private static readonly List<StickyNoteForm> open = new List<StickyNoteForm>();
        private static System.Windows.Forms.Timer reminderTimer;

        public static string Dir
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools"),
                    "notes");
            }
        }

        public static void CreateNew()
        {
            string id = Guid.NewGuid().ToString("N").Substring(0, 12);
            var wa = Screen.PrimaryScreen.WorkingArea;
            var bounds = new Rectangle(wa.Left + wa.Width / 2 - 110, wa.Top + wa.Height / 2 - 90, 220, 180);
            Spawn(id, "", bounds, DateTime.MinValue);
        }

        public static void RestoreAll()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                string[] files = Directory.GetFiles(Dir, "*.txt");
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        string id = Path.GetFileNameWithoutExtension(files[i]);
                        string[] lines = File.ReadAllLines(files[i]);
                        string head = lines.Length > 0 ? lines[0] : "";
                        Rectangle b = ParseBounds(head);
                        DateTime reminder = ParseReminder(head);
                        string content = lines.Length > 1 ? string.Join(Environment.NewLine, Sub(lines, 1)) : "";
                        Spawn(id, content, b, reminder);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void Spawn(string id, string content, Rectangle bounds, DateTime reminder)
        {
            var note = new StickyNoteForm(id, content, bounds, reminder);
            note.FormClosed += (s, e) => open.Remove(note);
            open.Add(note);
            note.Show();
            EnsureReminderTimer();
        }

        // Один общий таймер на все заметки: раз в 20 секунд проверяем, не пора ли напомнить
        private static void EnsureReminderTimer()
        {
            if (reminderTimer != null) return;
            reminderTimer = new System.Windows.Forms.Timer { Interval = 20 * 1000 };
            reminderTimer.Tick += (s, e) => {
                for (int i = open.Count - 1; i >= 0; i--)
                {
                    try { open[i].CheckReminder(); } catch { }
                }
            };
            reminderTimer.Start();
        }

        public static void Save(string id, Rectangle bounds, string content, DateTime reminder)
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                string head = bounds.X + "|" + bounds.Y + "|" + bounds.Width + "|" + bounds.Height;
                if (reminder != DateTime.MinValue)
                    head += "|" + reminder.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                lines.Add(head);
                lines.AddRange((content ?? "").Replace("\r\n", "\n").Split('\n'));
                File.WriteAllLines(Path.Combine(Dir, id + ".txt"), lines.ToArray());
            }
            catch { }
        }

        public static void Delete(string id)
        {
            try
            {
                string path = Path.Combine(Dir, id + ".txt");
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private static Rectangle ParseBounds(string line)
        {
            try
            {
                string[] p = line.Split('|');
                if (p.Length >= 4)
                {
                    int x = int.Parse(p[0]), y = int.Parse(p[1]), w = int.Parse(p[2]), h = int.Parse(p[3]);
                    if (w < 140) w = 220;
                    if (h < 100) h = 180;
                    return new Rectangle(x, y, w, h);
                }
            }
            catch { }
            var wa = Screen.PrimaryScreen.WorkingArea;
            return new Rectangle(wa.Left + 60, wa.Top + 60, 220, 180);
        }

        private static DateTime ParseReminder(string line)
        {
            try
            {
                string[] p = line.Split('|');
                DateTime when;
                if (p.Length >= 5 && DateTime.TryParseExact(p[4], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out when))
                    return when;
            }
            catch { }
            return DateTime.MinValue;
        }

        private static string[] Sub(string[] arr, int from)
        {
            var list = new List<string>();
            for (int i = from; i < arr.Length; i++) list.Add(arr[i]);
            return list.ToArray();
        }
    }

    public class StickyNoteForm : Form
    {
        private static readonly Color NoteBg = Color.FromArgb(255, 232, 138);
        private static readonly Color NoteBar = Color.FromArgb(245, 214, 110);
        private static readonly Color NoteText = Color.FromArgb(45, 40, 20);
        private static readonly Color BellActive = Color.FromArgb(200, 90, 20);

        private readonly string id;
        private TextBox textBox;
        private Label bellBtn;
        private DateTime reminder = DateTime.MinValue;
        private Point dragStart;
        private bool dragging = false;
        private bool suppressSave = true;

        public StickyNoteForm(string noteId, string content, Rectangle bounds, DateTime reminderAt)
        {
            id = noteId;
            reminder = reminderAt;
            // Просроченное напоминание (программа была выключена) - показываем сразу после старта
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            MinimumSize = new Size(140, 100);
            BackColor = NoteBg;

            var bar = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = NoteBar };
            bar.MouseDown += (s, e) => { dragging = true; dragStart = new Point(e.X, e.Y); };
            bar.MouseMove += (s, e) => {
                if (dragging) Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            bar.MouseUp += (s, e) => { dragging = false; SaveNote(); };
            Controls.Add(bar);

            var addBtn = new Label
            {
                Text = "+",
                ForeColor = NoteText,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Size = new Size(24, 24),
                Location = new Point(0, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            addBtn.Click += (s, e) => NotesManager.CreateNew();
            bar.Controls.Add(addBtn);

            // Напоминание: клик - задать время, повторный клик - убрать
            bellBtn = new Label
            {
                Text = "⏰",
                ForeColor = reminder != DateTime.MinValue ? BellActive : NoteText,
                Font = new Font("Segoe UI", 9F),
                Size = new Size(24, 24),
                Location = new Point(24, 0),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            UpdateBellTip();
            bellBtn.Click += (s, e) => {
                if (reminder != DateTime.MinValue)
                {
                    reminder = DateTime.MinValue;
                    bellBtn.ForeColor = NoteText;
                    UpdateBellTip();
                    SaveNote();
                    return;
                }
                DateTime when = ReminderPrompt.Ask(this);
                if (when != DateTime.MinValue)
                {
                    reminder = when;
                    bellBtn.ForeColor = BellActive;
                    UpdateBellTip();
                    SaveNote();
                }
            };
            bar.Controls.Add(bellBtn);

            var closeBtn = new Label
            {
                Text = "✕",
                ForeColor = NoteText,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(24, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(Width - 24, 0)
            };
            closeBtn.Click += (s, e) => { NotesManager.Delete(id); Close(); };
            bar.Controls.Add(closeBtn);

            textBox = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = NoteBg,
                ForeColor = NoteText,
                Font = new Font("Segoe UI", 10F),
                ScrollBars = ScrollBars.Vertical,
                Text = content ?? ""
            };
            textBox.TextChanged += (s, e) => SaveNote();
            Controls.Add(textBox);
            textBox.BringToFront();

            // Изменение размера за правый-нижний угол
            var grip = new Label
            {
                Text = "◢",
                ForeColor = NoteText,
                Font = new Font("Segoe UI", 8F),
                Size = new Size(16, 16),
                TextAlign = ContentAlignment.BottomRight,
                Cursor = Cursors.SizeNWSE,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                Location = new Point(Width - 16, Height - 16)
            };
            bool resizing = false; Point resizeStart = Point.Empty; Size startSize = Size.Empty;
            grip.MouseDown += (s, e) => { resizing = true; resizeStart = grip.PointToScreen(e.Location); startSize = Size; };
            grip.MouseMove += (s, e) => {
                if (!resizing) return;
                Point now = grip.PointToScreen(e.Location);
                Width = Math.Max(MinimumSize.Width, startSize.Width + (now.X - resizeStart.X));
                Height = Math.Max(MinimumSize.Height, startSize.Height + (now.Y - resizeStart.Y));
            };
            grip.MouseUp += (s, e) => { resizing = false; SaveNote(); };
            Controls.Add(grip);
            grip.BringToFront();

            LocationChanged += (s, e) => SaveNote();
            Load += (s, e) => { suppressSave = false; SaveNote(); };
        }

        private void UpdateBellTip()
        {
            DarkTip.Set(bellBtn, reminder != DateTime.MinValue
                ? Lang.T("Напомнит ", "Reminder at ") + reminder.ToString("dd.MM HH:mm") + Lang.T(" (клик - убрать)", " (click to remove)")
                : Lang.T("Напомнить в заданное время", "Remind me at a set time"));
        }

        // Зовётся таймером NotesManager. Когда время пришло - тост из трея,
        // заметка выпрыгивает наверх, напоминание сбрасывается
        public void CheckReminder()
        {
            if (reminder == DateTime.MinValue || DateTime.Now < reminder) return;

            reminder = DateTime.MinValue;
            bellBtn.ForeColor = NoteText;
            UpdateBellTip();
            SaveNote();

            string preview = (textBox.Text ?? "").Trim();
            int nl = preview.IndexOf('\n');
            if (nl > 0) preview = preview.Substring(0, nl).Trim();
            if (preview.Length > 60) preview = preview.Substring(0, 60) + "…";
            if (preview.Length == 0) preview = Lang.T("(пустая заметка)", "(empty note)");

            TrayNotify.Info("⏰ " + Lang.T("Напоминание", "Reminder"), preview, () => {
                try { Activate(); } catch { }
            });

            // Вытаскиваем стикер поверх окон, чтобы точно попался на глаза
            try
            {
                TopMost = true;
                Activate();
                TopMost = false;
            }
            catch { }
        }

        private void SaveNote()
        {
            if (suppressSave) return;
            NotesManager.Save(id, Bounds, textBox.Text, reminder);
        }
    }

    // Мини-диалог "во сколько напомнить": поле ЧЧ:ММ + быстрые кнопки
    public static class ReminderPrompt
    {
        // Возвращает DateTime.MinValue при отмене. Если время уже прошло - ставим на завтра
        public static DateTime Ask(Form owner)
        {
            DateTime result = DateTime.MinValue;

            using (var f = new Form())
            {
                f.FormBorderStyle = FormBorderStyle.None;
                f.StartPosition = FormStartPosition.Manual;
                f.Size = new Size(190, 112);
                f.BackColor = Theme.SidebarColor;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                Point p = owner.PointToScreen(new Point(0, 24));
                f.Location = p;
                f.Deactivate += (s, e) => f.Close();

                var lbl = new Label
                {
                    Text = Lang.T("Напомнить в:", "Remind at:"),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.5F),
                    Location = new Point(12, 10),
                    AutoSize = true
                };
                f.Controls.Add(lbl);

                var box = new TextBox
                {
                    Text = DateTime.Now.AddHours(1).ToString("HH:00"),
                    BackColor = Theme.InputColor,
                    ForeColor = Theme.TextMain,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                    Location = new Point(12, 32),
                    Size = new Size(76, 28),
                    TextAlign = HorizontalAlignment.Center
                };
                f.Controls.Add(box);

                var okBtn = new RoundedButton
                {
                    Text = "OK",
                    ButtonColor = Theme.Accent,
                    HoverColor = Theme.AccentHover,
                    TextColor = Theme.BgColor,
                    Location = new Point(100, 31),
                    Size = new Size(76, 28)
                };
                f.Controls.Add(okBtn);

                var hint = new Label
                {
                    Text = Lang.T("формат ЧЧ:ММ, прошло — завтра", "HH:MM; past time = tomorrow"),
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 7.5F),
                    Location = new Point(12, 70),
                    AutoSize = true
                };
                f.Controls.Add(hint);

                Action confirm = () => {
                    DateTime t;
                    if (DateTime.TryParseExact(box.Text.Trim(), new[] { "HH:mm", "H:mm", "HH.mm", "H.mm" },
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    {
                        DateTime when = DateTime.Today.AddHours(t.Hour).AddMinutes(t.Minute);
                        if (when <= DateTime.Now) when = when.AddDays(1);
                        result = when;
                        f.Close();
                    }
                    else
                    {
                        box.BackColor = Color.FromArgb(70, 35, 40);
                    }
                };
                okBtn.Click += (s, e) => confirm();
                f.KeyPreview = true;
                f.KeyDown += (s, e) => {
                    if (e.KeyCode == Keys.Enter) confirm();
                    if (e.KeyCode == Keys.Escape) f.Close();
                };

                f.ShowDialog(owner);
            }

            return result;
        }
    }
}
