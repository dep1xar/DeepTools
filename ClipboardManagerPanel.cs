using System;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;

namespace DeepTools
{
    public class ClipboardManagerPanel : Panel
    {
        private ClipboardManager clipboardManager;
        private ListBox historyListBox;
        private TextBox searchBox;
        private Label statusLabel;
        private PictureBox preview;
        private Label previewLabel;
        // Что сейчас реально показано в списке (с учётом поиска) - по нему же
        // работают Восстановить/Удалить, чтобы индексы не разъезжались
        private System.Collections.Generic.List<ClipboardEntry> displayed =
            new System.Collections.Generic.List<ClipboardEntry>();

        public ClipboardManagerPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            clipboardManager = new ClipboardManager();
            clipboardManager.HistoryChanged += (s, e) => RefreshHistory();

            BuildUi();
            clipboardManager.Start();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("📋 Буфер обмена", "📋 Clipboard"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            // Поле поиска
            var searchLabel = new Label
            {
                Text = Lang.T("Поиск", "Search"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(24, 50),
                AutoSize = true
            };
            Controls.Add(searchLabel);

            searchBox = new TextBox
            {
                Location = new Point(24, 70),
                Size = new Size(712, 28),
                Font = new Font("Segoe UI", 10F),
                BackColor = Theme.InputColor,
                ForeColor = Theme.TextMain,
                BorderStyle = BorderStyle.FixedSingle
            };
            searchBox.TextChanged += (s, e) => SearchHistory();
            Controls.Add(searchBox);

            // Карточка с историей
            var card = Theme.MakeCard(this, new Point(24, 110), new Size(712, 460));

            historyListBox = new ListBox
            {
                Location = new Point(12, 12),
                Size = new Size(508, 380),
                BackColor = Theme.SidebarColor,
                ForeColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9.5F),
                BorderStyle = BorderStyle.None,
                SelectionMode = SelectionMode.One
            };
            historyListBox.DoubleClick += (s, e) => RestoreSelected();
            historyListBox.SelectedIndexChanged += (s, e) => UpdatePreview();
            card.Controls.Add(historyListBox);
            NativeMethods.ApplyDarkScrollbar(historyListBox);

            preview = new PictureBox
            {
                Location = new Point(532, 12),
                Size = new Size(168, 168),
                BackColor = Theme.SidebarColor,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };
            card.Controls.Add(preview);

            previewLabel = new Label
            {
                Text = Lang.T("Превью картинки", "Image preview"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(532, 184),
                Size = new Size(168, 16),
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(previewLabel);

            // Кнопки управления
            var btnRestore = new RoundedButton
            {
                Text = Lang.T("↩️ Восстановить", "↩️ Restore"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(12, 400),
                Size = new Size(105, 32)
            };
            btnRestore.Click += (s, e) => RestoreSelected();
            card.Controls.Add(btnRestore);

            var btnDelete = new RoundedButton
            {
                Text = Lang.T("🗑️ Удалить", "🗑️ Delete"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(125, 400),
                Size = new Size(105, 32)
            };
            btnDelete.Click += (s, e) => DeleteSelected();
            card.Controls.Add(btnDelete);

            var btnPin = new RoundedButton
            {
                Text = Lang.T("📌 Закрепить", "📌 Pin"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(238, 400),
                Size = new Size(110, 32)
            };
            btnPin.Click += (s, e) => PinSelected();
            card.Controls.Add(btnPin);

            var btnClear = new RoundedButton
            {
                Text = Lang.T("🧹 Очистить всё", "🧹 Clear all"),
                ButtonColor = Theme.Danger,
                HoverColor = Theme.DangerHover,
                TextColor = Theme.BgColor,
                Location = new Point(570, 400),
                Size = new Size(106, 32)
            };
            btnClear.Click += (s, e) => ClearAll();
            card.Controls.Add(btnClear);

            statusLabel = new Label
            {
                Text = Lang.T("Готово", "Ready"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 580),
                AutoSize = true
            };
            Controls.Add(statusLabel);

            RefreshHistory();
        }

        private void RefreshHistory()
        {
            // Если в поиске что-то есть - показываем результаты поиска, иначе всю историю
            string query = searchBox == null ? "" : searchBox.Text;
            if (!string.IsNullOrWhiteSpace(query))
            {
                SearchHistory();
                return;
            }

            displayed = clipboardManager.GetHistory();
            FillListBox();
            statusLabel.Text = Lang.T("Элементов: ", "Items: ") + displayed.Count.ToString();
        }

        private void SearchHistory()
        {
            string query = searchBox.Text;

            if (string.IsNullOrWhiteSpace(query))
            {
                RefreshHistory();
                return;
            }

            displayed = clipboardManager.Search(query);
            FillListBox();
            statusLabel.Text = Lang.T("Найдено: ", "Found: ") + displayed.Count.ToString();
        }

        // Единственное место, которое наполняет ListBox из displayed - индексы всегда совпадают
        private void FillListBox()
        {
            historyListBox.Items.Clear();
            foreach (var entry in displayed)
            {
                string icon = entry.IsPinned ? "📌 " : "";
                string typeIcon = entry.Type == "text" ? "📄 " : "🖼️ ";
                string timeStr = entry.Timestamp.ToString("HH:mm:ss");
                historyListBox.Items.Add(icon + typeIcon + entry.DisplayText + " [" + timeStr + "]");
            }
        }

        private void RestoreSelected()
        {
            int idx = historyListBox.SelectedIndex;
            if (idx < 0 || idx >= displayed.Count)
            {
                MessageBox.Show(Lang.T("Выбери элемент из истории", "Select an item from history"), Lang.T("Буфер обмена", "Clipboard"));
                return;
            }

            clipboardManager.RestoreEntry(displayed[idx].Id);
            statusLabel.Text = Lang.T("✓ Восстановлено в буфер обмена", "✓ Restored to clipboard");
        }

        private void DeleteSelected()
        {
            int idx = historyListBox.SelectedIndex;
            if (idx < 0 || idx >= displayed.Count) return;

            clipboardManager.RemoveEntry(displayed[idx].Id);
            statusLabel.Text = Lang.T("✓ Удалено из истории", "✓ Deleted from history");
        }

        // Показывает превью для выбранной картинки, иначе очищает область
        private void UpdatePreview()
        {
            if (preview.Image != null) { preview.Image.Dispose(); preview.Image = null; }

            int idx = historyListBox.SelectedIndex;
            if (idx < 0 || idx >= displayed.Count) return;

            var entry = displayed[idx];
            if (entry.Type == "image" && System.IO.File.Exists(entry.Content))
            {
                try
                {
                    using (var tmp = Image.FromFile(entry.Content))
                        preview.Image = new Bitmap(tmp);
                    previewLabel.Text = Lang.T("Картинка (двойной клик — в буфер)", "Image (double-click to copy)");
                }
                catch { previewLabel.Text = Lang.T("Не удалось открыть", "Failed to open"); }
            }
            else
            {
                previewLabel.Text = Lang.T("Превью картинки", "Image preview");
            }
        }

        private void PinSelected()
        {
            int idx = historyListBox.SelectedIndex;
            if (idx < 0 || idx >= displayed.Count) return;

            clipboardManager.PinEntry(displayed[idx].Id);
            statusLabel.Text = Lang.T("✓ Закрепление изменено", "✓ Pin toggled");
        }

        private void ClearAll()
        {
            var result = MessageBox.Show(Lang.T("Очистить всю историю буфера обмена?", "Clear entire clipboard history?"), Lang.T("Подтверждение", "Confirmation"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                clipboardManager.ClearHistory();
                statusLabel.Text = Lang.T("✓ История очищена", "✓ History cleared");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                clipboardManager.Stop();
            }
            base.Dispose(disposing);
        }
    }
}
