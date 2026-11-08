using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeepTools
{
    // Авто-очистка standby-памяти (по мотивам ISLC). Windows держит в «резервном»
    // списке (standby) закэшированные страницы; когда свободной RAM мало, это
    // вызывает микрофризы в тяжёлых играх. Сбрасываем standby-лист через
    // NtSetSystemInformation, когда свободной памяти становится мало.
    public static class StandbyCleaner
    {
        // SYSTEM_MEMORY_LIST_COMMAND
        private const int MemoryPurgeStandbyList = 4;
        private const int SystemMemoryListInformation = 80;

        [DllImport("ntdll.dll")]
        private static extern uint NtSetSystemInformation(int infoClass, IntPtr info, int length);

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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buf);

        // Сброс standby-листа требует включённой привилегии SeProfileSingleProcessPrivilege.
        // Она есть у админского токена, но по умолчанию выключена - включаем один раз.
        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint LowPart; public int HighPart; }
        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID Luid; public uint Attributes; }

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string host, string name, out LUID luid);
        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
            ref TOKEN_PRIVILEGES newState, uint len, IntPtr prev, IntPtr relen);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        private const uint SE_PRIVILEGE_ENABLED = 0x2;
        private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
        private const uint TOKEN_QUERY = 0x8;
        private static bool _privEnabled = false;

        private static void EnablePrivilege()
        {
            if (_privEnabled) return;
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) return;
            try
            {
                LUID luid;
                if (!LookupPrivilegeValue(null, "SeProfileSingleProcessPrivilege", out luid)) return;
                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                _privEnabled = true;
            }
            catch { }
            finally { CloseHandle(token); }
        }

        private static bool _enabled = false;
        private static Thread _thread;
        private static volatile bool _running = false;
        private static DateTime _lastPurge = DateTime.MinValue;

        // Сколько МБ освободила последняя очистка (для статуса в UI)
        public static long LastFreedMb { get; private set; }
        public static bool IsActive { get { return _enabled; } }

        public static bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (value) Start();
                else _running = false;
            }
        }

        private static void Start()
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "StandbyCleaner" };
            _thread.Start();
        }

        private static void Loop()
        {
            while (_running)
            {
                try
                {
                    if (NeedsPurge() && (DateTime.Now - _lastPurge).TotalSeconds > 30)
                        Purge();
                }
                catch { }
                Thread.Sleep(5000);
            }
        }

        // Пора чистить, если свободно меньше 1 ГБ или загрузка памяти выше 85%
        private static bool NeedsPurge()
        {
            var s = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(s)) return false;
            bool lowFree = s.ullAvailPhys < 1024UL * 1024 * 1024;
            bool highLoad = s.dwMemoryLoad >= 85;
            return lowFree || highLoad;
        }

        // Ручной/автоматический сброс standby-листа. Возвращает освобождённые МБ.
        // Требует прав администратора (у программы они есть).
        public static long Purge()
        {
            ulong before = AvailBytes();
            EnablePrivilege();
            IntPtr buf = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                Marshal.WriteInt32(buf, MemoryPurgeStandbyList);
                NtSetSystemInformation(SystemMemoryListInformation, buf, sizeof(int));
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }

            Thread.Sleep(300); // даём системе перераспределить память
            ulong after = AvailBytes();
            _lastPurge = DateTime.Now;
            LastFreedMb = (long)((after > before ? after - before : 0) / (1024 * 1024));
            return LastFreedMb;
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
