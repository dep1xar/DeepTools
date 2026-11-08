using System;
using System.Management;

namespace DeepTools
{
    // Создание точки восстановления системы через WMI (SystemRestore, root\default).
    // Предлагается перед рискованными твиками: отключение служб, деблоат, телеметрия.
    // Требует прав администратора. Windows молча троттлит точки чаще раза в сутки -
    // в этом случае честно сообщаем результат вместо тихого «успеха»
    public static class RestorePoint
    {
        private static bool creating;

        public static bool IsCreating { get { return creating; } }

        // Создаёт точку восстановления в фоне. callback(ok, message) приходит в UI-потоке
        public static void CreateInBackground(string description, Action<bool, string> callback)
        {
            if (creating)
            {
                if (callback != null) callback(false, Lang.T("Точка уже создаётся, подожди", "A restore point is already being created, please wait"));
                return;
            }
            if (!Program.IsRunningAsAdmin())
            {
                if (callback != null) callback(false, Lang.T("Нужны права администратора", "Administrator rights required"));
                return;
            }

            creating = true;
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += (s, e) => { e.Result = Create(description); };
            worker.RunWorkerCompleted += (s, e) => {
                creating = false;
                string error = e.Result as string;
                if (callback != null)
                {
                    if (error == null)
                        callback(true, Lang.T("Точка восстановления создана ✓", "Restore point created ✓"));
                    else
                        callback(false, error);
                }
            };
            worker.RunWorkerAsync();
        }

        // «Создать точку восстановления перед изменениями?» Да - создаём и по завершении
        // зовём proceed, Нет - proceed сразу, Отмена - ничего. Без прав админа не спрашиваем
        public static void OfferBefore(System.Windows.Forms.IWin32Window owner, Action proceed)
        {
            if (!Program.IsRunningAsAdmin()) { proceed(); return; }

            System.Windows.Forms.DialogResult r = DTDialog.Show(owner,
                Lang.T("Создать точку восстановления системы перед изменениями?\n\nЕсли что-то пойдёт не так, можно будет откатиться. Занимает 10-30 секунд.",
                       "Create a system restore point before making changes?\n\nIf anything goes wrong you can roll back. Takes 10-30 seconds."),
                Lang.T("Точка восстановления", "Restore point"),
                System.Windows.Forms.MessageBoxButtons.YesNoCancel,
                System.Windows.Forms.MessageBoxIcon.Question);

            if (r == System.Windows.Forms.DialogResult.Cancel) return;
            if (r == System.Windows.Forms.DialogResult.No) { proceed(); return; }

            CreateInBackground("DeepTools", (ok, msg) => {
                if (!ok)
                    TrayNotify.Info(Lang.T("Точка восстановления", "Restore point"), msg);
                proceed();
            });
        }

        // Возвращает null при успехе или текст ошибки
        private static string Create(string description)
        {            try
            {
                var scope = new ManagementScope("\\\\localhost\\root\\default");
                var path = new ManagementPath("SystemRestore");
                using (var sr = new ManagementClass(scope, path, new ObjectGetOptions()))
                using (ManagementBaseObject inParams = sr.GetMethodParameters("CreateRestorePoint"))
                {
                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                    inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                    using (ManagementBaseObject outParams = sr.InvokeMethod("CreateRestorePoint", inParams, null))
                    {
                        uint ret = Convert.ToUInt32(outParams["ReturnValue"]);
                        if (ret == 0) return null;
                        if (ret == 1058)
                            return Lang.T("Восстановление системы выключено (Панель управления → Восстановление)", "System Restore is disabled (Control Panel → Recovery)");
                        return Lang.T("Не получилось, код ", "Failed, code ") + ret
                            + Lang.T(" (Windows создаёт не чаще 1 точки в сутки)", " (Windows allows at most 1 point per day)");
                    }
                }
            }
            catch (Exception ex)
            {
                return Lang.T("Ошибка: ", "Error: ") + ex.Message;
            }
        }
    }
}
