using System.Reflection;

// Версия приложения задаётся здесь и подхватывается везде:
// в заголовке окна, в трее и в свойствах exe (Свойства -> Подробно)
[assembly: AssemblyTitle("DeepTools")]
[assembly: AssemblyProduct("DeepTools")]
[assembly: AssemblyDescription("Умный твикер для Windows / Smart tweaker for Windows")]
[assembly: AssemblyCompany("DeepTools")]
[assembly: AssemblyCopyright("© 2026 dep1xar")]
[assembly: AssemblyVersion("1.6.0.0")]
[assembly: AssemblyFileVersion("1.6.0.0")]

namespace DeepTools
{
    public static class AppVersion
    {
        // Короткая версия для интерфейса: "v0.5"
        public static string Short
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return "v" + v.Major + "." + v.Minor;
            }
        }
    }
}
