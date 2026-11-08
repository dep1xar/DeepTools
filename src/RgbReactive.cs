using System;
using System.Drawing;
using System.Net;
using System.Text;
using System.Threading;

namespace DeepTools
{
    // RGB-реакция через OpenRGB HTTP REST API (openrgb --server).
    // Цвет плавно переходит от синего (холодно) через зелёный к красному (горячо)
    // по температуре CPU/GPU. Пульсирует под нагрузкой. Мигает красным при просадке FPS.
    // Требует запущенного OpenRGB с включённым SDK/HTTP-сервером.
    public static class RgbReactive
    {
        private static bool _enabled = false;
        private static Thread _thread;
        private static volatile bool _running = false;

        // OpenRGB HTTP сервер по умолчанию localhost:6800
        private static string _host = "http://127.0.0.1:6800";

        // Порог FPS для мигания красным
        public static int FpsAlertThreshold = 30;

        public static bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (value) Start();
                else { Stop(); }
            }
        }

        public static string Host
        {
            get { return _host; }
            set { _host = value; }
        }

        private static void Start()
        {
            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "RgbReactive" };
            _thread.Start();
        }

        private static void Stop()
        {
            _running = false;
            // Гасим все лампочки при выключении
            try { SetAll(Color.Black); } catch { }
        }

        private static void Loop()
        {
            int tick = 0;
            while (_running)
            {
                try
                {
                    int cpuTemp = CpuSensor.GetTemperature();
                    int fps     = PresentTracer.GetAnyFps();

                    Color c;

                    // FPS просадка — красный мигающий (чётный/нечётный тик)
                    if (fps > 0 && fps < FpsAlertThreshold)
                    {
                        c = (tick % 2 == 0) ? Color.FromArgb(255, 20, 20) : Color.FromArgb(60, 0, 0);
                    }
                    else
                    {
                        // Температурный градиент: 30°→60° = синий→зелёный→красный
                        c = TempToColor(cpuTemp);

                        // Пульсация при нагрузке (CPU > 70°): яркость ±25%
                        if (cpuTemp > 70)
                        {
                            double pulse = 0.75 + 0.25 * Math.Sin(tick * 0.4);
                            c = ScaleColor(c, pulse);
                        }
                    }

                    SetAll(c);
                }
                catch { }

                tick++;
                Thread.Sleep(400);
            }
        }

        // Температура [30..80] → цвет синий→зелёный→красный
        private static Color TempToColor(int temp)
        {
            if (temp <= 0) return Color.FromArgb(0, 40, 120); // нет данных - синий

            float t = Math.Max(0f, Math.Min(1f, (temp - 30f) / 50f));

            if (t < 0.5f)
            {
                // синий (0,80,255) → зелёный (46,214,140)
                float r = t * 2f;
                int R = (int)(0   + r * 46);
                int G = (int)(80  + r * (214 - 80));
                int B = (int)(255 + r * (140 - 255));
                return Color.FromArgb(R, G, B);
            }
            else
            {
                // зелёный (46,214,140) → красный (255,40,40)
                float r = (t - 0.5f) * 2f;
                int R = (int)(46  + r * (255 - 46));
                int G = (int)(214 + r * (40  - 214));
                int B = (int)(140 + r * (40  - 140));
                return Color.FromArgb(R, G, B);
            }
        }

        private static Color ScaleColor(Color c, double scale)
        {
            return Color.FromArgb(
                Math.Min(255, (int)(c.R * scale)),
                Math.Min(255, (int)(c.G * scale)),
                Math.Min(255, (int)(c.B * scale)));
        }

        // Отправляем RGBW-пакет на все устройства через OpenRGB HTTP API
        private static void SetAll(Color c)
        {
            // GET /api/controllers — список устройств
            string json = Get("/api/controllers");
            if (json == null) return;

            // Парсим ID устройств вручную (без JSON-библиотеки): ищем "device_id":N
            int idx = 0;
            while (true)
            {
                int pos = json.IndexOf("\"device_id\"", idx);
                if (pos < 0) break;
                int colon = json.IndexOf(':', pos);
                if (colon < 0) break;
                int start = colon + 1;
                while (start < json.Length && json[start] == ' ') start++;
                int end = start;
                while (end < json.Length && char.IsDigit(json[end])) end++;
                string idStr = json.Substring(start, end - start);
                int devId;
                if (int.TryParse(idStr, out devId))
                {
                    // GET число светодиодов
                    string devJson = Get("/api/controller/" + devId);
                    int leds = CountLeds(devJson);
                    if (leds > 0)
                        PutColor(devId, c, leds);
                }
                idx = end;
            }
        }

        private static int CountLeds(string devJson)
        {
            if (devJson == null) return 1;
            int pos = devJson.IndexOf("\"leds\"");
            if (pos < 0) return 1;
            // считаем массив "{" после "leds"
            int arr = devJson.IndexOf('[', pos);
            if (arr < 0) return 1;
            int count = 0;
            for (int i = arr; i < devJson.Length; i++)
            {
                if (devJson[i] == '{') count++;
                if (devJson[i] == ']') break;
            }
            return Math.Max(1, count);
        }

        // PUT /api/controller/{id}/leds — устанавливаем цвет
        private static void PutColor(int devId, Color c, int ledCount)
        {
            var sb = new StringBuilder();
            sb.Append("{\"colors\":[");
            for (int i = 0; i < ledCount; i++)
            {
                if (i > 0) sb.Append(',');
                // OpenRGB ожидает {"r":R,"g":G,"b":B}
                sb.Append("{\"r\":");  sb.Append(c.R);
                sb.Append(",\"g\":"); sb.Append(c.G);
                sb.Append(",\"b\":"); sb.Append(c.B);
                sb.Append('}');
            }
            sb.Append("]}");
            Put("/api/controller/" + devId + "/leds", sb.ToString());
        }

        private static string Get(string path)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(_host + path);
                req.Timeout = 1000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new System.IO.StreamReader(resp.GetResponseStream()))
                    return sr.ReadToEnd();
            }
            catch { return null; }
        }

        private static void Put(string path, string body)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(body);
                var req = (HttpWebRequest)WebRequest.Create(_host + path);
                req.Method = "PUT";
                req.ContentType = "application/json";
                req.ContentLength = data.Length;
                req.Timeout = 1000;
                using (var s = req.GetRequestStream())
                    s.Write(data, 0, data.Length);
                using (req.GetResponse()) { }
            }
            catch { }
        }
    }
}
