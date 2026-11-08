# DeepTools Telemetry Server / Сервер статистики DeepTools

Небольшой сервер для счётчика онлайна в главном окне DeepTools и полной
панели в отдельном `DeepToolsStats.exe`. Считает онлайн, запуски,
пользователей, версии и ошибки. Написан на Python 3.8+ без сторонних
зависимостей, данные лежат в SQLite.

> **Приватность.** Сервер не сохраняет IP-адреса. Страна берётся из заголовка
> `CF-IPCountry` (Cloudflare) или `X-Country-Code` только если включён
> `DT_TRUST_PROXY=1`. Подробности — в [PRIVACY.md](../PRIVACY.md).

---

## Быстрый старт

```bash
cd server
python deeptools_telemetry.py
```

Проверка:

```bash
curl http://127.0.0.1:8787/health
# {"ok":true,"service":"deeptools-telemetry"}
```

Затем укажи адрес сервера в конфиге клиента
`%APPDATA%\DeepTools\config.txt`:

```ini
telemetry_url=http://127.0.0.1:8787
```

или задай переменную окружения перед запуском DeepTools:

```powershell
$env:DEEPTOOLS_TELEMETRY_URL = "http://127.0.0.1:8787"
```

Для релиза адрес вшивается при сборке:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 `
    -TelemetryUrl "https://stats.example.com"
```

---

## Переменные окружения

| Переменная | По умолчанию | Назначение |
|---|---|---|
| `DT_PORT` | `8787` | Порт HTTP |
| `DT_DB` | `server/telemetry.db` | Файл SQLite |
| `DT_TRUST_PROXY` | `0` | `1` — доверять заголовкам страны от Cloudflare/nginx |
| `DT_RETENTION_DAYS` | `90` | Сколько дней хранить события ошибок |
| `DT_ADMIN_TOKEN` | пусто | Bearer-токен для `/api/v1/admin/errors` |
| `DT_VIEW_TOKEN` | пусто | Bearer-токен для полной панели `/api/v1/stats` |

Если `DT_VIEW_TOKEN` задан, полную статистику может получить только клиент,
который прислал этот токен. Обычный `DeepTools.exe` его не знает и использует
только лёгкий публичный счётчик `/api/v1/online`. Токен вшивается в отдельный
`DeepToolsStats.exe` при сборке:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 `
    -TelemetryUrl "https://stats.example.com" `
    -StatsToken "your-view-token"
```

Токен в клиенте не является криптографическим секретом: его можно извлечь из
exe. Он нужен, чтобы панель не была открыта всему интернету, а только тем, кому
ты передал `DeepToolsStats.exe`.

---

## API

### `POST /api/v1/event`

```json
{
  "install_id": "0123456789abcdef0123456789abcdef",
  "session_id": "fedcba9876543210fedcba9876543210",
  "event": "start",
  "version": "2.2.0.0",
  "os": "Windows 11 Pro 24H2 (26100)",
  "lang": "ru"
}
```

События: `start`, `heartbeat`, `exit`, `error`.
Для `error` дополнительно:

```json
{
  "error_code": "a1b2c3d4e5f60718",
  "error_title": "YandexCleaner: IOException [0x80070020]",
  "error_module": "YandexCleaner"
}
```

Исходный текст исключения, стек, имена файлов, имя пользователя и IP **не
передаются и не хранятся**.

### `GET /api/v1/online`

Публичный лёгкий счётчик для главного окна DeepTools:

```json
{ "ok": true, "online_now": 3 }
```

Он не раскрывает запуски, версии, страны и ошибки.

### `GET /api/v1/stats`

Полная агрегированная статистика. Если задан `DT_VIEW_TOKEN`, нужен заголовок
`Authorization: Bearer <DT_VIEW_TOKEN>`; иначе эндпоинт публичный (для обратной
совместимости).

```json
{
  "ok": true,
  "online_now": 3,
  "online_24h": 42,
  "users_total": 120,
  "launches_today": 17,
  "launches_total": 640,
  "errors_24h": 5,
  "errors_total": 23,
  "users_with_errors_24h": 4,
  "top_errors": [
    {
      "error_code": "a1b2c3d4e5f60718",
      "error_title": "YandexCleaner: IOException [0x80070020]",
      "error_module": "YandexCleaner",
      "count": 3,
      "users": 2,
      "last_seen": 1762400000
    }
  ],
  "versions": [
    { "version": "2.2.0.0", "users": 80, "launches": 400 }
  ],
  "countries": [
    { "country": "RU", "users": 90 }
  ]
}
```

### `GET /api/v1/admin/errors`

Только при заданном `DT_ADMIN_TOKEN`:

```bash
curl -H "Authorization: Bearer $DT_ADMIN_TOKEN" \
     "http://127.0.0.1:8787/api/v1/admin/errors?limit=100"
```

---

## Production / боевой запуск

### Вариант 1: systemd + nginx/Caddy

```ini
# /etc/systemd/system/deeptools-telemetry.service
[Unit]
Description=DeepTools telemetry
After=network.target

[Service]
User=deeptools
WorkingDirectory=/opt/deeptools-telemetry
Environment=DT_DB=/var/lib/deeptools-telemetry/telemetry.db
Environment=DT_TRUST_PROXY=1
ExecStart=/usr/bin/python3 /opt/deeptools-telemetry/deeptools_telemetry.py
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Поставь перед сервером HTTPS-прокси (Caddy проще всего):

```
telemetry.example.com {
    reverse_proxy 127.0.0.1:8787
}
```

Если используешь Cloudflare, включи `DT_TRUST_PROXY=1`, чтобы получать
`CF-IPCountry`. IP при этом всё равно не сохраняется.

### Вариант 2: Docker

```bash
cd server
docker compose up -d --build
```

Данные лежат в `server/data/telemetry.db`.

### Вариант 3: Render / Railway / Fly.io / VPS

Подойдёт любой хостинг с Python 3.8+ и **постоянным диском** для SQLite.
Бесплатные тарифы без persistent disk данные потеряют. Для публичного релиза
лучше VPS или платный инстанс с volume.

---

## Ограничения и безопасность

- Лимит: 120 запросов в минуту с одного IP (в памяти процесса).
- Тело запроса: не больше 16 КБ.
- SQLite в режиме WAL, все запросы параметризованы.
- `/api/v1/online` отдаёт только число активных сессий.
- `/api/v1/stats` отдаёт только агрегаты; при заданном `DT_VIEW_TOKEN` требует токен.
- Токен приложения не встроен в exe: любой может отправить фейковые события.
  Для честной статистики этого достаточно, для защиты от целенаправленного
  спама поставь перед сервером rate limit на прокси.
- Регулярно делай backup `telemetry.db`.

---

## Что показывает панель

| Плитка | Что значит |
|---|---|
| Сейчас онлайн | сессии с heartbeat за последние 5 минут |
| За 24 часа | уникальные установки за сутки |
| Всего пользователей | уникальные `install_id` за всё время |
| Запусков сегодня | события `start` с полуночи UTC |
| Всего запусков | сумма запусков по установкам |
| Ошибок за 24 часа | события `error` за сутки |
| Людей с ошибками | уникальные установки с ошибкой за сутки |
| Всего ошибок | сумма ошибок по установкам |
| Топ ошибок | группировка по коду/типу, сколько людей и раз |
| Версии / страны | распределение установок |
