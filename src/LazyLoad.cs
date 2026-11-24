using System;
using System.Windows.Forms;

namespace DeepTools
{
    // Отложенная загрузка данных раздела: панель показывается сразу, а список,
    // статусы и прочее наполняются следующим сообщением UI-потока. Раньше
    // конструктор вкладки успевал опросить реестр/процессы/диск до первого
    // кадра, и клик по меню ощущался как зависание.
    public static class LazyLoad
    {
        public static void OnFirstVisible(Control control, Action load)
        {
            if (control == null || load == null) return;

            bool started = false;
            EventHandler handler = null;
            handler = delegate
            {
                // Свежесозданный Control тоже отдаёт Visible=true, поэтому
                // ждём реального окна (Handle): иначе загрузка пойдёт прямо
                // в конструкторе, пока панель ещё ни к чему не прикреплена
                if (started || !control.Visible || !control.IsHandleCreated) return;
                started = true;
                try { control.VisibleChanged -= handler; } catch { }
                try { control.ParentChanged -= handler; } catch { }
                try { control.HandleCreated -= handler; } catch { }
                Run(control, load);
            };

            control.VisibleChanged += handler;
            control.ParentChanged += handler;
            control.HandleCreated += handler;
            // Панель могла быть уже показана до подписки
            handler(control, EventArgs.Empty);
        }

        private static void Run(Control control, Action load)
        {
            try
            {
                if (control.IsHandleCreated)
                {
                    control.BeginInvoke(new Action(delegate
                    {
                        if (!control.IsDisposed)
                        {
                            try { load(); } catch { }
                        }
                    }));
                    return;
                }
            }
            catch { }

            // Handle ещё не создан (первый кадр окна) - грузим сразу
            try { if (!control.IsDisposed) load(); } catch { }
        }
    }
}
