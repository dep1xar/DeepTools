using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // Заморозка фоновых процессов на время игры: NtSuspendProcess останавливает все
    // потоки процесса - он перестаёт есть CPU и таймеры, но ничего не теряет
    // (в отличие от Kill). После разморозки продолжает работать как ни в чём не бывало.
    // При выходе из DeepTools обязательно размораживаем всё (MainForm.FormClosed),
    // иначе процессы останутся висеть замороженными навсегда.
    public static class BackgroundFreezer
    {
        [DllImport("ntdll.dll")]
        private static extern int NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll")]
        private static extern int NtResumeProcess(IntPtr processHandle);

        // Держим сами Process-объекты: их Handle нужен для разморозки
        private static readonly List<Process> frozen = new List<Process>();

        public static int FrozenCount
        {
            get { lock (frozen) return frozen.Count; }
        }

        public static bool IsFrozen(int pid)
        {
            lock (frozen)
            {
                for (int i = 0; i < frozen.Count; i++)
                {
                    try { if (frozen[i].Id == pid) return true; }
                    catch { }
                }
            }
            return false;
        }

        // Замораживает список процессов, возвращает сколько реально удалось
        public static int Freeze(List<Process> procs)
        {
            int done = 0;
            for (int i = 0; i < procs.Count; i++)
            {
                Process p = procs[i];
                try
                {
                    if (IsFrozen(p.Id)) continue;
                    if (NtSuspendProcess(p.Handle) == 0)
                    {
                        lock (frozen) frozen.Add(p);
                        done++;
                    }
                }
                catch { /* процесс уже вышел или нет доступа - пропускаем */ }
            }
            return done;
        }

        // Размораживает всё, возвращает сколько процессов разбужено
        public static int ResumeAll()
        {
            int done = 0;
            lock (frozen)
            {
                for (int i = 0; i < frozen.Count; i++)
                {
                    try
                    {
                        if (NtResumeProcess(frozen[i].Handle) == 0) done++;
                    }
                    catch { }
                    finally { try { frozen[i].Dispose(); } catch { } }
                }
                frozen.Clear();
            }
            return done;
        }
    }
}
