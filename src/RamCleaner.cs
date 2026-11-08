using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // Освобождение RAM: сбрасываем рабочие наборы (working set) процессов через
    // EmptyWorkingSet - неиспользуемые страницы уходят в standby/на диск, физическая
    // память освобождается. Возвращает, сколько МБ освободилось (по замеру до/после)
    public static class RamCleaner
    {
        [DllImport("psapi.dll")]
        private static extern int EmptyWorkingSet(IntPtr hProcess);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        // Возвращает освобождённые мегабайты (может быть 0, если освобождать нечего)
        public static long Clean()
        {
            ulong before = AvailBytes();

            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return 0; }

            for (int i = 0; i < procs.Length; i++)
            {
                try { EmptyWorkingSet(procs[i].Handle); }
                catch { /* системный процесс без доступа - пропускаем */ }
                finally { try { procs[i].Dispose(); } catch { } }
            }

            System.Threading.Thread.Sleep(400); // даём системе перераспределить память
            ulong after = AvailBytes();

            long freed = (long)((after > before ? after - before : 0) / (1024 * 1024));
            return freed;
        }

        private static ulong AvailBytes()
        {
            try
            {
                var s = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(s)) return s.ullAvailPhys;
            }
            catch { }
            return 0;
        }
    }
}
