using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DeepTools
{
    // Сетевые инструменты: сброс сетевого стека (winsock/IP) и блокировка
    // интернета конкретному приложению через правила брандмауэра Windows.
    // Всё через netsh - права администратора у программы есть.
    public static class NetworkTools
    {
        // Полный сброс сетевого стека. Часть изменений применяется после перезагрузки.
        public static void ResetNetworkStack()
        {
            RunNetsh("winsock reset");
            RunNetsh("int ip reset");
            RunProc("ipconfig", "/flushdns");
            RunProc("ipconfig", "/release");
            RunProc("ipconfig", "/renew");
        }

        // Имя правила брандмауэра для данного exe (одно имя на out+in)
        private static string RuleName(string exePath)
        {
            return "DeepTools Block " + Path.GetFileName(exePath);
        }

        // Список заблокированных путей ведём в конфиге - чтобы UI не дёргал netsh
        // на каждый процесс (это медленно). netsh - для самого действия.
        public static HashSet<string> BlockedSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in AppConfig.Get("fw_blocked", "").Split('|'))
                if (!string.IsNullOrEmpty(p)) set.Add(p);
            return set;
        }

        private static void SaveBlocked(HashSet<string> set)
        {
            var list = new List<string>(set);
            AppConfig.Set("fw_blocked", string.Join("|", list.ToArray()));
        }

        public static bool IsBlocked(string exePath)
        {
            return BlockedSet().Contains(exePath);
        }

        public static void Block(string exePath)
        {
            string name = RuleName(exePath);
            RunNetsh("advfirewall firewall add rule name=\"" + name +
                     "\" dir=out action=block program=\"" + exePath + "\" enable=yes");
            RunNetsh("advfirewall firewall add rule name=\"" + name +
                     "\" dir=in action=block program=\"" + exePath + "\" enable=yes");
            var set = BlockedSet(); set.Add(exePath); SaveBlocked(set);
        }

        public static void Unblock(string exePath)
        {
            RunNetsh("advfirewall firewall delete rule name=\"" + RuleName(exePath) + "\"");
            var set = BlockedSet(); set.Remove(exePath); SaveBlocked(set);
        }

        private static int RunNetsh(string args) { return RunProc("netsh", args); }

        private static int RunProc(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return -1;
                    p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(8000)) return -1;
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }
}
