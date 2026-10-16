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

- `DarkScroll.cs` — переработан, исправлено «залипание» ползунка при медленной прокрутке:
  - **Причина бага:** позиция ползунка читалась из `VerticalScroll.Value`, а у AutoScroll-панелей это значение обновляется рывками — страница едет, а ползунок стоит. Теперь позиция берётся из `AutoScrollPosition` (всегда отражает реальное смещение) + float-точный пересчёт вместо целочисленного деления
  - **Убрана двойная прокрутка:** колесо над панелью больше не перехватывается — панель скроллится сама, полоса лишь успевает перерисоваться по событию `Scroll`
  - Аккумулятор `WHEEL_DELTA` для колеса над самой полосой: «медленное» колесо (дельта < 120) больше не теряется
  - Подходит под все темы: трек `BorderColor@55`, ползунок `Lerp(BgColor, TextDim)` с усилением при наведении, `Accent` при перетаскивании
  - Фон заливается цветом панели (`OnPaintBackground`) — нет мусора на скруглениях при перерисовке
  - `HighQuality` + `AntiAlias`, клик по треку скроллит и сразу тащит, hover-подсветка
  - Реестр живых полос (`live`) вместо поиска по `Controls` — дубликаты не плодятся, даже если `Attach` вызван до появления `Parent`; корректная отписка в `HandleDestroyed`
- Все панели теперь используют `DarkScroll.Attach(...)` вместо сырого системного скролла:
  - `SmartCleanupPanel`, `NetworkPanel` (уже было) + `GameBoosterPanel`, `MainForm` (Settings), `BsodForm`, `DebloatForm`, `FirewallBlockForm`, `GameTime`, `ServicesPanel`, `ScreenshotsPanel`, `StartupPanel`, `SystemInfoPanel`, `RecycleBinForm`, `MacroForm`, `TelemetryForm`, `Win11TweaksForm`
  - `ListBox`-формы (`UninstallerForm`, `ClipboardManager`) — только `ApplyDarkScrollbar`: `ListBox` не `ScrollableControl`, `DarkScroll` к ним не применим

### 📦 Версия

- `AssemblyInfo.cs`: `2.1.0.0` → `2.1.1.0`
- `app.manifest`: `2.1.0.0` → `2.1.1.0`
- `README.md`: бейдж + копирайт → `2.1.1`

### Сборка

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
# Требует .NET Framework 4.x, все DLL встраиваются в один DeepTools.exe
```

> Примечание: если `DeepTools.exe` запущен (требует админа), сборка создаёт `DeepTools_build.exe` — закрой программу (трей → Выход) и пересобери, или переименуй вручную.
