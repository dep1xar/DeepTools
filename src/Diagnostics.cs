using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DeepTools
{
    // Небольшой журнал для диагностики: старт/выход, падения, зависания UI.
    // crash.log остаётся как был (его читают пользователи), diagnostics.log
    // дополняет его тем, что не является исключением - например, подвисанием
    // интерфейса на 10 секунд или завершением процесса.
    public static class Diagnostics
    {
        private static readonly object sync = new object();
        private const long MaxBytes = 512 * 1024;

        public static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepTools");
            }
        }

        public static string LogPath
        {
            get { return Path.Combine(Folder, "diagnostics.log"); }
        }

        public static void Log(string message)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(Folder);
                    string path = LogPath;

                    try
                    {
                        var info = new FileInfo(path);
                        if (info.Exists && info.Length > MaxBytes)
                        {
                            string old = path + ".old";
                            try { if (File.Exists(old)) File.Delete(old); } catch { }
                            try { File.Move(path, old); } catch { }
                        }
                    }
                    catch { }

                    var sb = new StringBuilder();
                    sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ");
                    sb.Append(message);
                    sb.Append(Environment.NewLine);
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void LogException(string context, Exception ex)
        {
            if (ex == null) return;
            Log((context == null ? "exception" : context) + ": " + ex);
        }
    }

    // Маршалинг действий в UI-поток для фоновых потоков (MinerGuard, sampler,
    // уведомления из трея). Если окна ещё нет - действие выполняется напрямую.
    public static class Ui
    {
        private static Control anchor;

        public static void Init(Control control)
        {
            anchor = control;
        }

        public static void Post(Action action)
        {
            if (action == null) return;
            Control target = anchor;
            if (target == null || target.IsDisposed)
            {
                SafeInvoke(action);
                return;
            }

            try
            {
                if (target.InvokeRequired)
                {
                    target.BeginInvoke(action);
                    return;
                }
            }
            catch
            {
                // Окно закрывается или handle ещё не создан
            }

            SafeInvoke(action);
        }

        private static void SafeInvoke(Action action)
        {
            try { action(); } catch { }
        }
    }

    // Сторож UI-потока: раз в секунду MainForm отмечается, что жив. Если отметок
    // нет дольше 8 секунд, пишем в diagnostics.log - это отличает зависание от
    // тихого завершения процесса. Сам сторож работает в фоне, поэтому пишет
    // даже когда интерфейс заблокирован.
    public static class UiWatchdog
    {
        private const int FreezeMs = 8000;

        private static Thread thread;
        private static volatile bool running;
        private static long lastPing;
        private static bool reportedFreeze;

        public static void Start()
        {
            if (thread != null) return;
            lastPing = Environment.TickCount;
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "UiWatchdog";
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Start();
        }

        public static void Stop()
        {
            running = false;
        }

        public static void Ping()
        {
            lastPing = Environment.TickCount;
        }

        private static void Loop()
        {
            while (running)
            {
                Thread.Sleep(2000);

                int idle = unchecked(Environment.TickCount - (int)lastPing);
                if (idle < 0) idle = 0; // TickCount переполнился

                if (idle >= FreezeMs && !reportedFreeze)
                {
                    reportedFreeze = true;
                    Diagnostics.Log("UI thread did not respond for " + (idle / 1000) + " s");
                }
                else if (idle < 2000 && reportedFreeze)
                {
                    reportedFreeze = false;
                    Diagnostics.Log("UI thread responded again");
                }
            }
        }
    }
}
