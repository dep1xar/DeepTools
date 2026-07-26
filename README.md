<div align="center">

# DeepTools

**Smart tweaker, cleaner and monitoring tool for Windows — in one app.**
**Умный твикер, чистильщик и монитор для Windows — всё в одном.**

[![Version](https://img.shields.io/badge/version-1.5.0-blue)](https://github.com/dep1xar/DeepTools/releases)
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
| **Home** | Quick tiles to jump to the most-used tools, live CPU/RAM/temperature monitoring, and an optional always-on-top desktop widget. |
| **Smart Cleanup** | Clears temp files, caches and shader caches by category, with a size preview before you delete. Includes a **program uninstaller** with leftover cleanup and a **Windows bloatware** remover. |
| **Game Booster** | One-click "Ultimate Performance" power plan, disables core parking, boosts the running game's priority, optional auto-boost for new games, **customizable FPS overlay** with accurate per-game FPS (PresentMon-style ETW tracing), 1% low and a frametime graph, on-screen **crosshair**, and Win-key blocking. Games are detected in both fullscreen and borderless mode. |
| **Health Check** | Live CPU/GPU/RAM load and temperatures, overheat alerts with **user-adjustable CPU/GPU thresholds**, disk health (SMART), stress test, benchmark, BSOD analyzer and 24h temperature history. |
| **My PC** | Full system spec on one page with a **Copy** button — handy for forums, sales listings or sending to a friend. |
| **Startup** | See and toggle what launches with Windows, plus boot-time analysis. |
| **Services** | Enable/disable Windows services safely, with descriptions and a gaming preset. |
| **Visual Effects** | Toggle Windows animations and effects for a snappier feel. |
| **Clicker** | Configurable auto-clicker (mouse + keyboard, spam or hold) with hotkey, plus a **macro recorder**. |
| **Screenshots** | Full-screen and region capture (with arrows/boxes/text annotations) via global hotkeys. |
| **Clipboard** | Clipboard history manager — text **and images**, with pinning and search, persists across restarts. |
| **Network** | Live download/upload speed graphs, session traffic counter, ping test (avg/min/max/jitter) and a per-process "who is using the network" table. |

### Extra tools

- **Macro recorder** — record mouse clicks and key presses with timing, replay with repeats or looping, save/load macros. Hotkeys: **F6** record, **F7** play (while the macro window is open).
- **Program uninstaller** — lists installed programs and runs their uninstaller, then finds and removes leftover folders.
- **Sticky notes** — yellow desktop notes that stay on top, live in the tray, and are restored on launch (tray menu → *New note*).
- **Power quick actions** — restart straight into BIOS/UEFI, toggle Windows Fast Startup, restart Explorer.
- **Debloat** — removes pre-installed UWP apps you don't use. Only shows what's actually installed; removal is per-user and always confirmed.
- **Benchmark** — quick before/after CPU, RAM and disk test. Saves the result so you can see if your tweaks actually helped.
- **Stress Test** — loads all CPU cores to 100% for 2 minutes and reports peak temperature.
- **Temperature History** — 24-hour CPU/GPU graph with peak analysis.
- **Game Time** — tracks your gaming sessions (playtime, average CPU, peak temps) and reports when you close a game. Works with fullscreen and borderless games.
- **Crosshair** — customizable on-screen crosshair overlay (cross, dot, circle, T-shape); settings are remembered.
- **Desktop Widget** — compact always-on-top CPU/RAM/temperature panel.

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

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

The script compiles all `.cs` files, embeds the managed DLLs as resources and produces `DeepTools.exe` in the project folder.

### System requirements

- Windows 10 or Windows 11 (64-bit)
- .NET Framework 4.x (preinstalled on Windows 10/11)
- Administrator rights (for tweaks and sensors)

### Language

DeepTools ships in **English and Russian**. On first launch it picks the language from your Windows locale; you can change it any time in **Settings → Appearance & language**.

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
| **Главная** | Плитки быстрого перехода, живой мониторинг CPU/RAM/температур и опциональный виджет поверх окон. |
| **Умная очистка** | Чистит временные файлы, кэши и кэши шейдеров по категориям, показывает размер до удаления. Включает **деинсталлятор программ** с чисткой хвостов и удаление **встроенного мусора Windows**. |
| **Игровой буст** | В один клик включает план «Максимальная производительность», отключает парковку ядер, поднимает приоритет игры, авто-буст новых игр, **настраиваемый FPS-оверлей** с точным FPS игры (ETW-трейсинг в стиле PresentMon), 1% low и графиком фреймтайма, накладной **прицел** и блокировку клавиши Win. Игры определяются и в полноэкранном, и в borderless-режиме. |
| **Проверка здоровья** | Нагрузка и температуры CPU/GPU/RAM, тревога перегрева с **настраиваемыми порогами CPU/GPU**, здоровье диска (SMART), стресс-тест, бенчмарк, разбор синих экранов и история температур за 24 часа. |
| **Мой ПК** | Полная спека системы на одной странице с кнопкой **Копировать** — удобно для форумов, объявлений или отправки другу. |
| **Автозагрузка** | Смотри и отключай то, что стартует с Windows, плюс анализ времени загрузки. |
| **Службы** | Безопасно включай/отключай службы Windows, с описаниями и игровым пресетом. |
| **Визуальные эффекты** | Отключай анимации и эффекты Windows для отзывчивости. |
| **Кликер** | Настраиваемый авто-кликер (мышь + клавиатура, спам или зажатие) с горячей клавишей и **запись макросов**. |
| **Скриншоты** | Снимок всего экрана и области (со стрелками/рамками/текстом) по глобальным горячим клавишам. |
| **Буфер обмена** | Менеджер истории — текст **и картинки**, с закреплением и поиском, сохраняется между запусками. |
| **Сеть** | Живые графики скорости загрузки/отдачи, счётчик трафика за сессию, пинг-тест (средний/мин/макс/джиттер) и таблица «кто использует сеть» по процессам. |

### Дополнительные инструменты

- **Запись макросов** — запись кликов мыши и нажатий клавиш с таймингами, воспроизведение с повторами или в цикле, сохранение/загрузка. Хоткеи: **F6** запись, **F7** воспроизведение (пока открыто окно макросов).
- **Деинсталлятор программ** — список установленного и запуск удаления, затем поиск и удаление остаточных папок.
- **Заметки-стикеры** — жёлтые заметки поверх стола, живут в трее, восстанавливаются при запуске (меню трея → *Новая заметка*).
- **Быстрые действия питания** — перезагрузка прямо в BIOS/UEFI, тумблер быстрого запуска Windows, перезапуск проводника.
- **Деблоат** — удаляет предустановленные UWP-приложения. Показывает только реально установленное; удаление для текущего пользователя и всегда с подтверждением.
- **Бенчмарк** — быстрый тест CPU, RAM и диска «до/после». Сохраняет результат, чтобы увидеть реальный прирост.
- **Стресс-тест** — грузит все ядра CPU на 100% в течение 2 минут и показывает пиковую температуру.
- **История температур** — график CPU/GPU за 24 часа с разбором пиков.
- **Время в играх** — считает игровые сессии (время, средний CPU, пиковые температуры) и шлёт отчёт при закрытии игры. Работает с полноэкранными и borderless-играми.
- **Прицел** — настраиваемый накладной прицел (крест, точка, круг, T-образный); настройки запоминаются.
- **Виджет на рабочий стол** — компактная плашка CPU/RAM/температуры, всегда поверх окон.

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

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Скрипт компилирует все `.cs`-файлы, встраивает managed-библиотеки как ресурсы и создаёт `DeepTools.exe` в папке проекта.

### Системные требования

- Windows 10 или Windows 11 (64-бит)
- .NET Framework 4.x (предустановлен в Windows 10/11)
- Права администратора (для твиков и датчиков)

### Язык

DeepTools доступен на **русском и английском**. При первом запуске язык выбирается по локали Windows; сменить можно в любой момент в **Настройки → Внешний вид и язык**.

---

<div align="center">

© 2026 dep1xar · DeepTools v1.5.0

</div>
