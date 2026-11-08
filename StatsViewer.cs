using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

#if DT_STATS_VIEWER
namespace DeepTools
{
    // Отдельная программа для автора и помощников: показывает полную панель
    // «Онлайн DeepTools», но не отправляет телеметрию и не требует прав
    // администратора. Собирается как DeepToolsStats.exe.
    public static class StatsViewerProgram
    {
        [STAThread]
        static void Main()
        {
            try { Program.TryEnableDpiAwareness(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppConfig.Load();
            Theme.ApplyNamed(AppConfig.Get("theme", "dark"));

            string lang = AppConfig.Get("language", "");
            if (string.IsNullOrEmpty(lang))
            {
                bool systemIsRussian = CultureInfo.CurrentUICulture
                    .TwoLetterISOLanguageName == "ru";
                lang = systemIsRussian ? "ru" : "en";
            }
            Lang.IsEn = lang == "en";

            Application.Run(new StatsViewerForm());
        }
    }

    public class StatsViewerForm : Form
    {
        public StatsViewerForm()
        {
            Text = Lang.T("DeepTools — Онлайн", "DeepTools — Online");
            ClientSize = new Size(760, 616);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.BgColor;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            var panel = new OnlinePanel(true) { Dock = DockStyle.Fill };
            Controls.Add(panel);
        }
    }
}
#endif
