using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeepTools
{
    // Окно с тумблерами дебloat Windows 11 (реклама, Copilot, Recall, виджеты, Bing).
    public class Win11TweaksForm : Form
    {
        public Win11TweaksForm()
        {
            Text = Lang.T("Твики Windows 11", "Windows 11 tweaks");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            ClientSize = new Size(560, 480);

            var title = new Label
            {
                Text = Lang.T("Дебloat Windows 11", "Windows 11 debloat"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(20, 16),
                AutoSize = true
            };
            Controls.Add(title);

            var desc = new Label
            {
                Text = Lang.T("Всё обратимо. Часть изменений (виджеты, поиск) применится после перезапуска Проводника.",
                              "Everything is reversible. Some changes (widgets, search) apply after restarting Explorer."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 46),
                Size = new Size(520, 30)
            };
            Controls.Add(desc);

            var list = new FlowLayoutPanel
            {
                Location = new Point(20, 84),
                Size = new Size(520, 380),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            foreach (Win11Tweaks.Tweak t in Win11Tweaks.All())
                list.Controls.Add(MakeRow(t));
        }

        private Panel MakeRow(Win11Tweaks.Tweak t)
        {
            var row = new Panel { Size = new Size(496, 52), BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 4) };

            var name = new Label
            {
                Text = t.Title,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(6, 4),
                Size = new Size(420, 18),
                AutoEllipsis = true
            };
            row.Controls.Add(name);

            var desc = new Label
            {
                Text = t.Desc,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(6, 24),
                Size = new Size(430, 24),
                AutoEllipsis = true
            };
            row.Controls.Add(desc);

            var toggle = new ToggleSwitch { Location = new Point(444, 14), Checked = t.IsApplied() };
            toggle.CheckedChanged += (s, e) => t.Set(toggle.Checked);
            row.Controls.Add(toggle);

            return row;
        }
    }
}
