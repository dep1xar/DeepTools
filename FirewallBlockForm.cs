using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DeepTools
{
    // Блокировка интернета приложениям: список запущенных программ с путём к exe
    // и кнопкой блок/разблок. Работает через правила брандмауэра (NetworkTools).
    public class FirewallBlockForm : Form
    {
        private FlowLayoutPanel list;

        public FirewallBlockForm()
        {
            Text = Lang.T("Блокировка интернета", "Block internet access");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            ClientSize = new Size(560, 520);

            BuildUi();
            Populate();
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = Lang.T("Блокировка интернета приложениям", "Block apps from the internet"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(20, 16),
                AutoSize = true
            };
            Controls.Add(title);

            var desc = new Label
            {
                Text = Lang.T("Правило брандмауэра Windows режет приложению доступ в сеть. Повторный клик снимает блок.",
                              "A Windows Firewall rule cuts the app's network access. Click again to remove it."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 46),
                Size = new Size(520, 32)
            };
            Controls.Add(desc);

            var refreshBtn = new RoundedButton
            {
                Text = Lang.T("Обновить", "Refresh"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(440, 44),
                Size = new Size(100, 30)
            };
            refreshBtn.Click += (s, e) => Populate();
            Controls.Add(refreshBtn);

            list = new FlowLayoutPanel
            {
                Location = new Point(20, 86),
                Size = new Size(520, 414),
                BackColor = Theme.SidebarColor,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };
            Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);
        }

        // PLACEHOLDER_POPULATE
        private void Populate()
        {
            list.Controls.Clear();

            // Уникальные приложения по пути к exe (у одной программы много процессов)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var apps = new List<string>();
            Process[] procs;
            try { procs = Process.GetProcesses(); }
            catch { return; }

            foreach (Process p in procs)
            {
                string path = null;
                try { path = p.MainModule != null ? p.MainModule.FileName : null; }
                catch { } // системный/защищённый процесс - недоступен
                finally { try { p.Dispose(); } catch { } }
                if (string.IsNullOrEmpty(path)) continue;
                if (seen.Add(path)) apps.Add(path);
            }

            apps.Sort(delegate(string a, string b) {
                return string.Compare(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b),
                    StringComparison.OrdinalIgnoreCase);
            });

            if (apps.Count == 0)
            {
                list.Controls.Add(new Label
                {
                    Text = Lang.T("Не удалось получить список приложений", "Could not read the app list"),
                    ForeColor = Theme.TextDim, BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F), AutoSize = true, Margin = new Padding(6)
                });
                return;
            }

            foreach (string path in apps) list.Controls.Add(BuildRow(path));
        }

        private Panel BuildRow(string path)
        {
            var row = new Panel { Size = new Size(496, 40), BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2) };

            var nameLbl = new Label
            {
                Text = System.IO.Path.GetFileName(path),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(6, 3),
                AutoSize = true
            };
            row.Controls.Add(nameLbl);

            var pathLbl = new Label
            {
                Text = path,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(6, 21),
                Size = new Size(360, 14),
                AutoEllipsis = true
            };
            row.Controls.Add(pathLbl);

            var btn = new RoundedButton
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                Location = new Point(376, 6),
                Size = new Size(114, 28)
            };
            StyleToggle(btn, NetworkTools.IsBlocked(path));
            btn.Click += (s, e) => {
                bool blocked = NetworkTools.IsBlocked(path);
                btn.Enabled = false;
                var worker = new System.ComponentModel.BackgroundWorker();
                worker.DoWork += (s2, e2) => {
                    if (blocked) NetworkTools.Unblock(path);
                    else NetworkTools.Block(path);
                };
                worker.RunWorkerCompleted += (s2, e2) => {
                    if (IsDisposed) return;
                    btn.Enabled = true;
                    StyleToggle(btn, NetworkTools.IsBlocked(path));
                };
                worker.RunWorkerAsync();
            };
            row.Controls.Add(btn);

            return row;
        }

        private static void StyleToggle(RoundedButton btn, bool blocked)
        {
            btn.Text = blocked ? Lang.T("Разблокировать", "Unblock") : Lang.T("Блокировать", "Block");
            btn.ButtonColor = blocked ? Theme.Danger : Theme.KeyColor;
            btn.HoverColor = blocked ? Theme.DangerHover : Theme.KeyHover;
            btn.TextColor = blocked ? Theme.BgColor : Theme.TextMain;
            btn.Invalidate();
        }
    }
}
