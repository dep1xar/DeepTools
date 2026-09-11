using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeepTools
{
    // «Корзина с откатом»: очистка с бэкапом + список восстановления.
    public class RecycleBinForm : Form
    {
        private FlowLayoutPanel list;
        private Label status;

        public RecycleBinForm()
        {
            Text = Lang.T("Корзина с откатом", "Recycle bin with undo");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.BgColor;
            ClientSize = new Size(580, 470);

            Controls.Add(new Label
            {
                Text = Lang.T("Корзина с откатом", "Recycle bin with undo"),
                ForeColor = Theme.TextMain, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                Location = new Point(20, 16), AutoSize = true
            });
            Controls.Add(new Label
            {
                Text = Lang.T("Очистка сначала копирует файлы в бэкап DeepTools, потом чистит Корзину. Восстановить можно " + RecycleGuard.RetentionDays + " дней.",
                              "Emptying first backs files up in DeepTools, then clears the bin. You can restore for " + RecycleGuard.RetentionDays + " days."),
                ForeColor = Theme.TextDim, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(20, 44), Size = new Size(540, 32)
            });

            var emptyBtn = new RoundedButton
            {
                Text = Lang.T("🗑 Очистить корзину (с откатом)", "🗑 Empty bin (with undo)"),
                ButtonColor = Theme.Accent, HoverColor = Theme.AccentHover, TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(20, 84), Size = new Size(280, 32)
            };
            emptyBtn.Click += (s, e) => DoEmpty(emptyBtn);
            Controls.Add(emptyBtn);

            status = new Label
            {
                Text = "", ForeColor = Theme.Accent, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F), Location = new Point(312, 92), Size = new Size(248, 32), AutoEllipsis = true
            };
            Controls.Add(status);

            list = new FlowLayoutPanel
            {
                Location = new Point(20, 128), Size = new Size(540, 326),
                BackColor = Theme.SidebarColor, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true
            };
            Controls.Add(list);
            NativeMethods.ApplyDarkScrollbar(list);

            RefreshList();
        }

        // PLACEHOLDER_REST
        private void DoEmpty(RoundedButton btn)
        {
            if (DTDialog.Show(
                    Lang.T("Очистить корзину? Файлы сохранятся в бэкап DeepTools — их можно вернуть " + RecycleGuard.RetentionDays + " дней.",
                           "Empty the recycle bin? Files are backed up in DeepTools — recoverable for " + RecycleGuard.RetentionDays + " days."),
                    "DeepTools", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            btn.Enabled = false; btn.Text = "...";
            status.ForeColor = Theme.TextDim; status.Text = Lang.T("Сохраняю и чищу...", "Backing up and emptying...");
            var w = new System.ComponentModel.BackgroundWorker();
            w.DoWork += (s, e) => e.Result = RecycleGuard.SafeEmpty();
            w.RunWorkerCompleted += (s, e) => {
                if (IsDisposed) return;
                btn.Enabled = true; btn.Text = Lang.T("🗑 Очистить корзину (с откатом)", "🗑 Empty bin (with undo)");
                int n = (e.Error == null && e.Result != null) ? (int)e.Result : 0;
                status.ForeColor = Theme.Accent;
                status.Text = Lang.T("Корзина очищена, в бэкапе: ", "Emptied, backed up: ") + n;
                RefreshList();
            };
            w.RunWorkerAsync();
        }

        private void RefreshList()
        {
            list.Controls.Clear();
            List<RecycleGuard.Backup> items = RecycleGuard.ListBackups();
            if (items.Count == 0)
            {
                list.Controls.Add(new Label
                {
                    Text = Lang.T("Пока нечего восстанавливать", "Nothing to restore yet"),
                    ForeColor = Theme.TextDim, BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F), AutoSize = true, Margin = new Padding(6)
                });
                return;
            }
            foreach (RecycleGuard.Backup b in items) list.Controls.Add(MakeRow(b));
        }

        private Panel MakeRow(RecycleGuard.Backup b)
        {
            var row = new Panel { Size = new Size(516, 46), BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 2) };

            row.Controls.Add(new Label
            {
                Text = (b.IsDir ? "📁 " : "") + b.Name,
                ForeColor = Theme.TextMain, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(6, 4), Size = new Size(380, 18), AutoEllipsis = true
            });
            row.Controls.Add(new Label
            {
                Text = (string.IsNullOrEmpty(b.OrigPath) ? Lang.T("исходный путь неизвестен", "original path unknown") : b.OrigPath)
                       + "  ·  " + b.When.ToString("dd.MM HH:mm"),
                ForeColor = Theme.TextDim, BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(6, 24), Size = new Size(400, 16), AutoEllipsis = true
            });

            var btn = new RoundedButton
            {
                Text = Lang.T("Восстановить", "Restore"),
                ButtonColor = Theme.KeyColor, HoverColor = Theme.KeyHover, TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(396, 9), Size = new Size(114, 28)
            };
            btn.Click += (s, e) => {
                btn.Enabled = false;
                string res = RecycleGuard.Restore(b);
                status.ForeColor = res != null ? Theme.Accent : Theme.Warning;
                status.Text = res != null ? Lang.T("Восстановлено: ", "Restored: ") + b.Name
                                          : Lang.T("Не удалось восстановить", "Restore failed");
                btn.Enabled = true;
            };
            row.Controls.Add(btn);
            return row;
        }
    }
}
