using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // Настоящий FPS игры через ETW: слушаем события Present рантайма DXGI
    // (провайдер Microsoft-Windows-DXGI, тот же источник, что у PresentMon)
    // плюс Microsoft-Windows-D3D9 для старых игр, и считаем кадры процесса
    // активного окна. DwmFlush в OverlayForm остаётся запасным вариантом -
    // он меряет композитор, а не игру, и упирается в частоту монитора.
    // Для ETW-сессии нужны права администратора - они у программы есть
    // (UAC при запуске).
    public static class PresentTracer
    {
        private const string SessionName = "DeepToolsPresentTrace";
        // Microsoft-Windows-DXGI: событие 42 = Present_Start (каждый кадр DXGI-приложения)
        private static readonly Guid DxgiGuid = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private const ushort DxgiPresentId = 42;
        // Microsoft-Windows-D3D9: событие 1 = Present_Start (старые D3D9-игры)
        private static readonly Guid D3D9Guid = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        private const ushort D3D9PresentId = 1;

        // ---- P/Invoke ETW ----

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StartTraceW(out ulong sessionHandle, string sessionName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ControlTraceW(ulong sessionHandle, string sessionName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(ulong sessionHandle, ref Guid providerId, uint controlCode,
            byte level, ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern ulong OpenTraceW(ref EVENT_TRACE_LOGFILEW logfile);

        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(ulong traceHandle);

        private delegate uint BufferCb(IntPtr logfile);
        private delegate void EventRecordCb(IntPtr record);

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public byte Type;
            public byte Level;
            public ushort Version;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public uint KernelTime;
            public uint UserTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 172)]
            public byte[] TimeZone; // TIME_ZONE_INFORMATION
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EVENT_TRACE_LOGFILEW
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string LogFileName;
            [MarshalAs(UnmanagedType.LPWStr)] public string LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public BufferCb BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public EventRecordCb EventCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        // ---- Состояние ----

        private static ulong sessionHandle;
        private static ulong traceHandle = unchecked((ulong)~0L);
        private static System.Threading.Thread traceThread;
        private static volatile bool running;
        private static readonly object sync = new object();

        // QPC-время каждого Present по PID (держим последние ~4 секунды)
        private static readonly Dictionary<int, List<long>> presents = new Dictionary<int, List<long>>();

        // Ссылки на делегаты держим в полях, иначе GC соберёт их под нативным колбэком
        private static EventRecordCb recordCb;
        private static BufferCb bufferCb;
        private static long dxgiLo, dxgiHi, d3d9Lo, d3d9Hi;
        private static int cleanupCounter;

        public static bool Running { get { return running; } }

        // Трейсером пользуются и оверлей, и детект игр в GameBooster - считаем
        // потребителей, чтобы Stop одного не выключил трейсер под другим
        private static int refCount;
        private static bool exitHooked;

        public static void Start()
        {
            refCount++;
            if (running) return;
            if (!exitHooked)
            {
                exitHooked = true;
                // ETW-сессия - объект ядра и переживает процесс: гасим её при
                // любом выходе, иначе останется висеть в системе
                AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { StopSession(); } catch { } };
            }
            try
            {
                byte[] gb = DxgiGuid.ToByteArray();
                dxgiLo = BitConverter.ToInt64(gb, 0);
                dxgiHi = BitConverter.ToInt64(gb, 8);
                gb = D3D9Guid.ToByteArray();
                d3d9Lo = BitConverter.ToInt64(gb, 0);
                d3d9Hi = BitConverter.ToInt64(gb, 8);

                // Хвост от прошлого запуска (если процесс убили) - иначе StartTrace
                // вернёт ERROR_ALREADY_EXISTS
                StopSession();

                IntPtr props = AllocProps();
                try
                {
                    if (StartTraceW(out sessionHandle, SessionName, props) != 0) return; // нет прав и т.п.
                }
                finally { Marshal.FreeHGlobal(props); }

                Guid g = DxgiGuid;
                EnableTraceEx2(sessionHandle, ref g, 1 /*ENABLE_PROVIDER*/, 4 /*INFORMATIONAL*/,
                    0, 0, 0, IntPtr.Zero);
                g = D3D9Guid;
                EnableTraceEx2(sessionHandle, ref g, 1, 4, 0, 0, 0, IntPtr.Zero);

                recordCb = OnEventRecord;
                bufferCb = OnBuffer;
                var logFile = new EVENT_TRACE_LOGFILEW();
                logFile.LoggerName = SessionName;
                logFile.ProcessTraceMode = 0x100 | 0x10000000; // REAL_TIME | EVENT_RECORD
                logFile.EventCallback = recordCb;
                logFile.BufferCallback = bufferCb;
                traceHandle = OpenTraceW(ref logFile);
                if (traceHandle == unchecked((ulong)~0L)) { StopSession(); return; }

                running = true;
                traceThread = new System.Threading.Thread(() => {
                    // Блокируется до остановки сессии, события сыплются в OnEventRecord
                    try { ProcessTrace(new ulong[] { traceHandle }, 1, IntPtr.Zero, IntPtr.Zero); }
                    catch { }
                });
                traceThread.IsBackground = true;
                traceThread.Start();
            }
            catch { running = false; }
        }

        public static void Stop()
        {
            if (refCount > 0) refCount--;
            if (refCount > 0) return; // трейсер ещё нужен другому потребителю
            running = false;
            StopSession();
            if (traceHandle != unchecked((ulong)~0L))
            {
                try { CloseTrace(traceHandle); } catch { }
                traceHandle = unchecked((ulong)~0L);
            }
            lock (sync) presents.Clear();
        }

        // EVENT_TRACE_PROPERTIES + место под имя сессии. Смещения - для x64
        private const int PropsLen = 120 + 1024;

        private static IntPtr AllocProps()
        {
            IntPtr p = Marshal.AllocHGlobal(PropsLen);
            for (int i = 0; i < PropsLen; i += 4) Marshal.WriteInt32(p, i, 0);
            Marshal.WriteInt32(p, 0, PropsLen);       // Wnode.BufferSize
            Marshal.WriteInt32(p, 40, 1);             // Wnode.ClientContext = 1 (QPC - те же тики, что Stopwatch)
            Marshal.WriteInt32(p, 44, 0x00020000);    // Wnode.Flags = WNODE_FLAG_TRACED_GUID
            Marshal.WriteInt32(p, 48, 64);            // BufferSize, КБ
            Marshal.WriteInt32(p, 52, 8);             // MinimumBuffers
            Marshal.WriteInt32(p, 56, 64);            // MaximumBuffers
            Marshal.WriteInt32(p, 64, 0x100);         // LogFileMode = EVENT_TRACE_REAL_TIME_MODE
            Marshal.WriteInt32(p, 68, 1);             // FlushTimer, сек
            Marshal.WriteInt32(p, 116, 120);          // LoggerNameOffset
            return p;
        }

        private static void StopSession()
        {
            IntPtr p = AllocProps();
            try { ControlTraceW(0, SessionName, p, 1 /*EVENT_TRACE_CONTROL_STOP*/); }
            catch { }
            finally { Marshal.FreeHGlobal(p); }
        }

        // Колбэк на каждое ETW-событие. Работает на потоке ProcessTrace,
        // поэтому максимально дёшево: пара сравнений и запись в список
        private static void OnEventRecord(IntPtr rec)
        {
            try
            {
                // EVENT_HEADER: ProcessId по смещению 12, TimeStamp 16, ProviderId 24, EventDescriptor.Id 40
                ushort id = (ushort)Marshal.ReadInt16(rec, 40);
                long pLo = Marshal.ReadInt64(rec, 24);
                long pHi = Marshal.ReadInt64(rec, 32);
                bool isPresent =
                    (id == DxgiPresentId && pLo == dxgiLo && pHi == dxgiHi) ||
                    (id == D3D9PresentId && pLo == d3d9Lo && pHi == d3d9Hi);
                if (!isPresent) return;
                int pid = Marshal.ReadInt32(rec, 12);
                long ts = Marshal.ReadInt64(rec, 16);
                lock (sync)
                {
                    List<long> list;
                    if (!presents.TryGetValue(pid, out list))
                    {
                        if (presents.Count > 64) return; // защита от разрастания
                        list = new List<long>(256);
                        presents[pid] = list;
                    }
                    list.Add(ts);
                }
            }
            catch { }
        }

        private static uint OnBuffer(IntPtr logfile)
        {
            return running ? 1u : 0u; // 0 останавливает ProcessTrace
        }

        public class Snap
        {
            public int Fps;
            public float AvgMs;
            public int LowFps;
            public float[] FrameMs;
        }

        // FPS процесса по его Present-событиям, или null если процесс не рисует
        // (тогда OverlayForm остаётся на DwmFlush-фолбэке)
        public static Snap SnapshotForPid(int pid)
        {
            if (!running || pid <= 0) return null;
            long freq = Stopwatch.Frequency;
            long now = Stopwatch.GetTimestamp();
            long[] copy;
            lock (sync)
            {
                CleanupLocked(now, freq);
                List<long> list;
                if (!presents.TryGetValue(pid, out list) || list.Count < 10) return null;
                long cutoff = now - freq * 4;
                int firstKeep = 0;
                while (firstKeep < list.Count && list[firstKeep] < cutoff) firstKeep++;
                if (firstKeep > 0) list.RemoveRange(0, firstKeep);
                if (list.Count < 10) return null;
                copy = list.ToArray();
            }

            // FPS за последнюю секунду ДАННЫХ (окно от новейшего события назад):
            // ETW доставляет события пачками с задержкой до секунды, окно от
            // текущего времени занижало бы счёт
            long newest = copy[copy.Length - 1];
            long winStart = newest - freq;
            int frames = 0;
            for (int i = copy.Length - 1; i >= 0 && copy[i] >= winStart; i--) frames++;
            if (frames < 5) return null; // почти не рисует - не 3D-приложение

            var snap = new Snap();
            snap.Fps = frames;
            snap.AvgMs = 1000f / frames;

            // Фреймтаймы из пар соседних Present - для 1% low и графика
            var fts = new float[copy.Length - 1];
            for (int i = 1; i < copy.Length; i++)
                fts[i - 1] = (copy[i] - copy[i - 1]) * 1000f / freq;
            snap.FrameMs = fts;

            var sorted = (float[])fts.Clone();
            Array.Sort(sorted);
            int idx = (int)(sorted.Length * 0.99f);
            if (idx >= sorted.Length) idx = sorted.Length - 1;
            float worst = sorted[idx];
            snap.LowFps = worst > 0 ? (int)(1000f / worst) : 0;
            return snap;
        }

        // Рисует ли процесс кадры прямо сейчас: было ли достаточно Present-событий
        // за последнюю секунду. Используется детектом игр для borderless-режима,
        // где геометрия окна не совпадает с экраном пиксель в пиксель
        public static bool IsRendering(int pid, int minFps)
        {
            Snap snap = SnapshotForPid(pid);
            return snap != null && snap.Fps >= minFps;
        }

        // Выкидываем процессы, которые давно не рисовали (закрытые игры)
        private static void CleanupLocked(long now, long freq)
        {
            if (++cleanupCounter % 10 != 0) return;
            long cutoff = now - freq * 8;
            List<int> dead = null;
            foreach (KeyValuePair<int, List<long>> kv in presents)
            {
                if (kv.Value.Count == 0 || kv.Value[kv.Value.Count - 1] < cutoff)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(kv.Key);
                }
            }
            if (dead != null)
                for (int i = 0; i < dead.Count; i++) presents.Remove(dead[i]);
        }
    }
}
