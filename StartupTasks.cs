using System;
using System.Collections.Generic;
using System.Reflection;

namespace DeepTools
{
    // Задачи Планировщика Windows, которые стартуют при входе/загрузке - туда часто
    // прописывается мусор и апдейтеры мимо обычной автозагрузки. Работаем с COM
    // "Schedule.Service" через рефлексию (без dynamic, чтобы не тянуть Microsoft.CSharp).
    public static class StartupTasks
    {
        private const int TRIGGER_BOOT = 8;
        private const int TRIGGER_LOGON = 9;
        private const int ACTION_EXEC = 0;

        private static object Inv(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);
        }
        private static object Get(object o, string name)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, null);
        }
        private static object Item(object coll, int index)
        {
            return coll.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, coll, new object[] { index });
        }

        // Логон/бут-задачи как StartupItem (кроме системных \Microsoft\...)
        public static List<StartupItem> GetLogonTasks()
        {
            var result = new List<StartupItem>();
            try
            {
                Type t = Type.GetTypeFromProgID("Schedule.Service");
                if (t == null) return result;
                object svc = Activator.CreateInstance(t);
                Inv(svc, "Connect");
                object root = Inv(svc, "GetFolder", "\\");
                Walk(root, result);
            }
            catch { }
            return result;
        }

        private static void Walk(object folder, List<StartupItem> result)
        {
            // Задачи текущей папки
            try
            {
                object tasks = Inv(folder, "GetTasks", 1); // 1 = включая скрытые
                int count = Convert.ToInt32(Get(tasks, "Count"));
                for (int i = 1; i <= count; i++)
                {
                    try { AddIfLogon(Item(tasks, i), result); } catch { }
                }
            }
            catch { }

            // Подпапки (кроме \Microsoft - там системные задачи, их не трогаем)
            try
            {
                object folders = Inv(folder, "GetFolders", 0);
                int count = Convert.ToInt32(Get(folders, "Count"));
                for (int i = 1; i <= count; i++)
                {
                    object sub = Item(folders, i);
                    string path = Convert.ToString(Get(sub, "Path"));
                    if (path != null && path.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    Walk(sub, result);
                }
            }
            catch { }
        }

        private static void AddIfLogon(object task, List<StartupItem> result)
        {
            object def = Get(task, "Definition");
            object triggers = Get(def, "Triggers");
            int tcount = Convert.ToInt32(Get(triggers, "Count"));
            bool isLogon = false;
            for (int i = 1; i <= tcount; i++)
            {
                int type = Convert.ToInt32(Get(Item(triggers, i), "Type"));
                if (type == TRIGGER_LOGON || type == TRIGGER_BOOT) { isLogon = true; break; }
            }
            if (!isLogon) return;

            // Путь к exe из первого exec-действия (для рейтинга подозрительности)
            string exe = "";
            try
            {
                object actions = Get(def, "Actions");
                int acount = Convert.ToInt32(Get(actions, "Count"));
                for (int i = 1; i <= acount; i++)
                {
                    object a = Item(actions, i);
                    if (Convert.ToInt32(Get(a, "Type")) == ACTION_EXEC)
                    {
                        exe = Convert.ToString(Get(a, "Path")); break;
                    }
                }
            }
            catch { }

            result.Add(new StartupItem
            {
                Name = Convert.ToString(Get(task, "Name")),
                Command = string.IsNullOrEmpty(exe) ? Convert.ToString(Get(task, "Path")) : exe,
                SourceLabel = Lang.T("Планировщик задач", "Task Scheduler"),
                IsTask = true,
                TaskPath = Convert.ToString(Get(task, "Path")),
                Enabled = Convert.ToBoolean(Get(task, "Enabled"))
            });
        }

        // Включить/выключить задачу по её полному пути
        public static bool SetEnabled(string taskPath, bool enabled)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("Schedule.Service");
                object svc = Activator.CreateInstance(t);
                Inv(svc, "Connect");
                object root = Inv(svc, "GetFolder", "\\");
                object task = Inv(root, "GetTask", taskPath);
                task.GetType().InvokeMember("Enabled", BindingFlags.SetProperty, null, task, new object[] { enabled });
                return true;
            }
            catch { return false; }
        }
    }
}
