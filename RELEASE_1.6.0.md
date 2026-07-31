# DeepTools v1.6.0

**Русский**

Новое:
- 🎮 **FPS в отчёте после игры** — закрыл игру и сразу видишь: «средний FPS 142, 1% low 87» плюс нагрев. Средний FPS каждой игры теперь виден и в окне «Время в играх», и пишется в журнал сессий.
- 📤 **PNG-карточка «Поделиться»** — кнопка в окне «Время в играх» рендерит красивую карточку со спекой ПК (CPU/GPU/RAM), общим временем и топ-5 игр с FPS. Сразу копируется в буфер и сохраняется в Изображения\DeepTools — для форумов и «смотри какой у меня ПК».
- ⏱️ **Автоочистка по расписанию** — тумблер в SmartCleanup: раз в N дней (1–30) тихо чистит отмеченные категории и присылает отчёт из трея «Освобождено X ГБ». Не запускается, пока идёт игра — кэш шейдеров не выдёргивается из-под неё.
- 🔇 **Отключение телеметрии Windows** — новая панель в разделе «Службы»: 12 твиков (политики реестра, службы DiagTrack/dmwappushservice, задачи CEIP и Compatibility Appraiser) с пресетами «мягко / средне / жёстко». Каждый твик показывает живой статус, всё откатывается одной кнопкой «Вернуть как было».
- ♻️ **Точка восстановления** — перед рискованными действиями (деблоат, пресет служб, телеметрия) программа предлагает создать точку восстановления системы. Если что-то пойдёт не так — всегда можно откатиться.
- 🔧 Кнопка «Деинсталлятор программ» больше не вылезает за край панели SmartCleanup.
- 🌐 В пинг-тесте раздела «Сеть» сервер Яндекса заменён на Xbox DNS (xbox-dns.ru).

**English**

New:
- 🎮 **FPS in the post-game report** — close a game and instantly see "avg FPS 142, 1% low 87" plus thermals. Each game's average FPS is now also shown in the Game Time window and written to the session log.
- 📤 **PNG share card** — a button in the Game Time window renders a neat card with your PC spec (CPU/GPU/RAM), total playtime and top-5 games with FPS. Copied to the clipboard and saved to Pictures\DeepTools — for forums and "look at my PC" moments.
- ⏱️ **Scheduled auto cleanup** — a toggle in SmartCleanup: every N days (1–30) it silently cleans the checked categories and sends a tray report "Freed X GB". It won't run while a game is active, so shader caches are never yanked from under a running game.
- 🔇 **Windows telemetry switch-off** — a new panel in Services: 12 tweaks (registry policies, DiagTrack/dmwappushservice services, CEIP and Compatibility Appraiser scheduler tasks) with light / medium / aggressive presets. Every tweak shows its live status, and everything reverts with a single "Revert all" button.
- ♻️ **Restore point** — before risky actions (debloat, service preset, telemetry) the app offers to create a system restore point. If anything goes wrong, you can always roll back.
- 🔧 The "Program uninstaller" button no longer overflows the SmartCleanup panel edge.
- 🌐 In the Network ping test, the Yandex server was replaced with Xbox DNS (xbox-dns.ru).
