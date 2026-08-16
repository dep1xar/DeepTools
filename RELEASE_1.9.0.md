# DeepTools v1.9.0

**Русский**

Релиз про «под капотом»: меньше микрофризов, честная автозагрузка, датчики вентиляторов и здоровье батареи — плюс сетевые инструменты на каждый день.

Новое:
- 🚀 **Автозагрузка видит Планировщик задач** — logon/boot-задачи теперь в общем списке, с тем же рейтингом подозрительности (нет подписи + запуск из Temp + случайное имя) и переключателем вкл/выкл. Именно туда чаще всего прописываются апдейтеры и мусор мимо обычной автозагрузки (системные задачи `\Microsoft\` не трогаем).
- 🧠 **Авто-очистка standby-памяти** (в духе ISLC) — сбрасывает резервный кэш памяти, когда свободной RAM мало, убирая микрофризы в тяжёлых играх. Есть и кнопка «Очистить сейчас».
- 🎮 **Авто-очистка кэша шейдеров при смене драйвера GPU** — после обновления драйвера старый кэш (DirectX/NVIDIA/AMD) вызывает фризы; DeepTools ловит смену версии и чистит его на старте.
- 🌀 **Обороты вентиляторов** CPU/GPU/корпуса прямо в Health Check.
- 🔋 **Здоровье батареи и время загрузки** в «Мой ПК» — износ батареи, циклы, заводская vs текущая ёмкость, аптайм и тренд длительности загрузок Windows.
- 🌐 **Инструменты сети** — сброс сетевого стека (winsock/IP) одной кнопкой и блокировка интернета конкретным приложениям через брандмауэр Windows.
- 🔒 **Один экземпляр** — второй запуск больше не открывает второе окно, а поднимает уже запущенное.
- 🛠 **Сборка из исходников проще** — добавлен `build.bat` (двойной клик, окно не закрывается, видно результат/ошибку).

Удалено:
- 📦 Вкладка «Большие файлы» убрана.

**English**

An under-the-hood release: fewer micro-stutters, honest startup, fan sensors and battery health — plus everyday network tools.

New:
- 🚀 **Startup now sees Task Scheduler** — logon/boot tasks join the list with the same suspicion rating (no signature + runs from Temp + random-looking name) and an on/off toggle. That's where updaters and junk usually hide outside the normal startup list (system `\Microsoft\` tasks are left alone).
- 🧠 **Auto standby-memory cleanup** (ISLC-style) — purges the standby cache when free RAM runs low, removing micro-stutter in heavy games. A "Clean now" button is included too.
- 🎮 **Auto shader-cache cleanup on GPU driver change** — after a driver update a stale DirectX/NVIDIA/AMD cache causes stutter; DeepTools detects the version change and clears it on launch.
- 🌀 **Fan RPM** for CPU/GPU/case right in Health Check.
- 🔋 **Battery health and boot time** in My PC — battery wear, charge cycles, design vs current capacity, uptime and a Windows boot-duration trend.
- 🌐 **Network tools** — one-click network-stack reset (winsock/IP) and per-app internet blocking via Windows Firewall.
- 🔒 **Single instance** — launching DeepTools again brings the running window to the front instead of opening a second one.
- 🛠 **Easier builds from source** — added `build.bat` (double-click, the window stays open so you can read the result or the error).

Removed:
- 📦 The "Large Files" tab was removed.
