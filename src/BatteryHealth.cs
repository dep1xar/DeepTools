using System;
using System.Management;

namespace DeepTools
{
    // Здоровье батареи ноутбука через WMI (root\WMI). Износ = насколько текущая
    // полная ёмкость просела относительно заводской. На десктопах батареи нет - null.
    public static class BatteryHealth
    {
        public class Info
        {
            public int DesignedmWh;   // заводская ёмкость
            public int FullChargemWh; // текущая полная ёмкость
            public int CycleCount;    // циклов заряда (0 если недоступно)
            public int WearPercent;   // износ, %
        }

        public static Info Get()
        {
            int designed = QueryInt("BatteryStaticData", "DesignedCapacity");
            int full = QueryInt("BatteryFullChargedCapacity", "FullChargedCapacity");
            if (designed <= 0 || full <= 0) return null; // нет батареи или данные недоступны

            var info = new Info
            {
                DesignedmWh = designed,
                FullChargemWh = full,
                CycleCount = QueryInt("BatteryCycleCount", "CycleCount"),
            };
            int wear = (int)Math.Round((designed - full) * 100.0 / designed);
            info.WearPercent = wear < 0 ? 0 : wear;
            return info;
        }

        private static int QueryInt(string cls, string prop)
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT " + prop + " FROM " + cls))
                foreach (ManagementObject mo in searcher.Get())
                {
                    object v = mo[prop];
                    if (v != null) return Convert.ToInt32(v);
                }
            }
            catch { }
            return 0;
        }
    }
}
