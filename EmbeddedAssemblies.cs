using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace DeepTools
{
    // Managed-зависимости (LibreHardwareMonitorLib и её спутники) встроены в exe
    // как ресурсы через build.ps1 (/resource:...). Когда CLR не находит сборку рядом
    // с exe, этот обработчик достаёт её из ресурсов и грузит прямо из памяти -
    // так рядом с exe не нужно раскладывать десяток .dll и релиз - один файл.
    public static class EmbeddedAssemblies
    {
        private static readonly object sync = new object();
        private static readonly Dictionary<string, Assembly> cache =
            new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static bool installed = false;

        public static void Install()
        {
            if (installed) return;
            installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            // Имя вида "LibreHardwareMonitorLib, Version=..., Culture=..." - берём простое имя
            string shortName = new AssemblyName(args.Name).Name;
            string resourceName = "DeepTools.Embedded." + shortName + ".dll";

            lock (sync)
            {
                Assembly cached;
                if (cache.TryGetValue(resourceName, out cached)) return cached;

                // Ресурсы лежат сжатыми (<имя>.dll.gz, см. build.ps1); несжатый вариант
                // поддерживаем как запасной - на случай сборки старым скриптом
                byte[] data = ReadResource(resourceName + ".gz", true);
                if (data == null) data = ReadResource(resourceName, false);
                if (data == null)
                {
                    // Сборки с таким именем среди ресурсов нет - пусть CLR ищет дальше сам
                    cache[resourceName] = null;
                    return null;
                }

                Assembly loaded = Assembly.Load(data);
                cache[resourceName] = loaded;
                return loaded;
            }
        }

        private static byte[] ReadResource(string name, bool gzipped)
        {
            Assembly self = Assembly.GetExecutingAssembly();
            using (Stream stream = self.GetManifestResourceStream(name))
            {
                if (stream == null) return null;
                using (var ms = new MemoryStream())
                {
                    if (gzipped)
                    {
                        using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                            gz.CopyTo(ms);
                    }
                    else
                    {
                        stream.CopyTo(ms);
                    }
                    return ms.ToArray();
                }
            }
        }
    }
}
