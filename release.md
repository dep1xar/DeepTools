# DeepTools v2.1.1

## Security & Stability Update

### 🔒 Безопасность

- **UpdateChecker / SelfUpdater** — защита от RCE:
  - `github_repo` из `config.txt` теперь проверяется по белому списку (`dep1xar/DeepTools`); произвольный репо игнорируется
  - URL ассета валидируется (только `https` + `github.com` / `githubusercontent.com`)
  - Скачанный файл проверяется на PE-сигнатуру `MZ`; HTML-ошибки не устанавливаются
  - Временные файлы с рандомным именем (`Guid`) — защита от TOCTOU / предсказуемой подмены в `%TEMP%`
  - `busy` теперь атомарный (`Interlocked.CompareExchange`) — нет гонки при двойном клике
  - BAT-скрипт обновления тоже с рандомным именем
- **UninstallerForm** — защита от Command Injection:
  - `UninstallString` из реестра больше не запускается через `cmd.exe /c` — парсится вручную и запускается напрямую `ProcessStartInfo(exe, args)`
  - Защита от инъекции через `& | && ||` в строке деинсталлятора
- **SystemCleanup** — защита от удаления через symlink/junction:
  - Проверка `FileAttributes.ReparsePoint` перед `takeown / rd /s /q` — junction на `C:\Windows` не удалит систему
  - То же для `SoftwareDistribution\Download`
  - `RunCmd` теперь читает `stdout` + `stderr` асинхронно (защита от deadlock) и убивает процесс по таймауту
- **SensorDriver** — случайное имя файла в `%TEMP%` (`PawnIO_setup_<Guid>.exe`, `FileMode.CreateNew`) вместо предсказуемого `PawnIO_setup.exe`
- **DebloatForm** — `RunPowerShell` теперь читает `stdout`/`stderr` асинхронно, убивает зависший `powershell.exe` по таймауту, `Dispose()`
- **UninstallerForm / LeftoverForm** — защита от удаления не той папки:
  - Строгий матчинг: `StrictMatch` (точное совпадение или префикс + разделитель ` -_.(`), вместо `IndexOf` (матчил `One` → `OneDrive`)
  - Чёрный список системных папок (`Windows`, `Microsoft`, `Common Files` и т.д.)
  - Проверка `ReparsePoint` и защита от удаления корней (`Windows`, `Program Files`, `Users`)
- **AppConfig** — потокобезопасность: `lock(sync)` на всех операциях, атомарное сохранение `temp → File.Replace/Move`
- **PresentTracer** — `refCount` теперь атомарный (`Interlocked.Increment/Decrement`), защита от ухода в минус
- **EmbeddedAssemblies** — `Install()` теперь под `lock` (гонка при параллельной инициализации)

### 🎮 Игровые твики — проверка работоспособности

Проверено 10 твиков в `GameTweaksPanel.cs`:

| # | Твик | Реестр / API | Статус |
|---|------|-------------|--------|
| 1 | Отключить HAGS | `HKLM\...\GraphicsDrivers\HwSchMode = 1` (1=off, 2=on) | ✅ Работает, бэкап + откат |
| 2 | Включить Game Mode | `HKCU\...\GameBar\AutoGameModeEnabled + AllowAutoGameMode = 1` | ✅ Работает |
| 3 | Отключить Fullscreen Optimizations | `HKCU\System\GameConfigStore` (4 значения) | ✅ Работает, бэкап всех 4 |
| 4 | CPU Affinity | `Process.ProcessorAffinity & ~1` | ✅ Работает, откат по маске |
| 5 | Авто-файл подкачки | `HKLM\...\Memory Management\PagingFiles = "0 0"` | ✅ Работает, нужен ребут, бэкап MULTI_SZ |
| 6 | Сбалансированный план питания | `powercfg /setactive <BalancedGuid>` | ✅ Работает, помнит предыдущий план |
| 7 | Отключить PCIe LPM (ASPM) | `powercfg /setacvalueindex + /setdcvalueindex` | ✅ Работает, AC+DC, откат |
| 8 | Отключить лишние службы | `HKLM\...\Services\<name>\Start = 4` + `sc stop/start` | ✅ Только существующие службы, бэкап |
| 9 | Отключить HVCI | `HKLM\...\DeviceGuard\...\Enabled = 0` | ✅ Работает, dangerous + ребут |
| 10 | Отключить Power Throttling | `HKLM\...\PowerThrottling\PowerThrottlingOff = 1` | ✅ Работает |

Все твики читают реальное состояние системы (реестр), а не кэш. `RevertDword` корректно откатывает к исходному значению или удаляет ключ. `RunPowercfg` проверяет `ExitCode`.

