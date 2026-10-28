using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // Плавная прокрутка «как листаешь».
    //
    // Было: колесо мыши прокручивало панель мгновенным прыжком на фиксированный
    // шаг - страница дёргалась, а ползунок DarkScroll прыгал вместе с ней.
    //
    // Стало: дельты колеса копятся в цель, а панель доезжает до неё с
    // затуханием (быстрый старт, мягкое торможение). Ползунок берёт позицию из
    // AutoScrollPosition на каждом событии Scroll, поэтому едет кадр в кадр
    // вместе с содержимым и не может «остановиться», пока содержимое едет.
    //
    // Колесо ловим фильтром сообщений: так прокрутка одинаково плавная и когда
    // курсор над самой панелью, и когда он над карточкой, тумблером или
    // картинкой внутри неё.
    public static class SmoothScroll
    {
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WHEEL_DELTA = 120;
        private const double TAU = 0.075;      // постоянная времени затухания, сек
        private const int TICK_MS = 16;        // ~60 кадров в секунду

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point p);

        private sealed class State
        {
            public ScrollableControl Target;
            public double Position;    // текущее смещение, px
            public double Goal;        // куда едем
            public bool Active;
        }

        private static readonly Dictionary<ScrollableControl, State> states =
            new Dictionary<ScrollableControl, State>();
        private static readonly List<State> running = new List<State>();
        private static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        private static System.Windows.Forms.Timer timer;
        private static long lastTick;
        private static WheelFilter filter;

        // Панель подключается к плавной прокрутке (вызывается из DarkScroll.Attach)
        public static void Attach(ScrollableControl panel)
        {
            if (panel == null || states.ContainsKey(panel)) return;

            var s = new State { Target = panel };
            s.Position = CurrentOffset(panel);
            s.Goal = s.Position;
            states[panel] = s;

            panel.HandleDestroyed += (sender, e) => Detach(panel);

            if (filter == null)
            {
                filter = new WheelFilter();
                try { Application.AddMessageFilter(filter); } catch { }
            }
        }

        private static void Detach(ScrollableControl panel)
        {
            State s;
            if (!states.TryGetValue(panel, out s)) return;
            running.Remove(s);
            states.Remove(panel);
            if (running.Count == 0 && timer != null) timer.Stop();
        }

        // Панель прокрутили другим способом (ползунок, клавиатура) - подхватываем
        public static void SyncFromTarget(ScrollableControl panel)
        {
            State s;
            if (panel == null || !states.TryGetValue(panel, out s)) return;
            if (s.Active) return;   // во время анимации источник истины - анимация
            s.Position = CurrentOffset(panel);
            s.Goal = s.Position;
        }

        // Прервать доезд (например, пользователь схватил ползунок)
        public static void Stop(ScrollableControl panel)
        {
            State s;
            if (panel == null || !states.TryGetValue(panel, out s)) return;
            s.Active = false;
            running.Remove(s);
            s.Position = CurrentOffset(panel);
            s.Goal = s.Position;
        }

        // Обработать дельту колеса (120 = один щелчок)
        public static void ScrollBy(ScrollableControl panel, int wheelDelta)
        {
            if (panel == null || wheelDelta == 0) return;

            State s;
            if (!states.TryGetValue(panel, out s)) Attach(panel);
            if (!states.TryGetValue(panel, out s)) return;

            int max = MaxOffset(panel);
            if (max <= 0) return;

            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;          // 0 = «на страницу», редкая настройка

            ScrollProperties vs = panel.VerticalScroll;
            int step = vs.SmallChange > 0 ? vs.SmallChange : 40;
            if (step < 16) step = 40;

            double amount = (double)step * lines * wheelDelta / WHEEL_DELTA;

            if (!s.Active)
            {
                s.Position = CurrentOffset(panel);
                s.Goal = s.Position;
            }

            s.Goal -= amount;                   // колесо от себя - вниз (смещение растёт)
            if (s.Goal < 0) s.Goal = 0;
            if (s.Goal > max) s.Goal = max;

            if (Math.Abs(s.Goal - s.Position) < 0.5) return;

            if (!s.Active)
            {
                s.Active = true;
                running.Add(s);
            }
            EnsureTimer();
        }

        private static void EnsureTimer()
        {
            if (timer == null)
            {
                timer = new System.Windows.Forms.Timer { Interval = TICK_MS };
                timer.Tick += (sender, e) => Step();
            }
            lastTick = clock.ElapsedMilliseconds;
            if (!timer.Enabled) timer.Start();
        }

        private static void Step()
        {
            long now = clock.ElapsedMilliseconds;
            double dt = (now - lastTick) / 1000.0;
            lastTick = now;
            if (dt <= 0.0005) dt = 0.0005;
            if (dt > 0.1) dt = 0.1;             // после подвисания не прыгаем

            double k = 1.0 - Math.Exp(-dt / TAU);

            for (int i = running.Count - 1; i >= 0; i--)
            {
                State s = running[i];
                double diff = s.Goal - s.Position;

                if (Math.Abs(diff) < 0.5)
                {
                    s.Position = s.Goal;
                    Apply(s);
                    s.Active = false;
                    running.RemoveAt(i);
                    continue;
                }

                s.Position += diff * k;
                Apply(s);
            }

            if (running.Count == 0 && timer != null) timer.Stop();
        }

        private static void Apply(State s)
        {
            try
            {
                int max = MaxOffset(s.Target);
                double v = s.Position;
                if (v < 0) v = 0;
                if (v > max) v = max;

                int h = -s.Target.AutoScrollPosition.X;
                s.Target.AutoScrollPosition = new Point(h, (int)Math.Round(v));
            }
            catch { }
        }

        private static int CurrentOffset(ScrollableControl panel)
        {
            try
            {
                int v = -panel.AutoScrollPosition.Y;
                return v < 0 ? 0 : v;
            }
            catch { return 0; }
        }

        private static int MaxOffset(ScrollableControl panel)
        {
            ScrollProperties vs = panel.VerticalScroll;
            int max = vs.Maximum - vs.LargeChange + 1;
            return max < 0 ? 0 : max;
        }

        // Контролы, у которых своё поведение колеса (списки, поля ввода, ComboBox):
        // их не перехватываем, иначе сломаем прокрутку внутри них
        private static bool HandlesOwnWheel(Control c)
        {
            for (Control cur = c; cur != null; cur = cur.Parent)
            {
                if (cur is TextBoxBase || cur is ListBox || cur is ComboBox || cur is UpDownBase ||
                    cur is ScrollBar || cur is TrackBar || cur is ListView || cur is TreeView ||
                    cur is DataGridView || cur is PropertyGrid || cur is WebBrowser ||
                    cur is RichTextBox || cur is DateTimePicker)
                    return true;
            }
            return false;
        }

        private sealed class WheelFilter : IMessageFilter
        {
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_MOUSEWHEEL) return false;

                int delta = (short)((long)m.WParam >> 16);
                var screen = new Point(
                    (short)((long)m.LParam & 0xFFFF),
                    (short)(((long)m.LParam >> 16) & 0xFFFF));

                Control hit;
                try { hit = Control.FromHandle(WindowFromPoint(screen)); }
                catch { return false; }
                if (hit == null) return false;
                if (HandlesOwnWheel(hit)) return false;

                // Ищем ближайший контейнер, который реально может крутиться в
                // нужную сторону: в разделах это то сама панель, то вложенный
                // список карточек. Упёршийся контейнер пропускаем - дальше
                // крутится родитель (как в проводнике). Незнакомый подключаем
                // на месте, чтобы плавность была везде, а не только там, где её
                // включили руками
                for (Control cur = hit; cur != null; cur = cur.Parent)
                {
                    var sc = cur as ScrollableControl;
                    if (sc == null) continue;
                    if (!CanScroll(sc)) continue;
                    if (!CanMove(sc, delta)) continue;
                    if (!states.ContainsKey(sc)) Attach(sc);

                    ScrollBy(sc, delta);
                    return true;   // дальше сообщение не идёт - прыжка не будет
                }
                return false;
            }
        }

        private static bool CanScroll(ScrollableControl panel)
        {
            try { return MaxOffset(panel) > 0; }
            catch { return false; }
        }

        // Может ли контейнер ещё ехать в сторону, куда крутят колесо
        private static bool CanMove(ScrollableControl panel, int delta)
        {
            if (delta == 0) return false;
            int offset = CurrentOffset(panel);
            int max = MaxOffset(panel);
            return delta > 0 ? offset > 0 : offset < max;
        }
    }
}
