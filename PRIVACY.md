# DeepTools Privacy Policy / Политика конфиденциальности

**Last updated / Обновлено:** 2026-11-07

---

## English

### Summary

DeepTools can send **anonymous usage statistics** so the app can show how many people are using it and which errors occur most often. Telemetry is **enabled by default** and can be turned off at any time.

On first launch DeepTools asks you to accept this policy with a mandatory checkbox; the app does not start without that consent. Telemetry itself is controlled by a toggle in Settings and is enabled by default.

The telemetry server is **not** a profiling or advertising system. It does not store IP addresses, names, file paths, clipboard contents, search queries or exception messages/stacks.

### What is collected

When telemetry is enabled, DeepTools sends:

- a **random installation ID** generated on the device (32 hex characters, not derived from hardware, Windows account or Microsoft/Google account);
- a **random session ID** generated for each app launch;
- the DeepTools version;
- the Windows product name, version and build number (for example `Windows 11 Pro 24H2 (26100)`);
- the interface language (`ru` or `en`);
- the event type: `start`, `heartbeat`, `exit` or `error`;
- the server-side event time;
- for errors only: the module name, the exception type and the HRESULT, converted into a short hashed **error signature** (for example `YandexCleaner: IOException [0x80070020]`).

If the server is deployed behind a trusted reverse proxy (for example Cloudflare), the proxy may pass a **two-letter country code** (`CF-IPCountry`). The country is stored only as an aggregate counter. The IP address itself is not written to the DeepTools database.

### What is NOT collected

DeepTools telemetry does **not** collect or store:

- your name, email, Microsoft/Google account or any account identifier;
- IP addresses in the telemetry database;
- hardware serial numbers, MAC addresses, disk IDs or other permanent device identifiers;
- file names, file paths, folder names or contents of your files;
- window titles, clipboard contents, keystrokes, screenshots or browser history;
- search queries;
- the text of exception messages, stack traces or local log contents;
- any data from the tools themselves (cleanup results, tweak selections, notes, macros, etc.).

### Online counter and full dashboard

The regular DeepTools window shows only a small counter: **“Using DeepTools now: N”**. To draw it, the app requests the public `/api/v1/online` endpoint, which returns just the number of active sessions.

The full dashboard (launches, errors, versions, countries) is a separate program, `DeepToolsStats.exe`, intended for the author and trusted helpers. It does not send telemetry. If the server sets `DT_VIEW_TOKEN`, the full `/api/v1/stats` endpoint requires that token, which is embedded into the viewer build. A token in a distributed executable is not a cryptographic secret — it limits casual access, not a determined person.

### Why it is collected

The data is used only to:

- show the aggregate “DeepTools Online” dashboard (online users, launches, errors);
- understand which versions people use;
- find the most frequent crash/error types and fix them faster;
- measure whether a release reduced errors.

The full dashboard shows **aggregates only**. It does not expose individual installations or sessions.

### Legal basis and control

Anonymous telemetry is enabled by default and can be turned off at any time:

- **Settings → Anonymous telemetry** — turn the toggle off;
- or edit `%APPDATA%\DeepTools\config.txt` and set `telemetry_enabled=0`.

After disabling, DeepTools stops sending events immediately and the small online counter on the home screen stops updating. The app keeps working; updates, tweaks and every other feature are unaffected.

### Storage and retention

- Data is stored in a SQLite database on the telemetry server.
- Raw error events are kept for **90 days** by default (configurable with `DT_RETENTION_DAYS`).
- Aggregate counters (total installs, launches, error counts) may be kept longer because they no longer identify a session.
- Access is limited to the person operating the server.
- If the server is hosted by a third-party provider or behind a CDN, that provider may process connection metadata (including IP addresses) under its own privacy policy. DeepTools itself does not write IP addresses to its database.

### Security

- Use HTTPS for the production telemetry endpoint.
- The server uses parameterized SQL queries, request size limits and rate limiting.
- The telemetry endpoint is public by design: a client-side token embedded in an open-source app cannot be kept secret. Do not send sensitive data to it.
- The server source code is in [`server/`](server/) and can be audited or self-hosted.

### Other network requests

DeepTools also checks GitHub Releases for updates. That request goes to GitHub, and GitHub may process your IP address and request metadata under the [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement). This is separate from DeepTools telemetry.

### Your rights

Depending on your jurisdiction (for example GDPR or CCPA), you may have the right to access, correct or delete your data. Because the installation ID is random and not linked to your identity, the simplest way to “reset” it is:

1. Close DeepTools.
2. Open `%APPDATA%\DeepTools\config.txt`.
3. Delete the `telemetry_install_id=...` line.
4. Start DeepTools again — a new random ID will be generated.

For other requests, open an issue at <https://github.com/dep1xar/DeepTools/issues>.

### Changes

This policy may be updated when the telemetry format or the server changes. The “Last updated” date at the top will be changed accordingly. Material changes will be mentioned in the release notes.

### Contact

- GitHub Issues: <https://github.com/dep1xar/DeepTools/issues>
- Repository: <https://github.com/dep1xar/DeepTools>

---

## Русский

### Кратко

DeepTools может отправлять **анонимную статистику использования**, чтобы программа показывала, сколько людей пользуется ей сейчас, и чтобы быстрее находить частые ошибки. Телеметрия **включена по умолчанию** и её можно выключить в любой момент.

При первом запуске DeepTools просит принять эту политику, поставив обязательную галочку; без согласия программа не запустится. Сама телеметрия управляется тумблером в Настройках и включена по умолчанию.

