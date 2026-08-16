<div align="center">

# DeepTools

**Smart tweaker, cleaner and monitoring tool for Windows — in one app.**
**Умный твикер, чистильщик и монитор для Windows — всё в одном.**

[![Version](https://img.shields.io/badge/version-1.9.0-blue)](https://github.com/dep1xar/DeepTools/releases)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011-0078D6)](#)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-512BD4)](#)

**[English](#english) · [Русский](#русский)**

</div>

---

<a name="english"></a>

## English

DeepTools is a lightweight all-in-one utility for Windows 10 and 11. It cleans junk, tunes the system for gaming, watches your temperatures, manages startup and services, removes pre-installed bloat, and bundles a set of handy extras — a clicker, macro recorder, screenshot tool, clipboard manager and desktop sticky notes. One executable, no installer, no background services left behind.

> [!IMPORTANT]
> DeepTools requires **administrator rights** to change power plans, services and startup entries, and to read hardware sensors. On launch it asks for elevation via the standard Windows UAC prompt.

### Why it may be flagged by antivirus

DeepTools is a system tweaker, so it does things real malware also does: elevates via UAC, edits the registry and services, sends synthetic input (the clicker/macros) and loads a kernel driver to read CPU/GPU temperatures. Because the release is **not code-signed yet**, some engines (mostly ML/heuristic detections like `Trojan:Win32/Wacatac.B!ml`) may show a false positive. This is expected for this class of software.

- The full source is in this repository — you can read and build it yourself.
- You can verify any release on [VirusTotal](https://www.virustotal.com/).
- If Windows SmartScreen warns you, click **More info → Run anyway**.

### Features

| Section | What it does |
|---|---|
| **Home** | Quick tiles to jump to the most-used tools, live CPU/RAM/temperature monitoring, one-click RAM cleaner with **scheduled auto-clean** (every 5/10/30/60 min, pauses while a game is running), and an optional always-on-top desktop widget. |
| **Smart Cleanup** | Clears temp files, caches and shader caches by category, with a size preview before you delete. Includes **scheduled auto cleanup** (silent, with a tray report), a **program uninstaller** with leftover cleanup and a **Windows bloatware** remover. It can also **auto-clean the standby memory** (ISLC-style, kills micro-stutter under low RAM) and **auto-clear shader caches when the GPU driver changes**. |
| **Game Booster** | One-click "Ultimate Performance" power plan, disables core parking, boosts the running game's priority, optional auto-boost for new games, **Game DVR switch-off** (Game Bar background recording no longer eats FPS), **auto-pinning the game to the discrete GPU** on dual-GPU laptops, **freezing heavy background apps** (Discord/browser pause instead of being killed), **customizable FPS overlay** with accurate per-game FPS (PresentMon-style ETW tracing), 1% low and a frametime graph, on-screen **crosshair**, and Win-key blocking. Games are detected in both fullscreen and borderless mode. **Adaptive Power Profile** automatically switches the power plan by context — "Ultimate Performance" in a game, Balanced in a browser, Power Saver on idle — and backs off when FPS is already stable or CPU temperature is high. **VRAM Defrag** flushes fragmented video memory via DirectX on demand or automatically when fragmentation exceeds a threshold, eliminating stuttering in open-world games after hours of play without a restart. |
| **Health Check** | Live CPU/GPU/RAM load and temperatures, **fan RPM (CPU/GPU/case)**, overheat alerts with **user-adjustable CPU/GPU thresholds**, a **"time to clean the cooler" alert** when average CPU temperature keeps rising over the week, disk health (SMART), a **disk speed test** (sequential + random 4K, cache-bypassing, with an HDD/SSD/NVMe verdict), stress test, benchmark, BSOD analyzer and 24h temperature history. |
| **My PC** | Full system spec on one page with a **Copy** button — handy for forums, sales listings or sending to a friend. On laptops it also shows **battery health** (wear %, charge cycles, design vs current capacity) and **Windows uptime + boot-time trend**. |
| **Startup** | See and toggle what launches with Windows, plus boot-time analysis and a **suspicion rating** per entry (no digital signature + runs from Temp + random-looking name = red flag, with reasons on hover). Now also covers **Task Scheduler** logon/boot tasks — same suspicion rating and on/off toggle, where updaters and junk often hide outside the normal startup list. |
| **Services** | Enable/disable Windows services safely, with descriptions and a gaming preset. Includes a **Windows telemetry** switch-off panel (light/medium/aggressive presets, fully reversible). |
| **Visual Effects** | Toggle Windows animations and effects for a snappier feel. |
| **Clicker** | Configurable auto-clicker (mouse + keyboard, spam or hold) with hotkey and a **macro recorder**. |
| **Screenshots** | Full-screen and region capture (with arrows/boxes/text annotations) via global hotkeys. Uses direct GPU-to-disk transfer (**zero-latency screenshot**) powered by DirectStorage — no CPU copy through RAM means no frametime spike even in the heaviest scenes. |
| **Clipboard** | Clipboard history manager — text **and images**, with pinning and search, persists across restarts. |
| **Network** | Live download/upload speed graphs, session traffic counter, ping test (avg/min/max/jitter) and a per-process "who is using the network" table. **Latency Surgeon** automatically kills background update traffic (Windows Update, Steam, browsers) while an online game is running, reassigns network priorities via Windows QoS, detects and throttles parasitic upload/download, and surfaces packet loss and jitter in the FPS overlay. A built-in **DNS switcher** applies Cloudflare, Google or AdGuard DNS — or reverts to automatic (DHCP) — across all active adapters in one click and flushes the DNS cache, for faster site response and to bypass a slow ISP resolver. **Network tools** add a one-click **network-stack reset** (winsock/IP) and **per-app internet blocking** via Windows Firewall. |

### Extra tools

- **Macro recorder** — record mouse clicks and key presses with timing, replay with repeats or looping, save/load macros. Hotkeys: **F6** record, **F7** play (while the macro window is open).
- **Program uninstaller** — lists installed programs and runs their uninstaller, then finds and removes leftover folders.
- **Sticky notes** — yellow desktop notes that stay on top, live in the tray, and are restored on launch (tray menu → *New note*). Each note has a ⏰ **reminder**: set a time and get a tray notification with the note popping to the front.
- **Audio switcher** — switch the default playback device (headphones ↔ speakers) in one click from the tray menu.
- **Power quick actions** — restart straight into BIOS/UEFI, toggle Windows Fast Startup, restart Explorer.
- **Debloat** — removes pre-installed UWP apps you don't use. Only shows what's actually installed; removal is per-user and always confirmed.
- **Benchmark** — quick before/after CPU, RAM and disk test. Saves the result so you can see if your tweaks actually helped.
- **Stress Test** — loads all CPU cores to 100% for 2 minutes and reports peak temperature.
- **Temperature History** — 24-hour CPU/GPU graph with peak analysis.
- **Game Time** — tracks your gaming sessions (playtime, average FPS, 1% low, average CPU, peak temps) and reports when you close a game. Works with fullscreen and borderless games. A **Share** button renders a PNG card with your PC spec and top games. A per-game 📈 **FPS history** chart shows average FPS and 1% low across sessions with a trend verdict — so system degradation is visible at a glance.
- **Telemetry off** — disables Windows telemetry via official policies, services and scheduler tasks. Three presets (light/medium/aggressive), live status per tweak, one-click full revert.
- **Restore point** — offered automatically before risky tweaks (debloat, service presets, telemetry) so you can always roll back.
- **Crosshair** — customizable on-screen crosshair overlay (cross, dot, circle, T-shape); settings are remembered.
- **Desktop Widget** — compact always-on-top CPU/RAM/temperature panel.
- **RGB Reactive** — connects to OpenRGB to drive your lighting based on live sensor data: colour shifts from cool to hot as CPU/GPU temperature rises, pulses faster under heavy load, and flashes red when FPS drops below your threshold. Works with any OpenRGB-compatible device.

### Global hotkeys

| Key | Action |
|---|---|
| **F6** | Region screenshot |
| **F8** | Toggle clicker |
| **F9** | Full-screen screenshot |
| **F10** | Toggle FPS overlay |

### Installation

1. Go to the [Releases](https://github.com/dep1xar/DeepTools/releases) page.
2. Download the latest `DeepTools.exe`.
3. Run it. Approve the UAC prompt when Windows asks for administrator rights.

No installer, no dependencies to download — all libraries (including the hardware-sensor engine) are embedded into the single `.exe`.

### Building from source

You need the **.NET Framework 4.x** SDK (ships with Windows / Visual Studio; the compiler lives at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).

**Easiest way — double-click `build.bat`.** It runs the build and keeps the window open so you can read the result or any error. Make sure you downloaded the **whole repository** (Code → Download ZIP): every `.cs` file and every `.dll` must sit next to `build.ps1`, otherwise the build fails.

Or run it yourself from a PowerShell terminal (so the window doesn't close):

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The script compiles all `.cs` files, embeds the managed DLLs as resources and produces `DeepTools.exe` in the project folder.

### System requirements

- Windows 10 or Windows 11 (64-bit)
- .NET Framework 4.x (preinstalled on Windows 10/11)
- Administrator rights (for tweaks and sensors)

### Language

DeepTools ships in **English and Russian**. The very first launch asks you to pick a language (and shows a short quick-start wizard); you can change it any time in **Settings → Appearance & language**.

### Updates

DeepTools checks GitHub Releases on launch. When an update is available, clicking the tray notification **downloads and installs it automatically** — the app swaps its own exe and restarts.

---

<a name="русский"></a>

## Русский

DeepTools — это лёгкая утилита «всё в одном» для Windows 10 и 11. Она чистит мусор, настраивает систему под игры, следит за температурами, управляет автозагрузкой и службами, удаляет предустановленный хлам и включает набор удобных инструментов: кликер, запись макросов, скриншотер, менеджер буфера обмена и заметки-стикеры. Один исполняемый файл, без установщика и без фоновых служб, остающихся в системе.

> [!IMPORTANT]
> DeepTools нужны **права администратора**, чтобы менять планы питания, службы и автозагрузку, а также читать датчики железа. При запуске программа запрашивает их через стандартное окно UAC.

### Почему антивирус может ругаться

DeepTools — системный твикер, поэтому он делает то же, что и настоящие вирусы: повышает права через UAC, правит реестр и службы, шлёт синтетический ввод (кликер/макросы) и грузит драйвер ядра для чтения температур CPU/GPU. Так как релиз **пока не подписан сертификатом**, некоторые движки (в основном ML/эвристика вроде `Trojan:Win32/Wacatac.B!ml`) могут дать ложное срабатывание. Для такого класса программ это нормально.

- Весь исходный код — в этом репозитории, можно прочитать и собрать самому.
- Любой релиз можно проверить на [VirusTotal](https://www.virustotal.com/).
- Если предупреждает Windows SmartScreen — нажми **Подробнее → Выполнить в любом случае**.

### Возможности

| Раздел | Что делает |
|---|---|
| **Главная** | Плитки быстрого перехода, живой мониторинг CPU/RAM/температур, очистка RAM в один клик с **автоочисткой по расписанию** (каждые 5/10/30/60 минут, во время игры не запускается) и опциональный виджет поверх окон. |
| **Умная очистка** | Чистит временные файлы, кэши и кэши шейдеров по категориям, показывает размер до удаления. Включает **автоочистку по расписанию** (тихую, с отчётом из трея), **деинсталлятор программ** с чисткой хвостов и удаление **встроенного мусора Windows**. Умеет **авто-очистку standby-памяти** (в духе ISLC, убирает микрофризы при нехватке RAM) и **авто-очистку кэша шейдеров при смене драйвера GPU**. |
| **Игровой буст** | В один клик включает план «Максимальная производительность», отключает парковку ядер, поднимает приоритет игры, авто-буст новых игр, **отключение Game DVR** (фоновая запись Game Bar больше не ест FPS), **авто-закрепление игры за дискретной GPU** на ноутбуках с двумя видеокартами, **заморозка тяжёлого фона** (Discord/браузер ставятся на паузу вместо закрытия), **настраиваемый FPS-оверлей** с точным FPS игры (ETW-трейсинг в стиле PresentMon), 1% low и графиком фреймтайма, накладной **прицел** и блокировку клавиши Win. Игры определяются и в полноэкранном, и в borderless-режиме. **Адаптивный профиль питания** автоматически переключает план по контексту — максималка в игре, баланс в браузере, экономия на простое — и снижает агрессивность, если FPS уже стабильный или температура высокая. **Дефрагментация VRAM** сбрасывает фрагментированную видеопамять через DirectX по запросу или автоматически при превышении порога, устраняя подвисания в открытых мирах после часов игры без перезапуска. |
| **Проверка здоровья** | Нагрузка и температуры CPU/GPU/RAM, **обороты вентиляторов (CPU/GPU/корпус)**, тревога перегрева с **настраиваемыми порогами CPU/GPU**, алерт **«пора чистить кулер»**, когда средняя температура CPU растёт неделю подряд, здоровье диска (SMART), **тест скорости диска** (последовательный + случайный 4K, мимо кэша, с вердиктом HDD/SSD/NVMe), стресс-тест, бенчмарк, разбор синих экранов и история температур за 24 часа. |
| **Мой ПК** | Полная спека системы на одной странице с кнопкой **Копировать** — удобно для форумов, объявлений или отправки другу. На ноутбуках дополнительно показывает **здоровье батареи** (износ %, циклы, заводская vs текущая ёмкость) и **время работы + тренд загрузки Windows**. |
| **Автозагрузка** | Смотри и отключай то, что стартует с Windows, плюс анализ времени загрузки и **рейтинг подозрительности** каждой записи (нет подписи + запуск из Temp + случайное имя = красный флаг, причины — по наведению). Теперь охватывает и **Планировщик задач** (logon/boot-задачи) — тот же рейтинг и переключатель, где часто прячутся апдейтеры и мусор мимо обычной автозагрузки. |
| **Службы** | Безопасно включай/отключай службы Windows, с описаниями и игровым пресетом. Внутри — панель отключения **телеметрии Windows** (пресеты мягко/средне/жёстко, всё обратимо). |
| **Визуальные эффекты** | Отключай анимации и эффекты Windows для отзывчивости. |
| **Кликер** | Настраиваемый авто-кликер (мышь + клавиатура, спам или зажатие) с горячей клавишей и **запись макросов**. |
| **Скриншоты** | Снимок всего экрана и области (со стрелками/рамками/текстом) по глобальным горячим клавишам. Использует прямую передачу GPU→диск через DirectStorage (**мгновенный скриншот**) — никакого копирования через RAM, никаких фризов даже в самых тяжёлых сценах. |
| **Буфер обмена** | Менеджер истории — текст **и картинки**, с закреплением и поиском, сохраняется между запусками. |
| **Сеть** | Живые графики скорости загрузки/отдачи, счётчик трафика за сессию, пинг-тест (средний/мин/макс/джиттер) и таблица «кто использует сеть» по процессам. **Хирург латентности** автоматически режет фоновый трафик обновлений (Windows Update, Steam, браузеры) во время онлайн-игры, переназначает сетевые приоритеты через Windows QoS, детектирует и ограничивает паразитные upload/download, и выводит packet loss и джиттер в FPS-оверлее. Встроенный **DNS-переключатель** в один клик ставит DNS Cloudflare, Google или AdGuard — либо возвращает автоматический (DHCP) — на все активные адаптеры и сбрасывает кэш DNS: быстрее отклик сайтов и обход тормозного DNS провайдера. **Инструменты сети**: сброс сетевого стека (winsock/IP) в один клик и блокировка интернета приложениям через брандмауэр Windows. |

### Дополнительные инструменты

- **Запись макросов** — запись кликов мыши и нажатий клавиш с таймингами, воспроизведение с повторами или в цикле, сохранение/загрузка. Хоткеи: **F6** запись, **F7** воспроизведение (пока открыто окно макросов).
- **Деинсталлятор программ** — список установленного и запуск удаления, затем поиск и удаление остаточных папок.
- **Заметки-стикеры** — жёлтые заметки поверх стола, живут в трее, восстанавливаются при запуске (меню трея → *Новая заметка*). У каждой есть ⏰ **напоминание**: задай время — придёт уведомление из трея, а заметка выпрыгнет наверх.
- **Переключатель звука** — смена устройства вывода (наушники ↔ колонки) одним кликом из меню трея.
- **Быстрые действия питания** — перезагрузка прямо в BIOS/UEFI, тумблер быстрого запуска Windows, перезапуск проводника.
- **Деблоат** — удаляет предустановленные UWP-приложения. Показывает только реально установленное; удаление для текущего пользователя и всегда с подтверждением.
- **Бенчмарк** — быстрый тест CPU, RAM и диска «до/после». Сохраняет результат, чтобы увидеть реальный прирост.
- **Стресс-тест** — грузит все ядра CPU на 100% в течение 2 минут и показывает пиковую температуру.
- **История температур** — график CPU/GPU за 24 часа с разбором пиков.
- **Время в играх** — считает игровые сессии (время, средний FPS, 1% low, средний CPU, пиковые температуры) и шлёт отчёт при закрытии игры. Работает с полноэкранными и borderless-играми. Кнопка **Поделиться** рендерит PNG-карточку со спекой ПК и топом игр. График 📈 **истории FPS** по каждой игре — средний FPS и 1% low по сессиям с вердиктом тренда: деградация системы видна с одного взгляда.
- **Отключение телеметрии** — выключает телеметрию Windows через официальные политики, службы и задачи планировщика. Три пресета (мягко/средне/жёстко), живой статус каждого твика, полный откат одной кнопкой.
- **Точка восстановления** — автоматически предлагается перед рискованными твиками (деблоат, пресеты служб, телеметрия), чтобы всегда можно было откатиться.
- **Прицел** — настраиваемый накладной прицел (крест, точка, круг, T-образный); настройки запоминаются.
- **Виджет на рабочий стол** — компактная плашка CPU/RAM/температуры, всегда поверх окон.
- **RGB-реакция** — подключается к OpenRGB и управляет подсветкой по живым датчикам: цвет плавно меняется от холодного к горячему по температуре CPU/GPU, пульсирует быстрее под нагрузкой и мигает красным при просадке FPS ниже вашего порога. Работает с любым устройством, поддерживаемым OpenRGB.

### Глобальные горячие клавиши

| Клавиша | Действие |
|---|---|
| **F6** | Скриншот области |
| **F8** | Вкл/выкл кликер |
| **F9** | Скриншот всего экрана |
| **F10** | Вкл/выкл FPS-оверлей |

### Установка

1. Открой страницу [Releases](https://github.com/dep1xar/DeepTools/releases).
2. Скачай свежий `DeepTools.exe`.
3. Запусти. Подтверди запрос UAC, когда Windows попросит права администратора.

Без установщика и без докачки зависимостей — все библиотеки (включая движок датчиков) встроены прямо в один `.exe`.

### Сборка из исходников

Нужен **.NET Framework 4.x** SDK (идёт с Windows / Visual Studio; компилятор лежит по пути `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`).

**Самый простой способ — двойной клик по `build.bat`.** Он запускает сборку и оставляет окно открытым, чтобы можно было прочитать результат или текст ошибки. Убедись, что скачал **весь репозиторий** (Code → Download ZIP): рядом с `build.ps1` должны лежать все `.cs` и все `.dll`, иначе сборка не пройдёт.

Или запусти вручную из терминала PowerShell (чтобы окно не закрылось):

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Скрипт компилирует все `.cs`-файлы, встраивает managed-библиотеки как ресурсы и создаёт `DeepTools.exe` в папке проекта.

### Системные требования

- Windows 10 или Windows 11 (64-бит)
- .NET Framework 4.x (предустановлен в Windows 10/11)
- Права администратора (для твиков и датчиков)

### Язык

DeepTools доступен на **русском и английском**. При самом первом запуске программа спрашивает язык (и показывает короткий мастер знакомства); сменить можно в любой момент в **Настройки → Внешний вид и язык**.

### Обновления

DeepTools проверяет GitHub Releases при запуске. Когда выходит новая версия, клик по уведомлению из трея **скачивает и устанавливает её автоматически** — программа сама подменяет свой exe и перезапускается.

---

<div align="center">

© 2026 dep1xar · DeepTools v1.9.0

</div>
