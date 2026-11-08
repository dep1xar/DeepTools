# DeepTools telemetry на Cloudflare (Workers + Pages)

Тот же API, что у Python-сервера:

- `POST /api/v1/event` — `start` / `heartbeat` / `exit` / `error`
- `GET /api/v1/online` — публичный счётчик онлайна
- `GET /api/v1/stats` — полная статистика (нужен `DT_VIEW_TOKEN`)
- `GET /api/v1/admin/errors` — подробные ошибки (нужен `DT_ADMIN_TOKEN`)
- `GET /health`

Данные лежат в D1 (SQLite). IP-адреса не сохраняются; страна берётся из
заголовка `CF-IPCountry`, который ставит Cloudflare.

## Текущий деплой

- D1: `deeptools-telemetry` (регион EEUR) — общая база.
- Pages: `deeptools-telemetry` → **`https://deeptools-telemetry.pages.dev`**
  — этот адрес вшит в клиент.
- Worker: `deeptools-telemetry` → `https://deeptools-telemetry.deeptools.workers.dev`
  — оставлен для ежедневной очистки старых событий по cron. Его адрес в России
  часто блокируется провайдером на уровне TLS/SNI, поэтому клиент ходит на
  `pages.dev`.
- Секрет `DT_VIEW_TOKEN` установлен и на Pages, и на Worker. Значение лежит
  локально в `.stats-token` (файл в `.gitignore`, в репозиторий не попадает).

> `*.pages.dev` с сети пользователя открывается, `*.workers.dev` — нет.
> Поэтому публичный API обслуживает Pages Functions (`server/pages`), а cron —
> Worker. База у них одна, статистика не разделяется.

## Обновить Pages (публичный API)

```powershell
cd C:\Users\PC\Desktop\DeepTools\server\pages
npx --yes wrangler@latest pages deploy public --project-name deeptools-telemetry --branch main --commit-dirty=true
```

Файл `public/_worker.js` — копия `server/worker/src/index.js` без обработчика
`scheduled` (в Pages cron недоступен). При изменении API правь оба файла.

## Обновить Worker (cron-очистка)

```powershell
cd C:\Users\PC\Desktop\DeepTools\server\worker
npx --yes wrangler@latest deploy
```

## Обновить схему D1

```powershell
cd C:\Users\PC\Desktop\DeepTools\server\worker
npx --yes wrangler@latest d1 execute deeptools-telemetry --remote --file=schema.sql --yes
```

## Сменить токен просмотра

```powershell
cd C:\Users\PC\Desktop\DeepTools\server\worker
$token = node -e "console.log(require('crypto').randomBytes(24).toString('hex'))"
Set-Content .stats-token $token -NoNewline -Encoding ascii

# Важно: cmd type, а не PowerShell-пайп. PowerShell добавляет перевод строки,
# из-за чего токен в секрете перестаёт совпадать с файлом.
cmd /c "type .stats-token | npx --yes wrangler@latest secret put DT_VIEW_TOKEN"

cd ..\pages
cmd /c "type ..\worker\.stats-token | npx --yes wrangler@latest pages secret put DT_VIEW_TOKEN --project-name deeptools-telemetry"
npx --yes wrangler@latest pages deploy public --project-name deeptools-telemetry --branch main --commit-dirty=true
```

После смены токена пересобери `DeepToolsStats.exe` с новым `-StatsToken`.

## Собрать клиент с этим сервером

```powershell
cd C:\Users\PC\Desktop\DeepTools
$token = (Get-Content server\worker\.stats-token -Raw).Trim()
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 `
  -TelemetryUrl "https://deeptools-telemetry.pages.dev" `
  -StatsToken $token
```

Публичный `DeepTools.exe` не содержит токен: он попадает только в
`DeepToolsStats.exe`. Если сервер не настроен, счётчик показывает «—».

## Свой домен (необязательно)

Всё уже работает бесплатно на `pages.dev`. Если позже понадобится свой адрес:

1. Добавить домен в Cloudflare и дождаться статуса Active.
2. Открыть Pages-проект → Custom domains → добавить `telemetry.example.com`.
3. Проверить `https://telemetry.example.com/health` и `/api/v1/online`.
4. Собрать клиент с новым `-TelemetryUrl`.