### 🖥️ Интерфейс — исправлена "пиксельность"

Причина: приложение не было DPI-aware — Windows растягивала битмап билинейно.

- `app.manifest` — добавлен `PerMonitorV2` + `dpiAware` + `supportedOS` (Win8.1/10/11)
- `program.cs` — `TryEnableDpiAwareness()` до любых UI-операций (`SetProcessDpiAwareness(2)` → fallback `SetProcessDPIAware`)
- `NativeMethods.cs` — добавлены `SetProcessDPIAware`, `LoadLibrary`, `GetProcAddress`, `SetProcessDpiAwarenessDelegate`
- `uicontrols.cs` — `ClearTypeGridFit` + `AntiAlias` + `HighQuality` на всех кастомных контролах (`SidebarNavButton`, `ToggleSwitch`, `NeonSlider`, `RoundedButton`)
- `GameTweaksPanel.cs` — `StatusPill` теперь использует `TextRenderer.DrawText` (ClearType) вместо `Graphics.DrawString` (пиксельный GDI+), `ClearTypeGridFit` на фоне
- Результат: на масштабах 125%/150%/175% текст, галочки `✓`/`✕`, смайлики и скругления — чёткие, без размытия

### 🎨 Скроллбар — единый, как в SmartCleanup

- `DarkScroll.cs` — переработан, устранён рассинхрон ползунка с содержимым:
  - **Настоящая причина:** ползунок перерисовывался асинхронно (`Invalidate` по событию `Scroll`), а панель — своим `WM_PAINT`. Два контрола попадали в разные кадры, поэтому ползунок то отставал, то убегал вперёд
  - **Решение:** хук на окне панели (`NativeWindow`) ловит `WM_PAINT` **после** отрисовки содержимого и тут же синхронно перерисовывает ползунок (`Invalidate` + `Update`). Порядок жёсткий: контент → ползунок, всегда один кадр
  - Позиция читается из `AutoScrollPosition` (у AutoScroll-панелей `VerticalScroll.Value` обновляется рывками) + float-точный пересчёт вместо целочисленного деления
  - Перерисовка только при реальном изменении позиции ползунка — нет лишней работы на каждом кадре
  - Аккумулятор `WHEEL_DELTA` для колеса над самой полосой: «медленное» колесо (дельта < 120) больше не теряется
  - Подходит под все темы: трек `BorderColor@55`, ползунок `Lerp(BgColor, TextDim)` с усилением при наведении, `Accent` при перетаскивании
  - Фон заливается цветом панели (`OnPaintBackground`) — нет мусора на скруглениях при перерисовке
  - Клик по треку скроллит и сразу тащит; drag не срывается, если кнопку отпустили вне полосы (`OnMouseCaptureChanged`)
  - Реестр живых полос (`live`) вместо поиска по `Controls` — дубликаты не плодятся, даже если `Attach` вызван до появления `Parent`; корректная отписка в `HandleDestroyed`
- `SmoothScroll.cs` — плавная прокрутка колесом с инерцией: дельты копятся в цель, панель доезжает с затуханием (τ = 75 мс, 60 тиков/с). Колесо ловится фильтром сообщений, поэтому плавность работает и когда курсор над карточкой/тумблером внутри панели. Контролы со своим колесом (`ListBox`, `TextBox`, `ComboBox`, `TreeView`, `WebBrowser` и т.п.) не перехватываются
- Все панели используют `DarkScroll.Attach(...)` вместо сырого системного скролла:
  - `SmartCleanupPanel`, `NetworkPanel` + `GameBoosterPanel`, `MainForm` (Settings), `BsodForm`, `DebloatForm`, `FirewallBlockForm`, `GameTime`, `ServicesPanel`, `ScreenshotsPanel`, `StartupPanel`, `SystemInfoPanel`, `RecycleBinForm`, `MacroForm`, `TelemetryForm`, `Win11TweaksForm`
  - `ListBox`-формы (`UninstallerForm`, `ClipboardManager`) — только `ApplyDarkScrollbar`: `ListBox` не `ScrollableControl`, `DarkScroll` к ним не применим

### 🧹 Полное удаление Яндекс.Браузера (новое)

`YandexCleaner.cs` + `YandexCleanerForm.cs`. Кнопка **🧹 Удалить Яндекс** в SmartCleanup → карточка «Системные остатки и Windows 11».

Штатный деинсталлятор Яндекс.Браузера оставляет профиль и кэш в `AppData`, поэтому обычного «Удалить программу» недостаточно. Здесь собирается и сносится всё:

