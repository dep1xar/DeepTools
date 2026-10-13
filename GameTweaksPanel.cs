using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DeepTools
{
    // Игровые твики: сетка карточек 2x5 со статус-барами (зелёный = твик
    // применён, красный = не применён, серый = не применимо на этом ПК).
    //
    // Все значения читаются из реестра, поэтому статус показывает реальное
    // состояние системы, а не «что мы последний раз нажали». Клик по красной
    // карточке включает игровой режим фичи, клик по зелёной возвращает обычное
    // состояние Windows: откат никогда не оставляет карточку зелёной.
    // Там, где исходное значение важно (план питания, тип запуска служб,
    // размер файла подкачки), оно запоминается при первом применении.
    // Твики, которым нужна перезагрузка, помечены ⟳.
    public class GameTweaksPanel : Panel
    {
        private readonly List<TweakCard> cards = new List<TweakCard>();
        private Label statusLabel;
        private Timer refreshTimer;
        private bool busy;

        public GameTweaksPanel()
        {
            // Без явного размера Panel остаётся дефолтные 200x100 и обрезает
            // всё, что лежит правее/ниже (как остальные панели: 760x616)
            Size = new Size(760, 616);
            Dock = DockStyle.None;
            BackColor = Theme.BgColor;
            BuildUi();

            // Пока раздел открыт, статусы обновляются сами: игра может запуститься
            // или закрыться, а вместе с ней меняется карточка привязки к ядрам
            refreshTimer = new Timer { Interval = 2000 };
            refreshTimer.Tick += (s, e) => { if (!busy) RefreshAll(); };

            VisibleChanged += (s, e) =>
            {
                if (Visible) { RefreshAll(); refreshTimer.Start(); }
                else refreshTimer.Stop();
            };
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("Игровые твики", "Game Tweaks"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 14),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            var subLbl = new Label
            {
                Text = Lang.T("Клик по карточке включает или отключает твик, статус — реальное состояние системы. ⟳ — нужна перезагрузка.",
                              "Click a card to apply or revert a tweak. Status is the real system state. ⟳ — reboot required."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(24, 44),
                Size = new Size(430, 44)
            };
            Controls.Add(subLbl);

            statusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.On,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(440, 14),
                Size = new Size(296, 22),
                TextAlign = ContentAlignment.MiddleRight
            };
            Controls.Add(statusLabel);

            var refreshBtn = new RoundedButton
            {
                Text = Lang.T("Проверить статус", "Check status"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Location = new Point(486, 58),
                Size = new Size(140, 28)
            };
            refreshBtn.Click += (s, e) =>
            {
                RefreshAll();
                SetStatus(Lang.T("Статус обновлён", "Status refreshed"));
            };
            Controls.Add(refreshBtn);

            var revertAllBtn = new RoundedButton
            {
                Text = Lang.T("Отключить всё", "Disable all"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                CornerRadius = 10,
                Location = new Point(636, 58),
                Size = new Size(100, 28)
            };
            revertAllBtn.Click += (s, e) => RevertAll();
            Controls.Add(revertAllBtn);

            // Сетка 2 колонки x 5 строк
            int cardW = 348, cardH = 94, gapX = 16, gapY = 8;
            int x1 = 24, x2 = x1 + cardW + gapX;
            int y0 = 96, stepY = cardH + gapY;

            var defs = BuildDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                int col = i % 2, row = i / 2;
                var card = new TweakCard(defs[i])
                {
                    Location = new Point(col == 0 ? x1 : x2, y0 + row * stepY),
                    Size = new Size(cardW, cardH)
                };
                card.Changed += msg => { RefreshAll(); SetStatus(msg); };
                cards.Add(card);
                Controls.Add(card);
            }
        }

        public void RefreshAll()
        {
            foreach (var c in cards) c.RefreshStatus();
        }

        private void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.Text = text;
        }

        // Возвращает все применённые твики в обычное состояние Windows
        private void RevertAll()
        {
            var applied = new List<TweakCard>();
            foreach (var c in cards) if (c.State == 1) applied.Add(c);

            if (applied.Count == 0)
            {
                SetStatus(Lang.T("Нечего отключать", "Nothing to revert"));
                return;
            }

            var r = DTDialog.Show(
                Lang.T("Отключить все применённые твики (" + applied.Count + ")?",
                       "Revert all applied tweaks (" + applied.Count + ")?"),
                "DeepTools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;

            busy = true;
            int done = 0, failed = 0;
            foreach (var c in applied)
            {
                bool ok;
                try { ok = c.Revert(); }
                catch { ok = false; }
                if (ok) done++; else failed++;
                c.RefreshStatus();
            }
            busy = false;
            RefreshAll();

            SetStatus(failed == 0
                ? Lang.T("Отключено твиков: " + done, "Tweaks off: " + done)
                : Lang.T("Отключено: " + done + ", ошибок: " + failed, "Off: " + done + ", failed: " + failed));
        }

        // ---- Определения твиков ----

        private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
        private const string GameConfigStoreKey = @"System\GameConfigStore";
        private const string MemoryManagementKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
        private const string PowerThrottlingKey = @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
        private const string HvciKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
        private const string GameBarKey = @"Software\Microsoft\GameBar";
        private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services\";
        private const string PowerSchemesKey = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";

        private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        private List<TweakDef> BuildDefs()
        {
            var list = new List<TweakDef>();

            // HwSchMode: 1 = HAGS выключен, 2 = включён. В прошлой версии сюда
            // писался 0 - такого значения Windows не знает, твик ничего не
            // выключал, а карточка при этом становилась зелёной
            list.Add(new TweakDef
            {
                Title = Lang.T("Отключить HAGS", "Disable HAGS"),
                Desc = Lang.T("Аппаратное планирование GPU. Влияет по-разному: часть игр получает FPS, часть теряет",
                              "Hardware GPU scheduling. Mixed results: some games gain FPS, some lose it"),
                Dangerous = true,
                NeedsReboot = true,
                ConfirmRu = "Отключение HAGS меняет планирование GPU и требует перезагрузки. Применить?",
                ConfirmEn = "Disabling HAGS changes GPU scheduling and needs a reboot. Apply?",
                IsApplicable = () => true,
                IsApplied = () => RegDword(RegHklm, GraphicsDriversKey, "HwSchMode") == 1,
                Apply = () =>
                {
                    BackupDword(RegHklm, GraphicsDriversKey, "HwSchMode", "gt_hags");
                    return SetRegDword(RegHklm, GraphicsDriversKey, "HwSchMode", 1);
                },
                Revert = () => RevertDword(RegHklm, GraphicsDriversKey, "HwSchMode", "gt_hags", 1, 2)
            });

            // Game Mode в Windows включён по умолчанию, поэтому «выключен» - это
            // только явный 0, а отсутствие значения считается включённым
            list.Add(new TweakDef
            {
                Title = Lang.T("Включить Game Mode", "Enable Game Mode"),
                Desc = Lang.T("Приоритет активной игре: меньше фоновых задач и микрофризов. В Windows он включён по умолчанию",
                              "Gives the active game priority: fewer background tasks, fewer micro-stutters. On by default in Windows"),
                IsApplicable = () => true,
                IsApplied = () => RegDword(RegHkcu, GameBarKey, "AutoGameModeEnabled") != 0,
                Apply = () =>
                {
                    bool a = SetRegDword(RegHkcu, GameBarKey, "AutoGameModeEnabled", 1);
                    bool b = SetRegDword(RegHkcu, GameBarKey, "AllowAutoGameMode", 1);
                    return a && b;
                },
                Revert = () =>
                {
                    bool a = SetRegDword(RegHkcu, GameBarKey, "AutoGameModeEnabled", 0);
                    bool b = SetRegDword(RegHkcu, GameBarKey, "AllowAutoGameMode", 0);
                    return a && b;
                }
            });

            // Глобальный FSO: одного GameDVR_FSEBehaviorMode мало, Windows также
            // смотрит HonorUserFSEBehaviorMode и DXGIHonorFSEWindowsCompatible
            list.Add(new TweakDef
            {
                Title = Lang.T("Отключить Fullscreen Optimizations", "Disable Fullscreen Optimizations"),
                Desc = Lang.T("Убирает прослойку DWM в полноэкранном режиме: меньше задержка, стабильнее кадры",
                              "Removes the DWM layer in fullscreen: lower latency, steadier frames"),
                IsApplicable = () => true,
                IsApplied = () => RegDword(RegHkcu, GameConfigStoreKey, "GameDVR_FSEBehaviorMode") == 2,
                Apply = () =>
                {
                    BackupDword(RegHkcu, GameConfigStoreKey, "GameDVR_FSEBehaviorMode", "gt_fso_mode");
                    BackupDword(RegHkcu, GameConfigStoreKey, "GameDVR_FSEBehavior", "gt_fso_behavior");
                    BackupDword(RegHkcu, GameConfigStoreKey, "GameDVR_HonorUserFSEBehaviorMode", "gt_fso_honor");
                    BackupDword(RegHkcu, GameConfigStoreKey, "GameDVR_DXGIHonorFSEWindowsCompatible", "gt_fso_dxgi");
                    bool a = SetRegDword(RegHkcu, GameConfigStoreKey, "GameDVR_FSEBehaviorMode", 2);
                    bool b = SetRegDword(RegHkcu, GameConfigStoreKey, "GameDVR_FSEBehavior", 2);
                    bool c = SetRegDword(RegHkcu, GameConfigStoreKey, "GameDVR_HonorUserFSEBehaviorMode", 1);
                    bool d = SetRegDword(RegHkcu, GameConfigStoreKey, "GameDVR_DXGIHonorFSEWindowsCompatible", 1);
                    return a && b && c && d;
                },
                Revert = () =>
                {
                    bool a = RevertDword(RegHkcu, GameConfigStoreKey, "GameDVR_FSEBehaviorMode", "gt_fso_mode", 2, 0);
                    bool b = RevertDword(RegHkcu, GameConfigStoreKey, "GameDVR_FSEBehavior", "gt_fso_behavior", 2, 0);
                    bool c = RevertDword(RegHkcu, GameConfigStoreKey, "GameDVR_HonorUserFSEBehaviorMode", "gt_fso_honor", 1, 0);
                    bool d = RevertDword(RegHkcu, GameConfigStoreKey, "GameDVR_DXGIHonorFSEWindowsCompatible", "gt_fso_dxgi", 1, 0);
                    return a && b && c && d;
                }
            });

            list.Add(new TweakDef
            {
                Title = Lang.T("CPU Affinity активной игры", "CPU Affinity for active game"),
                Desc = Lang.T("Отвязывает игру от первого ядра: меньше конкуренции с системой и прерываниями",
                              "Keeps the game off CPU0: less contention with the OS and interrupts"),
                IsApplicable = () => GameBoosterPanel.DetectedGame != null,
                IsApplied = () =>
                {
                    var p = GameBoosterPanel.DetectedGame;
                    if (p == null) return false;
                    try { return (p.ProcessorAffinity.ToInt64() & 1L) == 0; }
                    catch { return false; }
                },
                Apply = () =>
                {
                    var p = GameBoosterPanel.DetectedGame;
                    if (p == null) return false;
                    try
                    {
                        long current = p.ProcessorAffinity.ToInt64();
                        long mask = current & ~1L;
                        // В игре осталось одно ядро - снимать с него CPU0 нельзя
                        if (mask == 0) return false;
                        AppConfig.Set("gt_affinity_mask", current.ToString());
                        p.ProcessorAffinity = (IntPtr)mask;
                        return true;
                    }
                    catch { return false; }
                },
                Revert = () =>
                {
                    var p = GameBoosterPanel.DetectedGame;
                    if (p == null) return false;
                    try
                    {
                        long mask;
                        if (!long.TryParse(AppConfig.Get("gt_affinity_mask", ""), out mask) || mask == 0)
                            mask = FullMask();
                        p.ProcessorAffinity = (IntPtr)mask;
                        return true;
                    }
                    catch { return false; }
                }
            });

            // Файл подкачки меняется только со следующей загрузки. Откат раньше
            // жёстко ставил «1024 4096» даже если у пользователя был свой размер,
            // поэтому теперь исходный размер запоминается
            list.Add(new TweakDef
            {
                Title = Lang.T("Авто-файл подкачки", "Auto page file"),
                Desc = Lang.T("Отдаёт размер файла подкачки системе: меньше крашей и фризов при нехватке RAM",
                              "Lets Windows size the page file: fewer crashes and stutters when RAM runs short"),
                NeedsReboot = true,
                IsApplicable = () => true,
                IsApplied = () => PagingFilesManaged(),
                Apply = () =>
                {
                    BackupMultiSz(RegHklm, MemoryManagementKey, "PagingFiles", "gt_pagefile");
                    return SetSystemDrivePagefile("0 0");
                },
                Revert = () =>
                {
                    // Если до нас размер был задан вручную - возвращаем именно его
                    string raw = AppConfig.Get("gt_pagefile", "none");
                    if (raw != "none")
                    {
                        var saved = new List<string>();
                        foreach (string s in raw.Split('|')) if (s != "") saved.Add(s);
                        if (saved.Count > 0 && !AllManaged(saved))
                            return SetRegMultiSz(RegHklm, MemoryManagementKey, "PagingFiles", saved.ToArray());
                    }
                    // Иначе просто выключаем авто-режим: фиксированные 1-4 ГБ
                    return SetSystemDrivePagefile("1024 4096");
                }
            });

            list.Add(new TweakDef
            {
                Title = Lang.T("Сбалансированный план питания", "Balanced power plan"),
                Desc = Lang.T("Стабильные частоты и меньше задержек планировщика, чем в «Экономии»",
                              "Steady clocks and lower scheduler latency than Power Saver"),
                IsApplicable = () => ActiveSchemeGuid() != "",
                IsApplied = () => ActiveSchemeGuid() == BalancedGuid,
                Apply = () =>
                {
                    string cur = ActiveSchemeGuid();
                    if (cur != "" && cur != BalancedGuid) AppConfig.Set("gt_prev_plan", cur);
                    return RunPowercfg("/setactive " + BalancedGuid);
                },
                // Откат возвращает план, который был активен до нас, а не жёстко
                // «Высокую производительность»
                Revert = () =>
                {
                    string prev = AppConfig.Get("gt_prev_plan", "");
                    if (prev != "" && prev != BalancedGuid && SchemeExists(prev))
                        return RunPowercfg("/setactive " + prev);
                    return RunPowercfg("/setactive " + HighPerfGuid);
                }
            });

            list.Add(new TweakDef
            {
                Title = Lang.T("Отключить PCIe LPM (ASPM)", "Disable PCIe LPM (ASPM)"),
                Desc = Lang.T("Убирает энергосбережение шины: меньше задержки к GPU и NVMe",
                              "Turns off link power management: lower latency to GPU and NVMe"),
                IsApplicable = () => ActiveSchemeGuid() != "",
                // Проверяем и питание от сети, и от батареи: раньше смотрели только AC
                IsApplied = () => AspmValue("ACSettingIndex") == 0 && AspmValue("DCSettingIndex") == 0,
                Apply = () =>
                {
                    string g = ActiveSchemeGuid();
                    if (g == "") return false;
                    BackupDword(RegHklm, AspmKey(g), "ACSettingIndex", "gt_aspm_ac");
                    BackupDword(RegHklm, AspmKey(g), "DCSettingIndex", "gt_aspm_dc");
                    bool a = RunPowercfg("/setacvalueindex " + g + " SUB_PCIEXPRESS PCIEXPRESS_ASPM 0");
                    bool b = RunPowercfg("/setdcvalueindex " + g + " SUB_PCIEXPRESS PCIEXPRESS_ASPM 0");
                    bool c = RunPowercfg("/setactive " + g);
                    return a && b && c;
                },
                Revert = () =>
                {
                    string g = ActiveSchemeGuid();
                    if (g == "") return false;
                    bool a = RestorePowerIndex(g, "ACSettingIndex", "gt_aspm_ac");
                    bool b = RestorePowerIndex(g, "DCSettingIndex", "gt_aspm_dc");
                    RunPowercfg("/setactive " + g);
                    return a && b;
                }
            });

            list.Add(new TweakDef
            {
                Title = Lang.T("Отключить лишние службы", "Disable unneeded services"),
                Desc = Lang.T("Останавливает телеметрию и геолокацию: меньше фонового CPU. Откатывается",
                              "Stops telemetry and location services: less background CPU. Revertible"),
                IsApplicable = () => ExistingServices().Count > 0,
                IsApplied = () =>
                {
                    var found = ExistingServices();
                    if (found.Count == 0) return false;
                    foreach (string name in found)
                        if (ServiceStart(name) != 4) return false;
                    return true;
                },
                Apply = () =>
                {
                    BackupServices();
                    bool ok = true;
                    foreach (string name in ExistingServices())
                        if (!SetServiceStart(name, 4)) ok = false;
                    return ok;
                },
                Revert = () => RestoreServices()
            });

            list.Add(new TweakDef
            {
                Title = Lang.T("Отключить HVCI", "Disable HVCI"),
                Desc = Lang.T("Memory Integrity ест 5-15% FPS. Отключение снижает защиту от эксплойтов",
                              "Memory Integrity costs 5-15% FPS. Disabling lowers exploit protection"),
                Dangerous = true,
                NeedsReboot = true,
                ConfirmRu = "HVCI (Memory Integrity) защищает ядро от эксплойтов. Отключение снижает защиту и требует перезагрузки. Применить?",
                ConfirmEn = "HVCI (Memory Integrity) protects the kernel from exploits. Disabling lowers protection and needs a reboot. Apply?",
                IsApplicable = () => true,
                IsApplied = () => RegDword(RegHklm, HvciKey, "Enabled") == 0,
                Apply = () =>
                {
                    BackupDword(RegHklm, HvciKey, "Enabled", "gt_hvci");
                    return SetRegDword(RegHklm, HvciKey, "Enabled", 0);
                },
                Revert = () => RevertDword(RegHklm, HvciKey, "Enabled", "gt_hvci", 0, 1)
            });

            list.Add(new TweakDef
            {
                Title = Lang.T("Отключить Power Throttling", "Disable Power Throttling"),
                Desc = Lang.T("Windows перестаёт занижать частоты фоновых потоков: лучше 1% low в играх",
                              "Stops Windows clocking down background threads: better 1% low in games"),
                IsApplicable = () => true,
                IsApplied = () => RegDword(RegHklm, PowerThrottlingKey, "PowerThrottlingOff") == 1,
                Apply = () =>
                {
                    BackupDword(RegHklm, PowerThrottlingKey, "PowerThrottlingOff", "gt_powert");
                    return SetRegDword(RegHklm, PowerThrottlingKey, "PowerThrottlingOff", 1);
                },
                Revert = () => RevertDword(RegHklm, PowerThrottlingKey, "PowerThrottlingOff", "gt_powert", 1, 0)
            });

            return list;
        }

        // ---- Карточка ----

        private class TweakCard : Panel
        {
            public event Action<string> Changed;
            private readonly TweakDef def;
            private readonly Label titleLbl, descLbl;
            private readonly StatusPill pill;
            private int state; // 0 = не применено, 1 = применено, 2 = не применимо
            private bool working;

            public int State { get { return state; } }

            public TweakCard(TweakDef d)
            {
                def = d;
                BackColor = Theme.CardColor;
                Cursor = Cursors.Hand;
                DoubleBuffered = true;

                Paint += (s, e) =>
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                    using (var path = RoundedRect(rect, 12))
                    using (var brush = new SolidBrush(Theme.CardColor))
                    using (var pen = new Pen(Theme.BorderColor))
                    {
                        e.Graphics.FillPath(brush, path);
                        e.Graphics.DrawPath(pen, path);
                    }
                };

                titleLbl = new Label
                {
                    Text = d.NeedsReboot ? d.Title + "  ⟳" : d.Title,
                    ForeColor = Theme.TextMain,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    Location = new Point(16, 14),
                    Cursor = Cursors.Hand
                };
                descLbl = new Label
                {
                    Text = d.Desc,
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 8.5F),
                    Location = new Point(16, 38),
                    Cursor = Cursors.Hand
                };
                pill = new StatusPill { Size = new Size(30, 44), Cursor = Cursors.Hand };

                Controls.Add(titleLbl);
                Controls.Add(descLbl);
                Controls.Add(pill);

                Click += OnClickCard;
                titleLbl.Click += OnClickCard;
                descLbl.Click += OnClickCard;
                pill.Click += OnClickCard;
            }

            // Размер карточки задаётся снаружи после конструктора, поэтому
            // раскладку детей пересчитываем здесь
            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (titleLbl != null) titleLbl.Size = new Size(Width - 76, 20);
                if (descLbl != null) descLbl.Size = new Size(Width - 76, Math.Max(20, Height - 46));
                if (pill != null) pill.Location = new Point(Width - 42, (Height - 44) / 2);
            }

            public void RefreshStatus()
            {
                try
                {
                    if (!def.IsApplicable()) state = 2;
                    else state = def.IsApplied() ? 1 : 0;
                }
                catch { state = 2; }
                pill.State = state;
            }

            // Откат без клика - для кнопки «Отключить всё»
            public bool Revert()
            {
                return def.Revert();
            }

            private void OnClickCard(object s, EventArgs e)
            {
                if (working || state == 2) return;

                bool applied = state == 1;
                if (!applied && def.Dangerous)
                {
                    var r = DTDialog.Show(
                        Lang.T(def.ConfirmRu, def.ConfirmEn),
                        "DeepTools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r != DialogResult.Yes) return;
                }

                working = true;
                bool ok;
                try { ok = applied ? def.Revert() : def.Apply(); }
                catch { ok = false; }
                working = false;

                if (!ok)
                {
                    DTDialog.Show(Lang.T("Не удалось применить (нужны права администратора?)",
                                         "Failed to apply (administrator rights needed?)"), "DeepTools");
                }

                RefreshStatus();

                if (Changed != null)
                {
                    string msg = ok
                        ? (applied ? Lang.T("Отключено", "Turned off") : Lang.T("Применено", "Applied"))
                        : Lang.T("Не удалось", "Failed");
                    if (ok && !applied && def.NeedsReboot) msg += Lang.T(" · нужна перезагрузка", " · reboot required");
                    Changed(msg);
                }
            }
        }

        // Статус твика: заливка плюс символ, а не один только цвет. В тёплых темах
        // (rust) акцент оранжевый и почти не отличается от красного - по цвету
        // включённое состояние не прочитать, а по «✓ есть / ✕ нет» читается всегда
        private class StatusPill : Control
        {
            private int _state;
            public int State
            {
                get { return _state; }
                set { _state = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = RoundedRect(rect, 6))
                {
                    if (_state == 1)
                    {
                        using (var brush = new SolidBrush(Theme.On))
                            e.Graphics.FillPath(brush, path);
                        DrawGlyph(e.Graphics, "✓", Theme.BgColor);
                    }
                    else if (_state == 0)
                    {
                        using (var brush = new SolidBrush(Theme.CardColor))
                            e.Graphics.FillPath(brush, path);
                        using (var pen = new Pen(Theme.Off, 2f))
                            e.Graphics.DrawPath(pen, path);
                        DrawGlyph(e.Graphics, "✕", Theme.Off);
                    }
                    else
                    {
                        using (var brush = new SolidBrush(Theme.Idle))
                            e.Graphics.FillPath(brush, path);
                        DrawGlyph(e.Graphics, "—", Theme.TextDim);
                    }
                }
            }

            private void DrawGlyph(Graphics g, string glyph, Color color)
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var f = new Font("Segoe UI", 9.5F, FontStyle.Bold))
                {
                    SizeF sz = g.MeasureString(glyph, f);
                    TextRenderer.DrawText(g, glyph, f, new Rectangle(0, 0, Width, Height), color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ---- Описание твика ----

        private class TweakDef
        {
            public string Title, Desc;
            public bool Dangerous;
            public bool NeedsReboot;
            public string ConfirmRu = "Этот твик снижает защиту системы. Применить?";
            public string ConfirmEn = "This tweak lowers system protection. Apply?";
            public Func<bool> IsApplicable = () => true;
            public Func<bool> IsApplied = () => false;
            public Func<bool> Apply = () => false;
            public Func<bool> Revert = () => false;
        }

        // ---- Реестр / система ----

        private static readonly RegistryKey RegHklm = Registry.LocalMachine;
        private static readonly RegistryKey RegHkcu = Registry.CurrentUser;

        private static int? RegDword(RegistryKey root, string path, string name)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null) return null;
                    object v = k.GetValue(name);
                    return v is int ? (int)v : (int?)null;
                }
            }
            catch { return null; }
        }

        private static bool SetRegDword(RegistryKey root, string path, string name, int value)
        {
            try
            {
                using (var k = root.CreateSubKey(path))
                {
                    if (k == null) return false;
                    k.SetValue(name, value, RegistryValueKind.DWord);
                }
                return true;
            }
            catch { return false; }
        }

        private static bool DelRegValue(RegistryKey root, string path, string name)
        {
            try
            {
                using (var k = root.OpenSubKey(path, true))
                {
                    if (k == null) return true; // ключа нет - убирать нечего
                    k.DeleteValue(name, false);
                }
                return true;
            }
            catch { return false; }
        }

        private static string[] RegMultiSz(RegistryKey root, string path, string name)
        {
            try
            {
                using (var k = root.OpenSubKey(path))
                {
                    if (k == null) return null;
                    return k.GetValue(name) as string[];
                }
            }
            catch { return null; }
        }

        private static bool SetRegMultiSz(RegistryKey root, string path, string name, string[] value)
        {
            try
            {
                using (var k = root.CreateSubKey(path))
                {
                    if (k == null) return false;
                    k.SetValue(name, value, RegistryValueKind.MultiString);
                }
                return true;
            }
            catch { return false; }
        }

        // Запоминаем исходное значение при первом применении твика
        private static void BackupDword(RegistryKey root, string path, string name, string key)
        {
            int? v = RegDword(root, path, name);
            AppConfig.Set(key, v.HasValue ? v.Value.ToString() : "none");
        }

        // Откат: если исходное значение известно и отличается от того, что ставит
        // твик, возвращаем его. Если значения не было - убираем наше (Windows
        // вернётся к дефолту). Если в бэкапе ровно «применённое» состояние -
        // ставим обычное, иначе карточка осталась бы зелёной после отката
        private static bool RevertDword(RegistryKey root, string path, string name, string key, int appliedValue, int fallback)
        {
            string raw = AppConfig.Get(key, "none");
            int v;
            if (raw == "none" || !int.TryParse(raw, out v)) return DelRegValue(root, path, name);
            if (v == appliedValue) return SetRegDword(root, path, name, fallback);
            return SetRegDword(root, path, name, v);
        }

        private static void BackupMultiSz(RegistryKey root, string path, string name, string key)
        {
            string[] v = RegMultiSz(root, path, name);
            AppConfig.Set(key, v == null ? "none" : string.Join("|", v));
        }

        private static string ActiveSchemeGuid()
        {
            try
            {
                using (var k = RegHklm.OpenSubKey(PowerSchemesKey))
                {
                    if (k == null) return "";
                    string g = k.GetValue("ActivePowerScheme") as string;
                    return g == null ? "" : g.ToLowerInvariant();
                }
            }
            catch { return ""; }
        }

        private static bool SchemeExists(string guid)
        {
            try
            {
                using (var k = RegHklm.OpenSubKey(PowerSchemesKey + "\\" + guid)) return k != null;
            }
            catch { return false; }
        }

        // Схема -> подгруппа PCI Express -> настройка «Link State Power Management»
        private const string PcieSub = @"501a4d13-42af-4429-9fd1-a8218c268e20\ee12f906-d277-404b-b6da-e5fa1a576df5";

        private static string AspmKey(string schemeGuid)
        {
            return PowerSchemesKey + "\\" + schemeGuid + "\\" + PcieSub;
        }

        private static int? AspmValue(string name)
        {
            string g = ActiveSchemeGuid();
            if (g == "") return null;
            return RegDword(RegHklm, AspmKey(g), name);
        }

        // Возврат индекса питания: 0 = ASPM выключен (наш твик), поэтому обычное
        // состояние - 2 («максимальная экономия»), но если у пользователя было
        // своё значение, возвращаем его
        private static bool RestorePowerIndex(string schemeGuid, string name, string key)
        {
            string raw = AppConfig.Get(key, "none");
            int v = 2;
            if (raw != "none")
            {
                int saved;
                if (int.TryParse(raw, out saved) && saved != 0) v = saved;
            }
            bool ac = name == "ACSettingIndex";
            return RunPowercfg((ac ? "/setacvalueindex " : "/setdcvalueindex ") + schemeGuid +
                               " SUB_PCIEXPRESS PCIEXPRESS_ASPM " + v);
        }

        private static bool RunPowercfg(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    // Вывод надо вычитать, иначе powercfg может упереться в
                    // переполненный пайп, а код возврата отличает «настройки нет
                    // на этом ПК» от успеха: раньше результат игнорировался и
                    // карточка зеленела просто так
                    p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return false; }
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private static long FullMask()
        {
            int n = Environment.ProcessorCount;
            if (n <= 0) return 1;
            if (n >= 64) return -1L;
            return (1L << n) - 1;
        }

        private static string SystemDrive()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root)) return "C:";
                return root.TrimEnd('\\');
            }
            catch { return "C:"; }
        }

        // Переписывает запись системного тома в PagingFiles, оставляя остальные
        // диски как есть. spec - «0 0» (размер по выбору системы) или «1024 4096»
        private static bool SetSystemDrivePagefile(string spec)
        {
            string os = SystemDrive();
            string[] cur = RegMultiSz(RegHklm, MemoryManagementKey, "PagingFiles");
            var next = new List<string>();
            bool replaced = false;
            if (cur != null)
            {
                foreach (string entry in cur)
                {
                    string e = (entry ?? "").Trim();
                    if (e == "") continue;
                    if (!replaced && e.StartsWith(os, StringComparison.OrdinalIgnoreCase))
                    {
                        next.Add(os + @"\pagefile.sys " + spec);
                        replaced = true;
                    }
                    else next.Add(e);
                }
            }
            if (!replaced) next.Add(os + @"\pagefile.sys " + spec);
            return SetRegMultiSz(RegHklm, MemoryManagementKey, "PagingFiles", next.ToArray());
        }

        // «Размер по выбору системы» = у каждой записи PagingFiles хвост «0 0»
        private static bool PagingFilesManaged()
        {
            string[] v = RegMultiSz(RegHklm, MemoryManagementKey, "PagingFiles");
            if (v == null || v.Length == 0) return false;
            return AllManaged(v);
        }

        private static bool AllManaged(IEnumerable<string> entries)
        {
            bool any = false;
            foreach (string entry in entries)
            {
                string[] parts = (entry ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) return false; // путь + начальный + максимальный размер
                if (parts[parts.Length - 2] != "0" || parts[parts.Length - 1] != "0") return false;
                any = true;
            }
            return any;
        }

        // ---- Службы ----

        private static Dictionary<string, string> ServiceList()
        {
            return new Dictionary<string, string>
            {
                { "DiagTrack", "Connected User Experiences and Telemetry" },
                { "dmwappushservice", "WAP Push Message Routing" },
                { "MapsBroker", "Downloaded Maps Manager" },
                { "lfsvc", "Geolocation Service" }
            };
        }

        private static int? ServiceStart(string name)
        {
            return RegDword(RegHklm, ServicesKey + name, "Start");
        }

        // Только те службы, что реально есть в системе: на разных сборках Windows
        // половины списка нет, а попытка «выключить» отсутствующую службу создавала
        // пустой ключ в реестре и карточка навсегда оставалась красной
        private static List<string> ExistingServices()
        {
            var found = new List<string>();
            foreach (var kv in ServiceList())
                if (ServiceStart(kv.Key).HasValue) found.Add(kv.Key);
            return found;
        }

        private static bool SetServiceStart(string name, int start)
        {
            bool ok = SetRegDword(RegHklm, ServicesKey + name, "Start", start);
            try
            {
                var psi = new ProcessStartInfo("sc.exe", (start == 4 ? "stop " : "start ") + name)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        p.StandardOutput.ReadToEnd();
                        if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } }
                    }
                }
            }
            catch { }
            return ok;
        }

        // Сохраняем исходные типы запуска, чтобы откат вернул всё как было
        private static void BackupServices()
        {
            var parts = new List<string>();
            foreach (string name in ExistingServices())
            {
                int? s = ServiceStart(name);
                if (s.HasValue) parts.Add(name + "=" + s.Value);
            }
            AppConfig.Set("gt_svc_backup", string.Join(";", parts.ToArray()));
        }

        // Возвращает true только если типы запуска реально записались: иначе
        // карточка рапортовала успешный откат, оставаясь при этом выключенной
        private static bool RestoreServices()
        {
            string raw = AppConfig.Get("gt_svc_backup", "");
            bool any = false, ok = true;
            if (raw != "")
            {
                foreach (string part in raw.Split(';'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    int start;
                    if (!int.TryParse(part.Substring(eq + 1), out start)) continue;
                    string name = part.Substring(0, eq);
                    // Служба могла исчезнуть вместе с обновлением Windows
                    if (!ServiceStart(name).HasValue) continue;
                    if (!SetServiceStart(name, start)) ok = false;
                    any = true;
                }
                AppConfig.Set("gt_svc_backup", "");
            }
            if (any) return ok;

            // Бэкапа нет - возвращаем дефолтные типы запуска Windows
            ok = true;
            foreach (string name in ExistingServices())
            {
                int def = (name == "lfsvc" || name == "MapsBroker") ? 3 : 2;
                if (!SetServiceStart(name, def)) ok = false;
            }
            return ok;
        }
    }
}
