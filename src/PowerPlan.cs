using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DeepTools
{
    // Управление планами электропитания через powercfg.
    // Ultimate Performance - скрытый план Windows для максимальной производительности.
    // Заодно отключаем парковку ядер CPU (минимум активных ядер = 100%)
    public static class PowerPlan
    {
        private const string UltimateBaseGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string CoreParkingSetting = "0cc5b647-c1df-4637-891a-dec35c318583";

        private static string Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", args);
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process p = Process.Start(psi);
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                return p.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractGuid(string text)
        {
            if (text == null) return null;
            Match m = Regex.Match(text, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        public static string GetActiveGuid()
        {
            return ExtractGuid(Run("/getactivescheme"));
        }

        public static bool IsUltimateActive()
        {
            string active = GetActiveGuid();
            if (active == null) return false;
            if (active == UltimateBaseGuid) return true;
            string saved = AppConfig.Get("ultimate_guid", "").ToLowerInvariant();
            return saved != "" && active == saved;
        }

        // Включает Ultimate Performance (создаёт копию плана, если в системе его нет)
        // и отключает парковку ядер. Прошлый план запоминается для отката
        public static bool EnableUltimate()
        {
            string prev = GetActiveGuid();

            string target = null;
            string list = Run("/list");
            string listLower = list == null ? "" : list.ToLowerInvariant();

            if (listLower.Contains(UltimateBaseGuid)) target = UltimateBaseGuid;

            if (target == null)
            {
                string saved = AppConfig.Get("ultimate_guid", "").ToLowerInvariant();
                if (saved != "" && listLower.Contains(saved)) target = saved;
            }

            if (target == null)
            {
                target = ExtractGuid(Run("-duplicatescheme " + UltimateBaseGuid));
                if (target == null) return false;
                AppConfig.Set("ultimate_guid", target);
            }

            if (Run("/setactive " + target) == null) return false;
            if (prev != null && prev != target) AppConfig.Set("prev_plan_guid", prev);

            // Парковка ядер: держать 100% ядер активными (и от сети, и от батареи)
            Run("/setacvalueindex " + target + " SUB_PROCESSOR " + CoreParkingSetting + " 100");
            Run("/setdcvalueindex " + target + " SUB_PROCESSOR " + CoreParkingSetting + " 100");
            Run("/setactive " + target); // повторная активация применяет новые значения

            return true;
        }

        // GUID сбалансированного плана - для AdaptivePowerProfile
        public static string GetBalancedGuid() { return BalancedGuid; }

        // Возврат на план, который был до включения Ultimate (или Сбалансированный)
        public static bool RestorePrevious()
        {
            string prev = AppConfig.Get("prev_plan_guid", "");
            if (prev == "") prev = BalancedGuid;
            return Run("/setactive " + prev) != null;
        }
    }
}
