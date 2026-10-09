using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    public class VisualEffectsPanel : Panel
    {
        private Label statusLabel;

        private ToggleSwitch transparencyToggle;
        private ToggleSwitch taskbarAnimToggle;
        private ToggleSwitch windowAnimToggle;
        private ToggleSwitch shadowToggle;
        private ToggleSwitch dragFullToggle;

        public VisualEffectsPanel()
        {
            Size = new Size(760, 616);
            BackColor = Theme.BgColor;

            BuildUi();
            LoadCurrentState();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("Визуальные эффекты", "Visual effects"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var hintLabel = new Label
            {
                Text = Lang.T("Отключение анимаций и эффектов немного снижает нагрузку на систему. Часть изменений применяется сразу, часть - после перезапуска проводника.", "Disabling animations and effects slightly reduces system load. Some changes apply immediately, others after restarting Explorer."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 50),
                Size = new Size(700, 30)
            };
            Controls.Add(hintLabel);

            var card = Theme.MakeCard(this, new Point(24, 90), new Size(712, 300));

            transparencyToggle = MakeToggleRow(card, Lang.T("Прозрачность интерфейса", "UI transparency"), Lang.T("Панель задач, меню Пуск и окна", "Taskbar, Start menu and windows"), 20);
            taskbarAnimToggle = MakeToggleRow(card, Lang.T("Анимации панели задач", "Taskbar animations"), Lang.T("Эффекты открытия и сворачивания приложений", "App open and minimize effects"), 76);
            windowAnimToggle = MakeToggleRow(card, Lang.T("Анимации окон", "Window animations"), Lang.T("Плавное сворачивание/разворачивание окон", "Smooth window minimize/restore"), 132);
            shadowToggle = MakeToggleRow(card, Lang.T("Тени значков в проводнике", "Explorer icon shadows"), Lang.T("Тени под подписями иконок на рабочем столе", "Shadows under desktop icon labels"), 188);
            dragFullToggle = MakeToggleRow(card, Lang.T("Отрисовка содержимого при перетаскивании", "Show window contents while dragging"), Lang.T("Показывать содержимое окна, а не только рамку, во время перетаскивания", "Show the full window instead of just a frame while dragging"), 244);

            var actionsCard = Theme.MakeCard(this, new Point(24, 404), new Size(712, 100));

            var maxPerfBtn = new RoundedButton
            {
                Text = Lang.T("Максимальная производительность", "Maximum performance"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Location = new Point(18, 16),
                Size = new Size(260, 34)
            };
            maxPerfBtn.Click += (s, e) => ApplyPreset(false);
            actionsCard.Controls.Add(maxPerfBtn);

            var restoreBtn = new RoundedButton
            {
                Text = Lang.T("Вернуть эффекты", "Restore effects"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(290, 16),
                Size = new Size(180, 34)
            };
            restoreBtn.Click += (s, e) => ApplyPreset(true);
            actionsCard.Controls.Add(restoreBtn);

            var restartExplorerBtn = new RoundedButton
            {
                Text = Lang.T("Перезапустить проводник", "Restart Explorer"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Location = new Point(482, 16),
                Size = new Size(210, 34)
            };
            restartExplorerBtn.Click += (s, e) => RestartExplorer();
            actionsCard.Controls.Add(restartExplorerBtn);

            var actionsHint = new Label
            {
                Text = Lang.T("Перезапуск проводника закроет и переоткроет рабочий стол и панель задач (окна программ не закроются)", "Restarting Explorer will close and reopen the desktop and taskbar (app windows will stay open)"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(18, 58),
                Size = new Size(670, 30)
            };
            actionsCard.Controls.Add(actionsHint);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 586),
                AutoSize = true
            };
            Controls.Add(statusLabel);
        }

        private ToggleSwitch MakeToggleRow(Control parent, string title, string desc, int y)
        {
            var nameLbl = new Label
            {
                Text = title,
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                Location = new Point(20, y),
                AutoSize = true
            };
            parent.Controls.Add(nameLbl);

            var descLbl = new Label
            {
                Text = desc,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(20, y + 20),
                Size = new Size(560, 18)
            };
            parent.Controls.Add(descLbl);

            var toggle = new ToggleSwitch { Location = new Point(640, y + 6) };
            parent.Controls.Add(toggle);

            toggle.CheckedChanged += (s, e) => ApplyIndividualToggle(toggle);

            return toggle;
        }

        private void ApplyIndividualToggle(ToggleSwitch toggle)
        {
            if (toggle == transparencyToggle) SetTransparency(toggle.Checked);
            else if (toggle == taskbarAnimToggle) SetTaskbarAnimations(toggle.Checked);
            else if (toggle == windowAnimToggle) SetWindowAnimations(toggle.Checked);
            else if (toggle == shadowToggle) SetIconShadows(toggle.Checked);
            else if (toggle == dragFullToggle) SetDragFullWindows(toggle.Checked);

            statusLabel.Text = Lang.T("Изменено. Если эффект не применился сразу - нажми \"Перезапустить проводник\"", "Changed. If the effect did not apply immediately, press Restart Explorer");
            statusLabel.ForeColor = Theme.On;
        }

        private void LoadCurrentState()
        {
            transparencyToggle.Checked = GetDword("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "EnableTransparency", 1) == 1;
            taskbarAnimToggle.Checked = GetDword("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarAnimations", 1) == 1;
            windowAnimToggle.Checked = GetString("Control Panel\\Desktop\\WindowMetrics", "MinAnimate", "1") == "1";
            shadowToggle.Checked = GetDword("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "ListviewShadow", 1) == 1;
            dragFullToggle.Checked = GetString("Control Panel\\Desktop", "DragFullWindows", "1") == "1";

            transparencyToggle.Invalidate();
            taskbarAnimToggle.Invalidate();
            windowAnimToggle.Invalidate();
            shadowToggle.Invalidate();
            dragFullToggle.Invalidate();
        }

        private void ApplyPreset(bool restoreDefaults)
        {
            bool value = restoreDefaults;

            SetTransparency(value);
            SetTaskbarAnimations(value);
            SetWindowAnimations(value);
            SetIconShadows(value);
            SetDragFullWindows(value);

            transparencyToggle.Checked = value;
            taskbarAnimToggle.Checked = value;
            windowAnimToggle.Checked = value;
            shadowToggle.Checked = value;
            dragFullToggle.Checked = value;

            transparencyToggle.Invalidate();
            taskbarAnimToggle.Invalidate();
            windowAnimToggle.Invalidate();
            shadowToggle.Invalidate();
            dragFullToggle.Invalidate();

            statusLabel.Text = restoreDefaults
                ? Lang.T("Эффекты включены обратно. Нажми \"Перезапустить проводник\", если что-то не обновилось", "Effects restored. Press Restart Explorer if something did not update")
                : Lang.T("Применена максимальная производительность. Нажми \"Перезапустить проводник\", если что-то не обновилось", "Maximum performance applied. Press Restart Explorer if something did not update");
            statusLabel.ForeColor = Theme.On;
        }

        private void SetTransparency(bool enabled)
        {
            SetDword("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", "EnableTransparency", enabled ? 1 : 0);
        }

        private void SetTaskbarAnimations(bool enabled)
        {
            SetDword("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarAnimations", enabled ? 1 : 0);
        }

        private void SetWindowAnimations(bool enabled)
        {
            SetString("Control Panel\\Desktop\\WindowMetrics", "MinAnimate", enabled ? "1" : "0");
        }

        private void SetIconShadows(bool enabled)
        {
            SetDword("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "ListviewShadow", enabled ? 1 : 0);
        }

        private void SetDragFullWindows(bool enabled)
        {
            SetString("Control Panel\\Desktop", "DragFullWindows", enabled ? "1" : "0");
        }

        private void RestartExplorer()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("explorer");
                for (int i = 0; i < procs.Length; i++)
                {
                    try { procs[i].Kill(); } catch { }
                }
                System.Threading.Thread.Sleep(500);
                Process.Start("explorer.exe");
                statusLabel.Text = Lang.T("Проводник перезапущен", "Explorer restarted");
                statusLabel.ForeColor = Theme.On;
            }
            catch (Exception ex)
            {
                statusLabel.Text = Lang.T("Не удалось перезапустить проводник: ", "Failed to restart Explorer: ") + ex.Message;
                statusLabel.ForeColor = Theme.Warning;
            }
        }

        private int GetDword(string path, string name, int defaultValue)
        {
            RegistryKey key = null;
            try
            {
                key = Registry.CurrentUser.OpenSubKey(path);
                if (key == null) return defaultValue;
                object val = key.GetValue(name);
                if (val == null) return defaultValue;
                return Convert.ToInt32(val);
            }
            catch
            {
                return defaultValue;
            }
            finally
            {
                if (key != null) key.Close();
            }
        }

        private void SetDword(string path, string name, int value)
        {
            RegistryKey key = null;
            try
            {
                key = Registry.CurrentUser.CreateSubKey(path);
                key.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch
            {
            }
            finally
            {
                if (key != null) key.Close();
            }
        }

        private string GetString(string path, string name, string defaultValue)
        {
            RegistryKey key = null;
            try
            {
                key = Registry.CurrentUser.OpenSubKey(path);
                if (key == null) return defaultValue;
                object val = key.GetValue(name);
                if (val == null) return defaultValue;
                return val.ToString();
            }
            catch
            {
                return defaultValue;
            }
            finally
            {
                if (key != null) key.Close();
            }
        }

        private void SetString(string path, string name, string value)
        {
            RegistryKey key = null;
            try
            {
                key = Registry.CurrentUser.CreateSubKey(path);
                key.SetValue(name, value, RegistryValueKind.String);
            }
            catch
            {
            }
            finally
            {
                if (key != null) key.Close();
            }
        }
    }
}