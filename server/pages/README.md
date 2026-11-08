# DeepTools telemetry на Cloudflare Pages

Публичный API для клиента: **`https://deeptools-telemetry.pages.dev`**.

Этот каталог — Pages-версия того же API, что лежит в `../worker`. Она нужна
потому, что `*.workers.dev` в России блокируется провайдером, а `*.pages.dev`
открывается. База D1 общая с Worker'ом, поэтому статистика не разделяется.

- `public/_worker.js` — весь API одним файлом (без cron).
- `wrangler.toml` — привязка к D1 `deeptools-telemetry`.
- Cron-очистка старых записей остаётся на Worker'е (`../worker`).

## Деплой

```powershell
cd C:\Users\PC\Desktop\DeepTools\server\pages
npx --yes wrangler@latest pages deploy public --project-name deeptools-telemetry --branch main --commit-dirty=true
```

## Секрет

```powershell
cd C:\Users\PC\Desktop\DeepTools\server\pages
cmd /c "type ..\worker\.stats-token | npx --yes wrangler@latest pages secret put DT_VIEW_TOKEN --project-name deeptools-telemetry"
npx --yes wrangler@latest pages deploy public --project-name deeptools-telemetry --branch main --commit-dirty=true
```

После изменения секрета нужен новый деплой Pages, иначе старое значение
останется в текущей версии.

## Проверка

```powershell
curl.exe https://deeptools-telemetry.pages.dev/health
curl.exe https://deeptools-telemetry.pages.dev/api/v1/online
curl.exe -H "Authorization: Bearer <токен>" https://deeptools-telemetry.pages.dev/api/v1/stats
```

## TLS 1.2 и 1.3

У части провайдеров TLS 1.2 до `pages.dev` не проходит (запрос висит и
обрывается по таймауту), а TLS 1.3 работает. Клиент включает TLS 1.3 на
Windows 11 / .NET Framework 4.8.1 и оставляет TLS 1.2 как запасной вариант.
На старой Windows, где TLS 1.3 нет, счётчик может показывать «—» — это
ограничение сети, а не сервера.
