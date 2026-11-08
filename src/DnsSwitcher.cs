using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DeepTools
{
    // Переключатель DNS: меняет DNS-серверы на всех активных сетевых адаптерах
    // через netsh (программа запущена от администратора, права есть). Пресеты -
    // публичные резолверы, «Авто» возвращает получение DNS по DHCP.
    public static class DnsSwitcher
    {
        public class Preset
        {
            public readonly string Name;
            public readonly string Primary;   // null = вернуть автоматический (DHCP) DNS
            public readonly string Secondary;
            public Preset(string name, string primary, string secondary)
            {
                Name = name; Primary = primary; Secondary = secondary;
            }
        }

        public static readonly Preset[] Presets = new Preset[]
        {
            new Preset("Cloudflare", "1.1.1.1", "1.0.0.1"),
            new Preset("Google", "8.8.8.8", "8.8.4.4"),
            new Preset("AdGuard", "94.140.14.14", "94.140.15.15"),
            new Preset("Auto", null, null),
        };

        // «Настоящие» активные адаптеры (Ethernet/Wi-Fi) с поддержкой IPv4,
        // без loopback, туннелей и отключённых - на них и меняем DNS
        private static List<string> ActiveInterfaceNames()
        {
            var names = new List<string>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    var t = nic.NetworkInterfaceType;
                    if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;
                    try { nic.GetIPProperties().GetIPv4Properties(); }
                    catch { continue; } // адаптер без IPv4 - пропускаем
                    names.Add(nic.Name);
                }
            }
            catch { }
            return names;
        }

        // Применить пресет ко всем активным адаптерам. Возвращает число адаптеров,
        // на которых команда прошла без ошибки
        public static int Apply(Preset preset)
        {
            int ok = 0;
            foreach (string iface in ActiveInterfaceNames())
            {
                if (ApplyToInterface(iface, preset)) ok++;
            }
            FlushDnsCache();
            return ok;
        }

        private static bool ApplyToInterface(string iface, Preset preset)
        {
            if (preset.Primary == null)
                return RunNetsh("interface ipv4 set dnsservers name=\"" + iface + "\" source=dhcp");

            bool primaryOk = RunNetsh("interface ipv4 set dnsservers name=\"" + iface +
                                      "\" static " + preset.Primary + " primary validate=no");
            if (!string.IsNullOrEmpty(preset.Secondary))
                RunNetsh("interface ipv4 add dnsservers name=\"" + iface +
                         "\" address=" + preset.Secondary + " index=2 validate=no");
            return primaryOk;
        }

        // Текущий IPv4-DNS первого активного адаптера (для отображения)
        public static string CurrentDns()
        {
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    var t = nic.NetworkInterfaceType;
                    if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;

                    var list = new List<string>();
                    foreach (IPAddress dns in nic.GetIPProperties().DnsAddresses)
                        if (dns.AddressFamily == AddressFamily.InterNetwork) list.Add(dns.ToString());

                    if (list.Count > 0) return string.Join(", ", list.ToArray());
                }
            }
            catch { }
            return null;
        }

        private static void FlushDnsCache()
        {
            RunProcess("ipconfig", "/flushdns", 3000);
        }

        private static bool RunNetsh(string args)
        {
            return RunProcess("netsh", args, 5000);
        }

        private static bool RunProcess(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (!p.WaitForExit(timeoutMs)) return false;
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }
    }
}