Сервер телеметрии — это **не** система профилирования или рекламы. Он не хранит IP-адреса, имена, пути к файлам, содержимое буфера обмена, поисковые запросы и тексты исключений со стеками.

### Какие данные собираются

Когда телеметрия включена, DeepTools отправляет:

- **случайный ID установки**, сгенерированный на устройстве (32 hex-символа; не связан с железом, учётной записью Windows или аккаунтом Microsoft/Google);
- **случайный ID сессии**, новый для каждого запуска программы;
- версию DeepTools;
- название, версию и сборку Windows (например, `Windows 11 Pro 24H2 (26100)`);
- язык интерфейса (`ru` или `en`);
- тип события: `start`, `heartbeat`, `exit` или `error`;
- время события по серверу;
- только для ошибок: имя модуля, тип исключения и HRESULT, преобразованные в короткую хешированную **подпись ошибки** (например, `YandexCleaner: IOException [0x80070020]`).

Если сервер развёрнут за доверенным reverse proxy (например, Cloudflare), прокси может передать **двухбуквенный код страны** (`CF-IPCountry`). Страна хранится только как агрегированный счётчик. Сам IP-адрес в базу DeepTools не записывается.

### Какие данные НЕ собираются

Телеметрия DeepTools **не** собирает и не хранит:

- имя, email, аккаунт Microsoft/Google или любой другой идентификатор аккаунта;
- IP-адреса в базе телеметрии;
- серийные номера железа, MAC-адреса, ID дисков и другие постоянные идентификаторы устройства;
- имена файлов, пути, названия папок и содержимое ваших файлов;
- заголовки окон, содержимое буфера обмена, нажатия клавиш, скриншоты и историю браузера;
- поисковые запросы;
- текст исключений, стек-трейсы и содержимое локальных логов;
- данные из самих инструментов (результаты очистки, выбранные твики, заметки, макросы и т.п.).

### Онлайн-счётчик и полная панель

Обычное окно DeepTools показывает только небольшой счётчик: **«Сейчас пользуются DeepTools: N»**. Для него программа запрашивает публичный эндпоинт `/api/v1/online`, который возвращает лишь число активных сессий.

Полная панель (запуски, ошибки, версии, страны) — отдельная программа `DeepToolsStats.exe` для автора и доверенных помощников. Она не отправляет телеметрию. Если на сервере задан `DT_VIEW_TOKEN`, полный эндпоинт `/api/v1/stats` требует этот токен, и он вшивается в сборку программы просмотра. Токен в распространяемом exe не является криптографическим секретом: он ограничивает случайный доступ, но не защищает от того, кто задался целью.

### Зачем это нужно

Данные используются только чтобы:

- показывать агрегированную статистику сообщества (онлайн, запуски, ошибки);
- понимать, какими версиями пользуются люди;
- находить самые частые типы падений/ошибок и быстрее их исправлять;
- проверять, стало ли меньше ошибок после релиза.

Полная панель показывает **только агрегаты**. Отдельные установки и сессии не раскрываются.

### Правовое основание и управление

Анонимная телеметрия включена по умолчанию, и её можно выключить в любой момент:

- **Настройки → Анонимная телеметрия** — выключить тумблер;
- или открыть `%APPDATA%\DeepTools\config.txt` и установить `telemetry_enabled=0`.

После выключения DeepTools сразу прекращает отправку событий, и небольшой счётчик онлайна на главной перестаёт обновляться. Программа продолжает работать; обновления, твики и остальные функции не затрагиваются.

### Хранение и сроки

- Данные хранятся в базе SQLite на сервере телеметрии.
- Сырые события ошибок по умолчанию хранятся **90 дней** (настраивается через `DT_RETENTION_DAYS`).
- Агрегированные счётчики (всего установок, запусков, ошибок) могут храниться дольше, поскольку уже не идентифицируют сессию.
- Доступ есть только у человека, обслуживающего сервер.
- Если сервер размещён у стороннего хостинг-провайдера или за CDN, провайдер может обрабатывать метаданные соединения (включая IP-адреса) по своей политике конфиденциальности. Сам DeepTools IP-адреса в свою базу не пишет.

### Безопасность

- Для боевого сервера используйте HTTPS.
- Сервер использует параметризованные SQL-запросы, ограничение размера запроса и rate limit.
- Эндпоинт телеметрии публичен по своей природе: токен в клиенте open-source приложения невозможно сохранить в секрете. Не отправляйте туда чувствительные данные.
- Исходный код сервера лежит в [`server/`](server/) — его можно проверить или развернуть самостоятельно.

### Другие сетевые запросы

DeepTools также проверяет обновления на GitHub Releases. Этот запрос идёт на GitHub, и GitHub может обрабатывать ваш IP-адрес и метаданные запроса согласно [GitHub Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement). Это не связано с телеметрией DeepTools.

### Ваши права

В зависимости от вашей юрисдикции (например, GDPR или CCPA) у вас могут быть права на доступ, исправление или удаление данных. Поскольку ID установки случайный и не связан с вашей личностью, проще всего «сбросить» его так:

1. Закройте DeepTools.
2. Откройте `%APPDATA%\DeepTools\config.txt`.
3. Удалите строку `telemetry_install_id=...`.
4. Запустите DeepTools снова — будет создан новый случайный ID.

По остальным вопросам создайте issue: <https://github.com/dep1xar/DeepTools/issues>.

### Изменения

Политика может обновляться при изменении формата телеметрии или сервера. Дата «Обновлено» вверху будет изменена. О существенных изменениях будет сказано в примечаниях к релизу.

### Контакты

- GitHub Issues: <https://github.com/dep1xar/DeepTools/issues>
- Репозиторий: <https://github.com/dep1xar/DeepTools>
