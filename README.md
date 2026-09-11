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

DeepTools is a system tweaker, so it does things real malware also does: elevates via UAC, edits the registry and services, sends synthetic input (the clicker/macros) and loads a kernel driver to read CPU/GPU temperatures. Because the release is **not code-signed yet**, some engines (mostly ML/heuristic detections like [Trojan:Win32/Wacatac.B!ml](https://www.microsoft.com/en-us/wdsi/threats/malware-encyclopedia-description?name=Trojan%3AWin32%2FWacatac.B!ml&threatid=2147735505)) may show a false positive. This is expected for this class of software.

- The full source is in this repository — you can read and build it yourself.
- You can verify any release on [VirusTotal](https://www.virustotal.com/).
- If Windows SmartScreen warns you, click **More info → Run anyway**.

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

DeepTools - это лёгкая утилита «всё в одном» для Windows 10 и 11, она чистит мусор, настраивает систему под игры, следит за температурами, позволяет управлять автозагрузкой и службами, удаляет предустановленный хлам и включает набор удобных инструментов: автокликер, запись макросов, скриншотер, менеджер буфера обмена и заметки. Один .exe файл, без установщика и без фоновых служб, остающихся в системе.

> [!IMPORTANT]
> DeepTools нужны **права администратора**, чтобы менять планы питания, службы и автозагрузку, а также читать датчики железа. При запуске программа запрашивает их через стандартное окно UAC.

### Почему антивирус может ругаться

DeepTools — системный твикер, поэтому он делает то же, что и настоящие вирусы: повышает права через UAC, правит реестр и службы, шлёт синтетический ввод (кликер/макросы) и грузит драйвер ядра для чтения температур CPU/GPU. Так как релиз **пока не подписан сертификатом**, некоторые движки (в основном ML/эвристика вроде [Trojan:Win32/Wacatac.B!ml](https://www.microsoft.com/en-us/wdsi/threats/malware-encyclopedia-description?name=Trojan%3AWin32%2FWacatac.B!ml&threatid=2147735505)) могут дать ложное срабатывание. Для такого класса программ это нормально.

- Весь исходный код — в этом репозитории, можно прочитать и собрать самому.
- Любой релиз можно проверить на [VirusTotal](https://www.virustotal.com/).
- Если предупреждает Windows SmartScreen — нажми **Подробнее → Выполнить в любом случае**.
- Иногда антивирус сразу же посылает файл в карантин, тогда **надо добавить его в исключение**


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
3. Запусти. Подтверди UAC, когда система попросит права администратора.

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

DeepTools проверяет GitHub Releases при запуске. Когда выходит новая версия, клик по уведомлению из трея **скачивает и устанавливает её автоматически** - программа сама подменяет свой exe и перезапускается.

---

<div align="center">

© 2026 dep1xar · DeepTools v1.9.0

</div>
