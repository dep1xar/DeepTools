using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace DeepTools
{
    public class ClipboardEntry
    {
        public string Id { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsPinned { get; set; }
        public string Type { get; set; } // "text", "image", "link"

        public string DisplayText
        {
            get
            {
                if (Type == "image") return Lang.T("[Изображение]", "[Image]");
                if (Content == null) return "";
                string flat = Content.Replace("\r", "").Replace("\n", " ").Replace("\t", " ");
                if (flat.Length > 80) flat = flat.Substring(0, 80) + "...";
                return flat;
            }
        }
    }

    public class ClipboardManager
    {
        private List<ClipboardEntry> history = new List<ClipboardEntry>();
        private string lastClipboard = "";
        private Timer monitoringTimer;
        private int maxHistorySize = 100;

        // Слушатель WM_CLIPBOARDUPDATE: обновление приходит сразу, а не по таймеру
        private ClipboardWatcher watcher;

        public event EventHandler HistoryChanged;

        public ClipboardManager()
        {
            monitoringTimer = new Timer { Interval = 1000 };
            monitoringTimer.Tick += (s, e) => MonitorClipboard();
            LoadHistory();
        }

        public void Start()
        {
            // Основной путь - мгновенное уведомление от Windows
            try
            {
                watcher = new ClipboardWatcher(this);
                watcher.Install();
            }
            catch { watcher = null; }

            // Таймер оставляем как страховку: если слушатель не установился
            // (или сообщение потерялось), история всё равно подхватит копирование
            monitoringTimer.Start();
        }

        public void Stop()
        {
            monitoringTimer.Stop();
            if (watcher != null)
            {
                watcher.Uninstall();
                watcher = null;
            }
        }

        // Дёргается из окна-слушателя на UI-потоке
        internal void OnClipboardChanged()
        {
            MonitorClipboard();
        }

        private void MonitorClipboard()
        {
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null) return;

                // Кириллица: CF_TEXT (DataFormats.Text) - это ANSI. Если брать его,
                // русские буквы вне системной кодовой страницы превращаются в "?".
                // Сначала пробуем UnicodeText, ANSI - только как резерв
                if (data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string text = data.GetData(DataFormats.UnicodeText) as string;
                    if (!string.IsNullOrEmpty(text) && text != lastClipboard)
                    {
                        lastClipboard = text;
                        AddEntry(text, "text");
                    }
                    return;
                }

                if (data.GetDataPresent(DataFormats.Text))
                {
                    string text = data.GetData(DataFormats.Text) as string;
                    if (!string.IsNullOrEmpty(text) && text != lastClipboard)
                    {
                        lastClipboard = text;
                        AddEntry(text, "text");
                    }
                    return;
                }

                // Изображение (скриншот, копирование картинки) - сохраняем в файл,
                // в истории храним путь к нему
                if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    using (System.Drawing.Image img = Clipboard.GetImage())
                    {
                        if (img == null) return;
                        string hash = ImageHash(img);
                        if (hash == lastClipboard) return;
                        lastClipboard = hash;

                        string path = SaveImageFile(img);
                        if (path != null) AddEntry(path, "image");
                    }
                }
            }
            catch { }
        }

        private static string ImageDir
        {
            get { return Path.Combine(Path.GetDirectoryName(FilePath), "clipboard_img"); }
        }

        private static string ImageHash(System.Drawing.Image img)
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    img.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    byte[] bytes = ms.ToArray();
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                        return "img:" + BitConverter.ToString(md5.ComputeHash(bytes));
                }
            }
            catch { return "img:" + img.Width + "x" + img.Height; }
        }

        private static string SaveImageFile(System.Drawing.Image img)
        {
            try
            {
                if (!Directory.Exists(ImageDir)) Directory.CreateDirectory(ImageDir);
                string name = "img_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".png";
                string path = Path.Combine(ImageDir, name);
                img.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return path;
            }
            catch { return null; }
        }

        public void AddEntry(string content, string type = "text")
        {
            var entry = new ClipboardEntry
            {
                Id = Guid.NewGuid().ToString().Substring(0, 8),
                Content = content,
                Timestamp = DateTime.Now,
                IsPinned = false,
                Type = type
            };

            history.Insert(0, entry);

            if (history.Count > maxHistorySize)
            {
                var old = history[history.Count - 1];
                if (old.Type == "image") TryDeleteImage(old.Content); // не оставляем файл-сироту
                history.RemoveAt(history.Count - 1);
            }

            SaveHistory();
            if (HistoryChanged != null)
                HistoryChanged(this, EventArgs.Empty);
        }

        public List<ClipboardEntry> GetHistory()
        {
            var pinned = history.Where(e => e.IsPinned).ToList();
            var unpinned = history.Where(e => !e.IsPinned).ToList();
            return pinned.Concat(unpinned).ToList();
        }

        public List<ClipboardEntry> Search(string query)
        {
            if (string.IsNullOrEmpty(query)) return GetHistory();
            // OrdinalIgnoreCase - корректно работает и с кириллицей, без ToLower()
            return history
                .Where(e => e.Content != null && e.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        public void PinEntry(string id)
        {
            var entry = history.FirstOrDefault(e => e.Id == id);
            if (entry != null)
            {
                entry.IsPinned = !entry.IsPinned;
                SaveHistory();
                if (HistoryChanged != null)
                    HistoryChanged(this, EventArgs.Empty);
            }
        }

        public void RemoveEntry(string id)
        {
            var entry = history.FirstOrDefault(e => e.Id == id);
            if (entry != null && entry.Type == "image") TryDeleteImage(entry.Content);
            history.RemoveAll(e => e.Id == id);
            SaveHistory();
            if (HistoryChanged != null)
                HistoryChanged(this, EventArgs.Empty);
        }

        public void ClearHistory()
        {
            foreach (var e in history)
                if (e.Type == "image") TryDeleteImage(e.Content);
            history.Clear();
            SaveHistory();
            if (HistoryChanged != null)
                HistoryChanged(this, EventArgs.Empty);
        }

        private static void TryDeleteImage(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        public void RestoreEntry(string id)
        {
            var entry = history.FirstOrDefault(e => e.Id == id);
            if (entry == null) return;

            if (entry.Type == "image")
            {
                try
                {
                    if (File.Exists(entry.Content))
                        using (var tmp = System.Drawing.Image.FromFile(entry.Content))
                        using (var bmp = new System.Drawing.Bitmap(tmp))
                            Clipboard.SetImage(bmp);
                }
                catch { }
            }
            else if (!string.IsNullOrEmpty(entry.Content))
            {
                // UnicodeText, чтобы кириллица не превращалась в "?" при вставке
                Clipboard.SetText(entry.Content, TextDataFormat.UnicodeText);
            }

            // Чтобы слушатель буфера не добавил восстановленное как новую запись
            if (entry.Type != "image") lastClipboard = entry.Content;
        }

        // Слушатель WM_CLIPBOARDUPDATE. Обычный опрос раз в секунду давал
        // заметную задержку: скопировал - а в истории появилось через раз.
        private class ClipboardWatcher : NativeWindow
        {
            private const int WM_CLIPBOARDUPDATE = 0x031D;
            private readonly ClipboardManager owner;
            private bool installed;

            public ClipboardWatcher(ClipboardManager owner) { this.owner = owner; }

            public void Install()
            {
                CreateHandle(new CreateParams());
                if (AddClipboardFormatListener(Handle)) installed = true;
            }

            public void Uninstall()
            {
                try { if (installed) RemoveClipboardFormatListener(Handle); } catch { }
                installed = false;
                try { DestroyHandle(); } catch { }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLIPBOARDUPDATE && owner != null)
                {
                    try { owner.OnClipboardChanged(); } catch { }
                    return;
                }
                base.WndProc(ref m);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        // Файл истории в AppData. Строка: id|pinned|type|ticks|base64(content).
        // Контент кодируем в base64 - в буфер попадает многострочный текст с любыми
        // символами, а base64 гарантированно без переносов и разделителей "|"
        private static string FilePath
        {
            get
            {
                return Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools"),
                    "clipboard.dat");
            }
        }

        private void SaveHistory()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var lines = new List<string>();
                foreach (ClipboardEntry e in history)
                {
                    if (e.Content == null) continue;
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Content));
                    lines.Add(e.Id + "|" + (e.IsPinned ? "1" : "0") + "|" + (e.Type ?? "text")
                        + "|" + e.Timestamp.Ticks + "|" + b64);
                }
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch
            {
                // не смогли сохранить - не критично, история просто не переживёт перезапуск
            }
        }

        private void LoadHistory()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                string[] lines = File.ReadAllLines(FilePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    // 5 частей, но base64 сам "|" не содержит, поэтому split с лимитом безопасен
                    string[] parts = lines[i].Split(new[] { '|' }, 5);
                    if (parts.Length < 5) continue;

                    long ticks;
                    if (!long.TryParse(parts[3], out ticks)) continue;

                    string content;
                    try { content = Encoding.UTF8.GetString(Convert.FromBase64String(parts[4])); }
                    catch { continue; }

                    history.Add(new ClipboardEntry
                    {
                        Id = parts[0],
                        IsPinned = parts[1] == "1",
                        Type = parts[2],
                        Timestamp = new DateTime(ticks),
                        Content = content
                    });
                }

                // Чтобы монитор не добавил дубликатом первый же скопированный ранее текст
                var first = history.FirstOrDefault(e => e.Type == "text");
                if (first != null) lastClipboard = first.Content;
            }
            catch
            {
                // повреждённый файл - просто стартуем с пустой историей
            }
        }
    }
}