| Что | Где ищется |
|---|---|
| Процессы | `browser.exe` (основной), `browser_proxy.exe`, `yandexbrowser.exe`, `ybrowser.exe`, `yandexbrowserupdate.exe`, `yupdate.exe`, `service_update.exe` |
| Папки | `%LOCALAPPDATA%\Yandex\YandexBrowser`, `%LOCALAPPDATA%\YandexBrowser`, `%LOCALAPPDATA%\Programs\Yandex\YandexBrowser`, `%APPDATA%\Yandex\YandexBrowser`, `%APPDATA%\YandexBrowser`, `%PROGRAMFILES%\Yandex\YandexBrowser`, `%PROGRAMFILES(X86)%\Yandex\YandexBrowser`, `%PROGRAMDATA%\Yandex\YandexBrowser` |
| Реестр | `HKCU/HKLM\Software\Yandex\YandexBrowser`, `HKLM\Software\Wow6432Node\Yandex\YandexBrowser`, `HKCU\Software\Classes\yandexbrowser`, записи в `Uninstall` (по `DisplayName`) |
| Автозагрузка | значения `Run` в HKCU/HKLM/Wow6432Node, где имя или данные содержат `yandex` |
| Задачи | `%WINDIR%\System32\Tasks` — по имени, удаление через `schtasks /Delete /F` |
| Службы | `HKLM\SYSTEM\CurrentControlSet\Services` — по имени, `sc stop` + `sc delete` |
| Ярлыки | Рабочий стол, общий Рабочий стол, меню «Пуск» (`.lnk` по имени) |

Порядок удаления: процессы → задачи → службы → реестр → папки → ярлыки (процессы первыми, иначе файлы заняты и папка не удалится).

- Флажок **«Заодно остальные продукты Яндекса»** — добавляет `Software\Yandex` целиком и папки `Yandex` (Диск, Мессенджер и т.п.). По умолчанию выключен, чтобы не снести лишнее
- Вложенные папки не дублируются: родитель поглощает детей (сортировка по длине пути), иначе размер считался бы дважды
- `browser.exe` — слишком общее имя, поэтому процесс добивается только если путь реально в папке Яндекса; PID сверяется с именем процесса перед `Kill` (PID мог достаться другому процессу)
- Удаление папок: сброс атрибутов «только чтение», 3 попытки, затем fallback `rd /s /q`
- После удаления список пересканируется: если что-то осталось — честно показывается «Не всё удалилось: осталось N», иначе «Яндекс удалён полностью ✓»

Проверено на искусственных следах (папки + ключ реестра + запись автозагрузки): сканер находит всё, дедуп работает, `ForceDelete` сносит дерево с read-only файлами и идемпотентен при повторном вызове.

### 📋 Буфер обмена — переделан

Три проблемы разом:

- **Кириллица превращалась в `?`/ромбики.** Текст читался через `DataFormats.Text`, а это `CF_TEXT` — ANSI. При системной кодовой странице UTF-8 (или любой не-русской) кириллица не перекодируется и заменяется на `�`. Проверено на живой машине: `CF_TEXT` → `������, ���!`, `CF_UNICODE` → `Привет, мир!`. Теперь сначала читается `DataFormats.UnicodeText`, ANSI — только резерв; восстановление тоже через `TextDataFormat.UnicodeText`
- **История не обновлялась сразу.** Буфер опрашивался таймером раз в 1000 мс — отсюда задержка «скопировал, а появилось потом». Добавлен слушатель `AddClipboardFormatListener` + `WM_CLIPBOARDUPDATE`: запись появляется мгновенно. Таймер оставлен как страховка, если слушатель не установится
- **Не было нормального скроллера.** `ListBox` — не `ScrollableControl`, поэтому общий `DarkScroll` к нему не цеплялся. Заменён на `ClipboardHistoryView`: собственный список с отрисовкой строк, настоящим `AutoScroll` и тем же `DarkScroll`, что и во всех разделах. Скруглённая подсветка строки, маркер типа (текст/картинка), метка времени, «закреплено», hover, двойной клик — в буфер

Дополнительно: выбор элемента сохраняется по `Id` при обновлении истории (перерисовка не сбрасывает выделение), поиск через `OrdinalIgnoreCase` (корректно для кириллицы), восстановленный элемент не попадает в историю повторно.

### 📦 Версия

- `AssemblyInfo.cs`: `2.1.0.0` → `2.1.1.0`
- `app.manifest`: `2.1.0.0` → `2.1.1.0`
- `README.md`: бейдж + копирайт → `2.1.1`

### Сборка

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
# Требует .NET Framework 4.x, все DLL встраиваются в один DeepTools.exe
```

> Примечание: если `DeepTools.exe` запущен, скрипт переименует его в `DeepTools_old.exe` и подставит свежую сборку. В конце всегда печатается `BUILD OK` или `BUILD FAILED` с конкретной причиной.
