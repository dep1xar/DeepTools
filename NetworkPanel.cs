using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeepTools
{
    // График скорости сети с автоматической шкалой (в отличие от LoadGraph,
    // где шкала фиксирована 0-100%). Максимум подбирается по пику в истории
    public class NetGraph : Panel
    {
        private List<float> history = new List<float>();
        public int MaxPoints = 60;
        public Color LineColor = Theme.Accent;

        public NetGraph()
        {
            BackColor = Theme.SidebarColor;
            DoubleBuffered = true;
        }

        public void AddPoint(float value)
        {
            history.Add(Math.Max(0, value));
            if (history.Count > MaxPoints) history.RemoveAt(0);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle plot = new Rectangle(6, 6, Width - 12, Height - 12);
            if (plot.Width <= 0 || plot.Height <= 0) return;

            using (var gridPen = new Pen(Theme.BorderColor))
            {
                for (int i = 1; i < 4; i++)
                {
                    int y = plot.Bottom - plot.Height * i / 4;
                    g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
                }
            }

            if (history.Count < 2 || MaxPoints < 2) return;

            // Автошкала: максимум графика = пик истории, но не меньше 100 КБ/с,
            // чтобы тишина в сети не рисовала шум во весь рост
            float max = 100 * 1024;
            for (int i = 0; i < history.Count; i++) if (history[i] > max) max = history[i];

            float stepX = plot.Width / (float)(MaxPoints - 1);
            PointF[] points = new PointF[history.Count];
            for (int i = 0; i < history.Count; i++)
            {
                float x = plot.Left + i * stepX;
                float y = plot.Bottom - history[i] / max * plot.Height;
                points[i] = new PointF(x, y);
            }

            using (GraphicsPath areaPath = new GraphicsPath())
            {
                areaPath.AddLine(points[0].X, plot.Bottom, points[0].X, points[0].Y);
                for (int i = 1; i < points.Length; i++) areaPath.AddLine(points[i - 1], points[i]);
                areaPath.AddLine(points[points.Length - 1].X, points[points.Length - 1].Y, points[points.Length - 1].X, plot.Bottom);
                areaPath.CloseFigure();
                using (var areaBrush = new SolidBrush(Color.FromArgb(40, LineColor)))
                {
                    g.FillPath(areaBrush, areaPath);
                }
            }

            using (var linePen = new Pen(LineColor, 2))
            {
                for (int i = 1; i < points.Length; i++) g.DrawLine(linePen, points[i - 1], points[i]);
            }
        }
    }

    // Трафик по процессам через TCP EStats: включаем сбор статистики на каждом
    // TCP-соединении (нужны права администратора - они у программы есть) и
    // суммируем байты по PID. Если EStats недоступен - остаётся счётчик соединений
    public static class NetTraffic
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen,
            bool sort, int ipVersion, int tableClass, uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        public struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
        }

        [DllImport("iphlpapi.dll")]
        private static extern uint SetPerTcpConnectionEStats(ref MIB_TCPROW row, int estatsType,
            byte[] rw, uint rwVersion, uint rwSize, uint offset);

        [DllImport("iphlpapi.dll")]
        private static extern uint GetPerTcpConnectionEStats(ref MIB_TCPROW row, int estatsType,
            IntPtr rw, uint rwVersion, uint rwSize,
            IntPtr ros, uint rosVersion, uint rosSize,
            IntPtr rod, uint rodVersion, uint rodSize);

        private const int AF_INET = 2;
        private const int TCP_TABLE_OWNER_PID_ALL = 5;
        private const int TcpConnectionEstatsData = 1;
        private const int ESTATS_DATA_ROD_SIZE = 104; // sizeof(TCP_ESTATS_DATA_ROD_v0) с запасом

        public class ProcTraffic
        {
            public int Pid;
            public string Name;
            public int Connections;
            public long TotalBytesIn;   // суммарно по живым соединениям
            public long TotalBytesOut;
            public float RateIn;        // байт/сек, считается по дельте
            public float RateOut;
        }

        private class PrevSample
        {
            public long BytesIn;
            public long BytesOut;
            public DateTime When;
        }

        private static Dictionary<int, PrevSample> prev = new Dictionary<int, PrevSample>();
        private static bool estatsWorks = true; // после первой неудачи не долбим API

        public static List<MIB_TCPROW_OWNER_PID> GetTcpRows()
        {
            var result = new List<MIB_TCPROW_OWNER_PID>();
            int size = 0;
            GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (size <= 0) return result;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (GetExtendedTcpTable(buf, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                    return result;

                int count = Marshal.ReadInt32(buf);
                IntPtr rowPtr = new IntPtr(buf.ToInt64() + 4);
                int rowSize = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                for (int i = 0; i < count; i++)
                {
                    result.Add((MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(rowPtr, typeof(MIB_TCPROW_OWNER_PID)));
                    rowPtr = new IntPtr(rowPtr.ToInt64() + rowSize);
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
            return result;
        }

        // Байты in/out по одному соединению, или false если EStats не дал данных
        private static bool TryGetConnBytes(MIB_TCPROW_OWNER_PID r, out long bytesIn, out long bytesOut)
        {
            bytesIn = 0;
            bytesOut = 0;
            if (!estatsWorks) return false;

            var row = new MIB_TCPROW
            {
                state = r.state,
                localAddr = r.localAddr,
                localPort = r.localPort,
                remoteAddr = r.remoteAddr,
                remotePort = r.remotePort
            };

            // Включаем сбор (если ещё не включён). Ошибку игнорируем - у части
            // соединений сбор уже идёт, у части они успели закрыться
            byte[] rw = new byte[] { 1 };
            SetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData, rw, 0, 1, 0);

            IntPtr rod = Marshal.AllocHGlobal(ESTATS_DATA_ROD_SIZE);
            try
            {
                uint res = GetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData,
                    IntPtr.Zero, 0, 0, IntPtr.Zero, 0, 0, rod, 0, (uint)ESTATS_DATA_ROD_SIZE);
                if (res != 0) return false;
                // TCP_ESTATS_DATA_ROD_v0: DataBytesOut (offset 0), DataBytesIn (offset 16)
                bytesOut = Marshal.ReadInt64(rod, 0);
                bytesIn = Marshal.ReadInt64(rod, 16);
                return true;
            }
            catch
            {
                estatsWorks = false;
                return false;
            }
            finally { Marshal.FreeHGlobal(rod); }
        }

        // Снимок трафика по процессам. Скорость считается по дельте с прошлым вызовом
        public static List<ProcTraffic> Snapshot()
        {
            var byPid = new Dictionary<int, ProcTraffic>();
            List<MIB_TCPROW_OWNER_PID> rows = GetTcpRows();

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                int pid = (int)r.owningPid;
                if (pid <= 4) continue; // System/Idle не интересны

                ProcTraffic pt;
                if (!byPid.TryGetValue(pid, out pt))
                {
                    pt = new ProcTraffic { Pid = pid };
                    byPid[pid] = pt;
                }
                pt.Connections++;

                // Байты считаем только на установленных соединениях
                if (r.state == 5)
                {
                    long bin, bout;
                    if (TryGetConnBytes(r, out bin, out bout))
                    {
                        pt.TotalBytesIn += bin;
                        pt.TotalBytesOut += bout;
                    }
                }
            }

            // Имена процессов и скорость по дельте
            DateTime now = DateTime.Now;
            var newPrev = new Dictionary<int, PrevSample>();
            var list = new List<ProcTraffic>();
            foreach (KeyValuePair<int, ProcTraffic> pair in byPid)
            {
                ProcTraffic pt = pair.Value;
                try { pt.Name = Process.GetProcessById(pt.Pid).ProcessName; }
                catch { pt.Name = "PID " + pt.Pid; }

                PrevSample ps;
                if (prev.TryGetValue(pt.Pid, out ps))
                {
                    double sec = (now - ps.When).TotalSeconds;
                    if (sec > 0.5)
                    {
                        // Соединения закрываются и сумма падает - минус не показываем
                        pt.RateIn = (float)Math.Max(0, (pt.TotalBytesIn - ps.BytesIn) / sec);
                        pt.RateOut = (float)Math.Max(0, (pt.TotalBytesOut - ps.BytesOut) / sec);
                    }
                }
                newPrev[pt.Pid] = new PrevSample { BytesIn = pt.TotalBytesIn, BytesOut = pt.TotalBytesOut, When = now };
                list.Add(pt);
            }
            prev = newPrev;

            // Сортировка: сначала по скорости, потом по числу соединений
            list.Sort(delegate(ProcTraffic a, ProcTraffic b) {
                float ra = a.RateIn + a.RateOut, rb = b.RateIn + b.RateOut;
                if (ra != rb) return rb.CompareTo(ra);
                return b.Connections.CompareTo(a.Connections);
            });
            return list;
        }

        public static bool EstatsAvailable { get { return estatsWorks; } }
    }

    // Раздел «Сеть»: живая скорость с графиками, пинг-тест, трафик по процессам
    public class NetworkPanel : Panel
    {
        private readonly Timer refreshTimer;

        private Label downValueLabel;
        private Label upValueLabel;
        private Label sessionLabel;
        private NetGraph downGraph;
        private NetGraph upGraph;

        private long lastBytesIn = -1;
        private long lastBytesOut = -1;
        private DateTime lastSample = DateTime.MinValue;
        private long sessionIn = 0;
        private long sessionOut = 0;

        private RoundedButton pingBtn;
        private TextBox pingHostBox;
        private Label[] pingResultLabels;
        private bool pingRunning = false;

        private Label[] procNameLabels;
        private Label[] procConnLabels;
        private Label[] procRateLabels;
        private Label trafficHintLabel;
        private Label surgeonStatusLabel;
        private const int ProcRows = 5;

        private RoundedButton[] dnsButtons;
        private Label dnsStatusLabel;
        private bool dnsBusy = false;

        // Пинг-тест по этим серверам + свой адрес из поля ввода.
        // Xbox DNS (xbox-dns.ru) - Smart DNS для Xbox Live/ChatGPT/Supercell, адреса 111.88.96.50/51
        private static readonly string[] PingHosts = { "1.1.1.1", "8.8.8.8", "111.88.96.50" };
        private static readonly string[] PingNames = { "Cloudflare", "Google", "Xbox DNS" };

        public NetworkPanel()
        {
            Size = new Size(760, 616);
            AutoScroll = true;
            BackColor = Theme.BgColor;
            NativeMethods.ApplyDarkScrollbar(this);
            DarkScroll.Attach(this);

            BuildUi();

            refreshTimer = new Timer { Interval = 1000 };
            refreshTimer.Tick += RefreshTick;
            refreshTimer.Start();
        }

        private void BuildUi()
        {
            var titleLbl = new Label
            {
                Text = Lang.T("Сеть", "Network"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(24, 16),
                AutoSize = true
            };
            Controls.Add(titleLbl);

            // ---- Карточка скорости ----
            var speedCard = Theme.MakeCard(this, new Point(24, 60), new Size(712, 168));

            var downTitle = new Label
            {
                Text = "↓ " + Lang.T("Загрузка", "Download"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(16, 12),
                AutoSize = true
            };
            speedCard.Controls.Add(downTitle);

            downValueLabel = new Label
            {
                Text = "—",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(16, 32),
                AutoSize = true
            };
            speedCard.Controls.Add(downValueLabel);

            downGraph = new NetGraph { Location = new Point(16, 66), Size = new Size(330, 88), LineColor = Theme.Accent };
            speedCard.Controls.Add(downGraph);

            var upTitle = new Label
            {
                Text = "↑ " + Lang.T("Отдача", "Upload"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(366, 12),
                AutoSize = true
            };
            speedCard.Controls.Add(upTitle);

            upValueLabel = new Label
            {
                Text = "—",
                ForeColor = Color.FromArgb(90, 160, 240),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                Location = new Point(366, 32),
                AutoSize = true
            };
            speedCard.Controls.Add(upValueLabel);

            upGraph = new NetGraph { Location = new Point(366, 66), Size = new Size(330, 88), LineColor = Color.FromArgb(90, 160, 240) };
            speedCard.Controls.Add(upGraph);

            sessionLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(180, 15),
                AutoSize = true
            };
            speedCard.Controls.Add(sessionLabel);

            // ---- Карточка пинга ----
            var pingCard = Theme.MakeCard(this, new Point(24, 240), new Size(712, 128));

            var pingTitle = new Label
            {
                Text = Lang.T("Пинг-тест", "Ping test"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            pingCard.Controls.Add(pingTitle);

            pingHostBox = new TextBox
            {
                Text = "",
                ForeColor = Theme.TextMain,
                BackColor = Theme.InputColor,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(370, 10),
                Size = new Size(210, 24)
            };
            pingCard.Controls.Add(pingHostBox);

            var hostHint = new Label
            {
                Text = Lang.T("свой адрес (необязательно)", "custom host (optional)"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(370, 36),
                AutoSize = true
            };
            pingCard.Controls.Add(hostHint);

            pingBtn = new RoundedButton
            {
                Text = Lang.T("Тест", "Test"),
                ButtonColor = Theme.Accent,
                HoverColor = Theme.AccentHover,
                TextColor = Theme.BgColor,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(596, 8),
                Size = new Size(100, 30)
            };
            pingBtn.Click += (s, e) => RunPingTest();
            pingCard.Controls.Add(pingBtn);

            // Строки результатов: 3 сервера + свой адрес
            pingResultLabels = new Label[PingHosts.Length + 1];
            for (int i = 0; i < pingResultLabels.Length; i++)
            {
                var lbl = new Label
                {
                    Text = i < PingHosts.Length ? PingNames[i] + " (" + PingHosts[i] + "):  —" : "",
                    ForeColor = Theme.TextDim,
                    BackColor = Color.Transparent,
                    Font = new Font("Segoe UI", 9F),
                    Location = new Point(16 + (i % 2) * 350, 48 + (i / 2) * 26),
                    Size = new Size(340, 20),
                    AutoEllipsis = true
                };
                pingCard.Controls.Add(lbl);
                pingResultLabels[i] = lbl;
            }

            var jitterHint = new Label
            {
                Text = Lang.T("5 пингов на сервер: средний / мин / макс / джиттер",
                              "5 pings per host: avg / min / max / jitter"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(16, 104),
                AutoSize = true
            };
            pingCard.Controls.Add(jitterHint);

            // ---- Карточка трафика по процессам ----
            var trafficCard = Theme.MakeCard(this, new Point(24, 380), new Size(712, 216));

            var trafficTitle = new Label
            {
                Text = Lang.T("Кто использует сеть", "Who is using the network"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            trafficCard.Controls.Add(trafficTitle);

            var hdrName = MakeDimLabel(trafficCard, Lang.T("Процесс", "Process"), 16, 38);
            var hdrConn = MakeDimLabel(trafficCard, Lang.T("Соединения", "Connections"), 300, 38);
            var hdrRate = MakeDimLabel(trafficCard, Lang.T("Скорость ↓ / ↑", "Rate ↓ / ↑"), 440, 38);

            procNameLabels = new Label[ProcRows];
            procConnLabels = new Label[ProcRows];
            procRateLabels = new Label[ProcRows];
            for (int i = 0; i < ProcRows; i++)
            {
                int y = 62 + i * 26;
                procNameLabels[i] = MakeRowLabel(trafficCard, 16, y, 270);
                procConnLabels[i] = MakeRowLabel(trafficCard, 300, y, 120);
                procRateLabels[i] = MakeRowLabel(trafficCard, 440, y, 250);
            }

            trafficHintLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 7.5F),
                Location = new Point(16, 194),
                Size = new Size(680, 16),
                AutoEllipsis = true
            };
            trafficCard.Controls.Add(trafficHintLabel);

            // ---- Карточка: Хирург латентности ----
            var surgeonCard = Theme.MakeCard(this, new Point(24, 608), new Size(712, 88));

            var surgeonTitle = new Label
            {
                Text = Lang.T("🌐 Хирург латентности", "🌐 Latency Surgeon"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(16, 14),
                AutoSize = true
            };
            surgeonCard.Controls.Add(surgeonTitle);

            var surgeonDesc = new Label
            {
                Text = Lang.T("Режет фоновый трафик (Steam, обновления, браузеры) в игре, QoS-приоритет для игровых пакетов",
                              "Kills background traffic (Steam, updates, browsers) during game, QoS priority for game packets"),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 36),
                Size = new Size(560, 32)
            };
            surgeonCard.Controls.Add(surgeonDesc);

            var surgeonToggle = new ToggleSwitch
            {
                Location = new Point(646, 22),
                Checked = AppConfig.GetBool("latency_surgeon", false)
            };
            surgeonToggle.CheckedChanged += (s, e) => {
                if (surgeonToggle.Checked) LatencySurgeon.Enable();
                else LatencySurgeon.Disable();
                AppConfig.SetBool("latency_surgeon", surgeonToggle.Checked);
            };
            surgeonCard.Controls.Add(surgeonToggle);

            surgeonStatusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(16, 62),
                AutoSize = true
            };
            surgeonCard.Controls.Add(surgeonStatusLabel);

            // Восстанавливаем состояние после перезапуска
            if (AppConfig.GetBool("latency_surgeon", false))
                LatencySurgeon.Enable();

            // ---- Карточка: DNS-переключатель ----
            var dnsCard = Theme.MakeCard(this, new Point(24, surgeonCard.Bottom + 12), new Size(712, 118));

            var dnsTitle = new Label
            {
                Text = Lang.T("DNS-переключатель", "DNS switcher"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            dnsCard.Controls.Add(dnsTitle);

            var dnsDesc = new Label
            {
                Text = Lang.T("Меняет DNS на всех активных адаптерах. Быстрее отклик сайтов, обход тормозов DNS провайдера.",
                              "Sets DNS on all active adapters. Faster site response, bypasses a slow ISP DNS."),
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F),
                Location = new Point(16, 34),
                Size = new Size(680, 16),
                AutoEllipsis = true
            };
            dnsCard.Controls.Add(dnsDesc);

            dnsButtons = new RoundedButton[DnsSwitcher.Presets.Length];
            int dnsX = 16;
            for (int i = 0; i < DnsSwitcher.Presets.Length; i++)
            {
                DnsSwitcher.Preset preset = DnsSwitcher.Presets[i];
                bool isAuto = preset.Primary == null;
                int w = isAuto ? 140 : 120;
                var b = new RoundedButton
                {
                    Text = isAuto ? Lang.T("Авто (DHCP)", "Auto (DHCP)") : preset.Name,
                    ButtonColor = Theme.KeyColor,
                    HoverColor = Theme.KeyHover,
                    TextColor = Theme.TextMain,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Location = new Point(dnsX, 56),
                    Size = new Size(w, 30)
                };
                b.Click += (s, e) => ApplyDnsPreset(preset);
                dnsCard.Controls.Add(b);
                dnsButtons[i] = b;
                dnsX += w + 12;
            }

            dnsStatusLabel = new Label
            {
                Text = "",
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 92),
                Size = new Size(680, 18),
                AutoEllipsis = true
            };
            dnsCard.Controls.Add(dnsStatusLabel);

            // ---- Карточка: Инструменты сети (сброс стека + блокировка приложений) ----
            var toolsCard = Theme.MakeCard(this, new Point(24, dnsCard.Bottom + 12), new Size(712, 108));

            var toolsTitle = new Label
            {
                Text = Lang.T("Инструменты сети", "Network tools"),
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Location = new Point(16, 12),
                AutoSize = true
            };
            toolsCard.Controls.Add(toolsTitle);

            var toolsStatus = new Label
            {
                Text = "",
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8.5F),
                Location = new Point(16, 78),
                Size = new Size(680, 18),
                AutoEllipsis = true
            };

            var resetBtn = new RoundedButton
            {
                Text = Lang.T("Сбросить сеть (winsock/IP)", "Reset network (winsock/IP)"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(16, 40),
                Size = new Size(240, 32)
            };
            resetBtn.Click += (s, e) => {
                DialogResult r = DTDialog.Show(
                    Lang.T("Сбросить сетевой стек? Часть настроек применится только после перезагрузки. Это чинит «нет интернета», но сбросит сетевые твики.",
                           "Reset the network stack? Some changes apply only after a reboot. This fixes \"no internet\" issues but clears network tweaks."),
                    "DeepTools", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;

                resetBtn.Enabled = false;
                toolsStatus.ForeColor = Theme.TextDim;
                toolsStatus.Text = Lang.T("Сбрасываем сеть...", "Resetting network...");
                var w = new System.ComponentModel.BackgroundWorker();
                w.DoWork += (s2, e2) => NetworkTools.ResetNetworkStack();
                w.RunWorkerCompleted += (s2, e2) => {
                    if (IsDisposed) return;
                    resetBtn.Enabled = true;
                    toolsStatus.Text = Lang.T("Сеть сброшена. Перезагрузи компьютер, чтобы всё применилось.",
                                              "Network reset. Reboot the PC to fully apply.");
                    toolsStatus.ForeColor = Theme.Accent;
                };
                w.RunWorkerAsync();
            };
            toolsCard.Controls.Add(resetBtn);

            var blockBtn = new RoundedButton
            {
                Text = Lang.T("Блокировка интернета приложениям", "Block apps from internet"),
                ButtonColor = Theme.KeyColor,
                HoverColor = Theme.KeyHover,
                TextColor = Theme.TextMain,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(268, 40),
                Size = new Size(280, 32)
            };
            blockBtn.Click += (s, e) => {
                using (var f = new FirewallBlockForm())
                    f.ShowDialog(FindForm());
            };
            toolsCard.Controls.Add(blockBtn);

            toolsCard.Controls.Add(toolsStatus);

            ShowCurrentDns();
        }

        private Label MakeDimLabel(Panel parent, string text, int x, int y)
        {
            var lbl = new Label
            {
                Text = text,
                ForeColor = Theme.TextDim,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Location = new Point(x, y),
                AutoSize = true
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private Label MakeRowLabel(Panel parent, int x, int y, int w)
        {
            var lbl = new Label
            {
                Text = "",
                ForeColor = Theme.TextMain,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 9F),
                Location = new Point(x, y),
                Size = new Size(w, 20),
                AutoEllipsis = true
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        // Раз в секунду: скорость по всем активным адаптерам; трафик по
        // процессам обновляем раз в 3 тика - GetExtendedTcpTable недёшев
        private int tickCount = 0;

        private void RefreshTick(object sender, EventArgs e)
        {
            // Панель скрыта - не жжём CPU (кроме учёта сессии)
            RefreshSpeed();
            tickCount++;
            if (Visible && tickCount % 3 == 0) RefreshTraffic();
            // Обновляем статус Хирурга латентности раз в 5 тиков
            if (Visible && LatencySurgeon.IsActive && tickCount % 5 == 0)
            {
                surgeonStatusLabel.Text = string.Format(
                    "loss {0}%  jitter {1} ms",
                    LatencySurgeon.PacketLoss, LatencySurgeon.Jitter);
            }
            else if (!LatencySurgeon.IsActive && surgeonStatusLabel != null)
            {
                surgeonStatusLabel.Text = "";
            }
        }

        private void RefreshSpeed()
        {
            try
            {
                long totalIn = 0, totalOut = 0;
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
                for (int i = 0; i < nics.Length; i++)
                {
                    var nic = nics[i];
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    IPv4InterfaceStatistics st = nic.GetIPv4Statistics();
                    totalIn += st.BytesReceived;
                    totalOut += st.BytesSent;
                }

                DateTime now = DateTime.Now;
                if (lastBytesIn >= 0 && lastSample != DateTime.MinValue)
                {
                    double sec = (now - lastSample).TotalSeconds;
                    if (sec > 0.2)
                    {
                        // Счётчики адаптера могут сброситься (переподключение) - тогда дельта
                        // отрицательная, пропускаем такт
                        float rin = (float)Math.Max(0, (totalIn - lastBytesIn) / sec);
                        float rout = (float)Math.Max(0, (totalOut - lastBytesOut) / sec);
                        sessionIn += Math.Max(0, totalIn - lastBytesIn);
                        sessionOut += Math.Max(0, totalOut - lastBytesOut);

                        if (Visible)
                        {
                            downValueLabel.Text = FormatRate(rin);
                            upValueLabel.Text = FormatRate(rout);
                            sessionLabel.Text = Lang.T("За сессию: ", "This session: ")
                                + "↓ " + FormatBytes(sessionIn) + "  ↑ " + FormatBytes(sessionOut);
                            downGraph.AddPoint(rin);
                            upGraph.AddPoint(rout);
                        }
                    }
                }
                lastBytesIn = totalIn;
                lastBytesOut = totalOut;
                lastSample = now;
            }
            catch { }
        }

        private void RefreshTraffic()
        {
            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += delegate(object s, System.ComponentModel.DoWorkEventArgs e2) {
                e2.Result = NetTraffic.Snapshot();
            };
            worker.RunWorkerCompleted += delegate(object s, System.ComponentModel.RunWorkerCompletedEventArgs e2) {
                if (e2.Error != null || e2.Result == null) return;
                if (IsDisposed) return;
                var list = (List<NetTraffic.ProcTraffic>)e2.Result;
                for (int i = 0; i < ProcRows; i++)
                {
                    if (i < list.Count)
                    {
                        var p = list[i];
                        procNameLabels[i].Text = p.Name;
                        procConnLabels[i].Text = p.Connections.ToString();
                        procRateLabels[i].Text = NetTraffic.EstatsAvailable
                            ? FormatRate(p.RateIn) + " / " + FormatRate(p.RateOut)
                            : "—";
                    }
                    else
                    {
                        procNameLabels[i].Text = "";
                        procConnLabels[i].Text = "";
                        procRateLabels[i].Text = "";
                    }
                }
                trafficHintLabel.Text = NetTraffic.EstatsAvailable
                    ? Lang.T("TCP-трафик по данным Windows EStats. UDP-трафик (часть игр, стримы) не учитывается.",
                             "TCP traffic via Windows EStats. UDP traffic (some games, streams) is not counted.")
                    : Lang.T("Скорость по процессам недоступна на этой системе - показаны только соединения.",
                             "Per-process rates are unavailable on this system - only connections are shown.");
            };
            worker.RunWorkerAsync();
        }

        // ---- Пинг-тест ----

        private void RunPingTest()
        {
            if (pingRunning) return;
            pingRunning = true;
            pingBtn.Text = Lang.T("Тестируем...", "Testing...");

            var hosts = new List<string>(PingHosts);
            var names = new List<string>(PingNames);
            string custom = pingHostBox.Text.Trim();
            if (custom.Length > 0)
            {
                hosts.Add(custom);
                names.Add(custom);
            }

            for (int i = 0; i < pingResultLabels.Length; i++)
            {
                pingResultLabels[i].Text = i < hosts.Count ? names[i] + ":  ..." : "";
                pingResultLabels[i].ForeColor = Theme.TextDim;
            }

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += delegate(object s, System.ComponentModel.DoWorkEventArgs e2) {
                var results = new List<string>();
                var colors = new List<Color>();
                for (int h = 0; h < hosts.Count; h++)
                {
                    var times = new List<long>();
                    try
                    {
                        using (var ping = new Ping())
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                try
                                {
                                    PingReply reply = ping.Send(hosts[h], 2000);
                                    if (reply != null && reply.Status == IPStatus.Success)
                                        times.Add(reply.RoundtripTime);
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }

                    if (times.Count == 0)
                    {
                        results.Add(names[h] + ":  " + Lang.T("нет ответа", "no reply"));
                        colors.Add(Theme.Danger);
                        continue;
                    }

                    long min = long.MaxValue, max = 0, sum = 0;
                    for (int i = 0; i < times.Count; i++)
                    {
                        if (times[i] < min) min = times[i];
                        if (times[i] > max) max = times[i];
                        sum += times[i];
                    }
                    long avg = sum / times.Count;
                    long jitter = max - min;

                    results.Add(string.Format("{0}:  {1} мс  (мин {2} / макс {3} / джиттер {4})",
                        names[h], avg, min, max, jitter).Replace("мс", Lang.T("мс", "ms"))
                        .Replace("мин", Lang.T("мин", "min")).Replace("макс", Lang.T("макс", "max"))
                        .Replace("джиттер", Lang.T("джиттер", "jitter")));
                    colors.Add(avg < 30 ? Theme.Accent : (avg < 80 ? Theme.Warning : Theme.Danger));
                }
                e2.Result = new object[] { results, colors };
            };
            worker.RunWorkerCompleted += delegate(object s, System.ComponentModel.RunWorkerCompletedEventArgs e2) {
                pingRunning = false;
                pingBtn.Text = Lang.T("Тест", "Test");
                if (e2.Error != null || e2.Result == null || IsDisposed) return;
                var arr = (object[])e2.Result;
                var results = (List<string>)arr[0];
                var colors = (List<Color>)arr[1];
                for (int i = 0; i < pingResultLabels.Length; i++)
                {
                    if (i < results.Count)
                    {
                        pingResultLabels[i].Text = results[i];
                        pingResultLabels[i].ForeColor = colors[i];
                    }
                }
            };
            worker.RunWorkerAsync();
        }

        // ---- DNS-переключатель ----

        private void ApplyDnsPreset(DnsSwitcher.Preset preset)
        {
            if (dnsBusy) return;
            dnsBusy = true;
            for (int i = 0; i < dnsButtons.Length; i++) dnsButtons[i].Enabled = false;
            dnsStatusLabel.ForeColor = Theme.TextDim;
            dnsStatusLabel.Text = Lang.T("Применяем DNS...", "Applying DNS...");

            var worker = new System.ComponentModel.BackgroundWorker();
            worker.DoWork += delegate(object s, System.ComponentModel.DoWorkEventArgs e2) {
                e2.Result = DnsSwitcher.Apply(preset);
            };
            worker.RunWorkerCompleted += delegate(object s, System.ComponentModel.RunWorkerCompletedEventArgs e2) {
                dnsBusy = false;
                if (IsDisposed) return;
                for (int i = 0; i < dnsButtons.Length; i++) dnsButtons[i].Enabled = true;

                int n = (e2.Error == null && e2.Result != null) ? (int)e2.Result : 0;
                if (n > 0)
                {
                    string what = preset.Primary == null
                        ? Lang.T("Авто (DHCP)", "Auto (DHCP)")
                        : preset.Name + " (" + preset.Primary + ")";
                    dnsStatusLabel.Text = what + Lang.T(" — применён на ", " — applied to ") + n +
                        Lang.T(" адаптер(ах), кэш DNS сброшен", " adapter(s), DNS cache flushed");
                    dnsStatusLabel.ForeColor = Theme.Accent;
                }
                else
                {
                    dnsStatusLabel.Text = Lang.T("Не удалось изменить DNS (нет активных адаптеров или ошибка)",
                                                 "Failed to change DNS (no active adapters or an error occurred)");
                    dnsStatusLabel.ForeColor = Theme.Warning;
                }
                HighlightActiveDns();
            };
            worker.RunWorkerAsync();
        }

        private void ShowCurrentDns()
        {
            string cur = DnsSwitcher.CurrentDns();
            dnsStatusLabel.Text = Lang.T("Текущий DNS: ", "Current DNS: ") +
                (cur ?? Lang.T("не определён", "unknown"));
            dnsStatusLabel.ForeColor = Theme.TextDim;
            HighlightActiveDns();
        }

        // Подсветить кнопку пресета, чей первичный адрес совпадает с текущим DNS
        private void HighlightActiveDns()
        {
            if (dnsButtons == null) return;
            string cur = DnsSwitcher.CurrentDns();
            for (int i = 0; i < dnsButtons.Length; i++)
            {
                DnsSwitcher.Preset preset = DnsSwitcher.Presets[i];
                bool active = preset.Primary != null && cur != null && cur.StartsWith(preset.Primary);
                dnsButtons[i].ButtonColor = active ? Theme.Accent : Theme.KeyColor;
                dnsButtons[i].HoverColor = active ? Theme.AccentHover : Theme.KeyHover;
                dnsButtons[i].TextColor = active ? Theme.BgColor : Theme.TextMain;
                dnsButtons[i].Invalidate();
            }
        }

        // ---- Форматирование ----

        private static string FormatRate(float bytesPerSec)
        {
            if (bytesPerSec >= 1024 * 1024)
                return string.Format("{0:0.0} " + Lang.T("МБ/с", "MB/s"), bytesPerSec / (1024f * 1024f));
            if (bytesPerSec >= 1024)
                return string.Format("{0:0.0} " + Lang.T("КБ/с", "KB/s"), bytesPerSec / 1024f);
            return string.Format("{0:0} " + Lang.T("Б/с", "B/s"), bytesPerSec);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return string.Format("{0:0.0} " + Lang.T("ГБ", "GB"), bytes / (1024.0 * 1024 * 1024));
            if (bytes >= 1024L * 1024)
                return string.Format("{0:0.0} " + Lang.T("МБ", "MB"), bytes / (1024.0 * 1024));
            return string.Format("{0:0} " + Lang.T("КБ", "KB"), bytes / 1024.0);
        }
    }
}
