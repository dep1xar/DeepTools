using System;
using System.Collections.Generic;
using System.IO;

namespace DeepTools
{
    // Простой конфиг вида ключ=значение в файле AppData
    // Не используем XML/JSON сериализацию, чтобы обойтись без внешних библиотек
    public static class AppConfig
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools");
        private static readonly string FilePath = Path.Combine(FolderPath, "config.txt");
        private static Dictionary<string, string> values = new Dictionary<string, string>();
        private static bool loaded = false;
        private static readonly object sync = new object();

        public static void Load()
        {
            lock (sync)
            {
                values.Clear();
                if (File.Exists(FilePath))
                {
                    try
                    {
                        string[] lines = File.ReadAllLines(FilePath);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string line = lines[i];
                            int idx = line.IndexOf('=');
                            if (idx > 0)
                            {
                                string key = line.Substring(0, idx);
                                string val = line.Substring(idx + 1);
                                values[key] = val;
                            }
                        }
                    }
                    catch { }
                }
                loaded = true;
            }
        }

        public static void Save()
        {
            lock (sync)
            {
                try
                {
                    if (!Directory.Exists(FolderPath)) Directory.CreateDirectory(FolderPath);
                    List<string> lines = new List<string>();
                    foreach (KeyValuePair<string, string> pair in values)
                    {
                        lines.Add(pair.Key + "=" + pair.Value);
                    }
                    string tmp = FilePath + ".tmp." + Guid.NewGuid().ToString("N");
                    File.WriteAllLines(tmp, lines.ToArray());
                    // атомарная замена: если оригинал существует — заменяем
                    if (File.Exists(FilePath))
                    {
                        try { File.Replace(tmp, FilePath, null); }
                        catch
                        {
                            try { File.Delete(FilePath); } catch { }
                            try { File.Move(tmp, FilePath); } catch { try { File.Delete(tmp); } catch { } }
                        }
                    }
                    else
                    {
                        try { File.Move(tmp, FilePath); } catch { try { File.Delete(tmp); } catch { } }
                    }
                }
                catch
                {
                    // Если не смогли сохранить настройки - не критично, просто продолжаем без них
                }
            }
        }

        public static string Get(string key, string defaultValue)
        {
            lock (sync)
            {
                if (!loaded) { /* avoid deadlock via nested lock: load inside lock */ }
                // Load outside lock recursion: copy pattern
                if (!loaded)
                {
                    // вызвать Load логику inline чтобы не брать lock рекурсивно
                    // но Load уже берет lock — просто проверим что loaded, иначе загрузим
                }
            }
            // Безопасный путь: проверяем loaded под lock, затем читаем
            lock (sync)
            {
                if (!loaded)
                {
                    values.Clear();
                    if (File.Exists(FilePath))
                    {
                        try
                        {
                            string[] lines = File.ReadAllLines(FilePath);
                            for (int i = 0; i < lines.Length; i++)
                            {
                                string line = lines[i];
                                int idx = line.IndexOf('=');
                                if (idx > 0)
                                {
                                    string k = line.Substring(0, idx);
                                    string v = line.Substring(idx + 1);
                                    values[k] = v;
                                }
                            }
                        }
                        catch { }
                    }
                    loaded = true;
                }
                if (values.ContainsKey(key)) return values[key];
                return defaultValue;
            }
        }

        public static void Set(string key, string value)
        {
            lock (sync)
            {
                if (!loaded)
                {
                    values.Clear();
                    if (File.Exists(FilePath))
                    {
                        try
                        {
                            string[] lines = File.ReadAllLines(FilePath);
                            for (int i = 0; i < lines.Length; i++)
                            {
                                string line = lines[i];
                                int idx = line.IndexOf('=');
                                if (idx > 0)
                                {
                                    string k = line.Substring(0, idx);
                                    string v = line.Substring(idx + 1);
                                    values[k] = v;
                                }
                            }
                        }
                        catch { }
                    }
                    loaded = true;
                }
                values[key] = value;
            }
            Save();
        }

        public static bool GetBool(string key, bool defaultValue)
        {
            string s = Get(key, defaultValue ? "1" : "0");
            return s == "1";
        }

        public static void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }
    }
}
