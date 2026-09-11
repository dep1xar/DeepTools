using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // «Корзина с откатом»: наша очистка сначала копирует файлы из Корзины в бэкап
    // DeepTools (с исходным путём), потом чистит системную Корзину. Из бэкапа можно
    // восстановить файл несколько дней. Читаем Корзину через Shell COM (рефлексия,
    // без dynamic). Индекс бэкапа - строки "id|исходный_путь|isDir|ticks".
    public static class RecycleGuard
    {
        public const int RetentionDays = 7;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);
        private const uint SHERB_NOCONFIRMATION = 1, SHERB_NOPROGRESSUI = 2, SHERB_NOSOUND = 4;

        private static object Inv(object o, string name, params object[] a)
        { return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, a); }
        private static object Get(object o, string name)
        { return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null); }

        public class Item { public string Name; public string BinPath; public string OrigFolder; public bool IsDir; }

        // Текущее содержимое Корзины (имя, путь к данным в $Recycle.Bin, исходная папка)
        public static List<Item> ReadBin()
        {
            var result = new List<Item>();
            try
            {
                Type t = Type.GetTypeFromProgID("Shell.Application");
                if (t == null) return result;
                object app = Activator.CreateInstance(t);
                object ns = Inv(app, "NameSpace", 10); // 10 = Корзина

                // Ищем колонку «Исходное расположение» (заголовок зависит от локали)
                int col = 1;
                for (int i = 0; i < 40; i++)
                {
                    string h = Inv(ns, "GetDetailsOf", null, i) as string;
                    if (!string.IsNullOrEmpty(h) &&
                        (h.IndexOf("распол", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         h.IndexOf("Location", StringComparison.OrdinalIgnoreCase) >= 0)) { col = i; break; }
                }

                object items = Inv(ns, "Items");
                int cnt = Convert.ToInt32(Get(items, "Count"));
                for (int i = 0; i < cnt; i++)
                {
                    try
                    {
                        object it = Inv(items, "Item", i);
                        string binPath = Get(it, "Path") as string;
                        if (string.IsNullOrEmpty(binPath)) continue;
                        result.Add(new Item
                        {
                            Name = Get(it, "Name") as string,
                            BinPath = binPath,
                            OrigFolder = Inv(ns, "GetDetailsOf", it, col) as string,
                            IsDir = Directory.Exists(binPath)
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        // PLACEHOLDER_REST
        public class Backup { public string FileId; public string OrigPath; public string Name; public bool IsDir; public DateTime When; }

        private static string BackupRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DeepTools\\recycle_backup");
            }
        }
        private static string IndexPath { get { return Path.Combine(BackupRoot, "index.txt"); } }

        // Скопировать содержимое Корзины в бэкап, затем очистить системную Корзину.
        // Возвращает, сколько элементов удалось сохранить.
        public static int SafeEmpty()
        {
            List<Item> items = ReadBin();
            int saved = 0;
            try { Directory.CreateDirectory(BackupRoot); } catch { }

            var lines = new List<string>();
            foreach (Item it in items)
            {
                try
                {
                    string id = Guid.NewGuid().ToString("N") + (it.IsDir ? "" : Path.GetExtension(it.Name));
                    string dest = Path.Combine(BackupRoot, id);
                    if (it.IsDir) CopyDir(it.BinPath, dest);
                    else File.Copy(it.BinPath, dest, true);

                    string orig = string.IsNullOrEmpty(it.OrigFolder) ? "" : Path.Combine(it.OrigFolder, it.Name);
                    lines.Add(id + "|" + orig + "|" + (it.IsDir ? 1 : 0) + "|" + it.Name + "|" + DateTime.Now.Ticks);
                    saved++;
                }
                catch { }
            }
            try { if (lines.Count > 0) File.AppendAllText(IndexPath, string.Join(Environment.NewLine, lines.ToArray()) + Environment.NewLine); }
            catch { }

            EmptyBin();
            PurgeOld(RetentionDays);
            return saved;
        }

        public static void EmptyBin()
        {
            try { SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND); }
            catch { }
        }

        public static List<Backup> ListBackups()
        {
            var list = new List<Backup>();
            try
            {
                if (!File.Exists(IndexPath)) return list;
                foreach (string line in File.ReadAllLines(IndexPath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 5) continue;
                    string full = Path.Combine(BackupRoot, p[0]);
                    if (!File.Exists(full) && !Directory.Exists(full)) continue;
                    long ticks; long.TryParse(p[4], out ticks);
                    list.Add(new Backup { FileId = p[0], OrigPath = p[1], IsDir = p[2] == "1", Name = p[3], When = new DateTime(ticks) });
                }
            }
            catch { }
            list.Reverse(); // новее сверху
            return list;
        }

        // Восстановить: в исходный путь, а если его нет - на Рабочий стол в «DeepTools Recovered»
        public static string Restore(Backup b)
        {
            try
            {
                string src = Path.Combine(BackupRoot, b.FileId);
                string target = !string.IsNullOrEmpty(b.OrigPath) ? b.OrigPath
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DeepTools Recovered", b.Name);

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target) || Directory.Exists(target))
                {
                    string dir = Path.GetDirectoryName(target);
                    string nm = Path.GetFileNameWithoutExtension(target), ex = Path.GetExtension(target);
                    target = Path.Combine(dir, nm + " (восстановлен)" + ex);
                }
                if (b.IsDir) CopyDir(src, target); else File.Copy(src, target, false);
                return target;
            }
            catch { return null; }
        }

        public static void PurgeOld(int days)
        {
            try
            {
                if (!File.Exists(IndexPath)) return;
                DateTime cutoff = DateTime.Now.AddDays(-days);
                var keep = new List<string>();
                foreach (string line in File.ReadAllLines(IndexPath))
                {
                    string[] p = line.Split('|');
                    if (p.Length < 5) continue;
                    long ticks; long.TryParse(p[4], out ticks);
                    string full = Path.Combine(BackupRoot, p[0]);
                    if (new DateTime(ticks) < cutoff)
                    {
                        try { if (Directory.Exists(full)) Directory.Delete(full, true); else if (File.Exists(full)) File.Delete(full); }
                        catch { }
                    }
                    else keep.Add(line);
                }
                File.WriteAllText(IndexPath, string.Join(Environment.NewLine, keep.ToArray()) + (keep.Count > 0 ? Environment.NewLine : ""));
            }
            catch { }
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
