using System.Reflection;

// Версия приложения задаётся здесь и подхватывается везде:
// в заголовке окна, в трее и в свойствах exe (Свойства -> Подробно)
[assembly: AssemblyTitle("DeepTools")]
[assembly: AssemblyProduct("DeepTools")]
[assembly: AssemblyDescription("Умный твикер для Windows / Smart tweaker for Windows")]
[assembly: AssemblyCompany("DeepTools")]
[assembly: AssemblyCopyright("© 2026 dep1xar")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]

namespace DeepTools
{
    public static class AppVersion
    {
        // Версия для интерфейса: "2.3.0" (без префикса v)
        public static string Short
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v.Major + "." + v.Minor + "." + v.Build;
            }
        }
    }
}
