# DeepTools v1.7.0

**Русский**

Релиз про реальную оптимизацию: меньше «фишек», больше честного FPS.

Новое:
- 🎮 **Дискретная GPU для игр** — тумблер в GameBooster: обнаруженная игра автоматически закрепляется за мощной видеокартой (профиль «Высокая производительность» в настройках графики Windows). Для ноутбуков с двумя GPU, где Windows иногда запускает игру на встроенной графике, — это разница в разы, а не в проценты. Вступает в силу после перезапуска игры.
- ⚡ **Отключение Game DVR** — фоновая запись Xbox Game Bar захватывает кадры, даже когда ты ей не пользуешься, и это реальный минус к FPS. Один тумблер в GameBooster выключает её (и так же легко возвращает).
- ❄ **Заморозка тяжёлого фона** — Discord, браузер и лаунчеры теперь можно не закрывать, а заморозить на время игры: процесс полностью перестаёт есть CPU, но ничего не теряет — после разморозки продолжает с того же места. Разморозка той же кнопкой; при выходе из DeepTools всё размораживается автоматически.
- ⏱ **Автоочистка RAM по расписанию** — на главной рядом с кнопкой «Освободить RAM»: тумблер и интервал 5/10/30/60 минут. Работает даже когда окно свёрнуто в трей и не запускается во время игры, чтобы не дёргать память из-под неё.

Дополнение к релизу:
- 📈 **История FPS по играм** — в окне «Время в играх» у каждой игры с замеренным FPS появилась кнопка 📈: график среднего FPS и 1% low по всем сессиям. Сразу видно, деградирует ли система: если полгода назад было 200 FPS, а теперь 140 — линия честно ползёт вниз. Плюс автоматический вердикт тренда.
- 🌡 **«Пора чистить кулер»** — DeepTools и так пишет температуры 7 дней; теперь он сравнивает средние за начало и конец недели и, если CPU стабильно греется сильнее (+5°C и больше), предупреждает из трея: похоже на пыль или подсохшую термопасту. Не чаще раза в 3 дня, без спама.
- ⏰ **Напоминания в заметках** — на жёлтом стикере появилась кнопка ⏰: задаёшь время — в нужный момент придёт уведомление из трея, а заметка выпрыгнет поверх окон. Прошедшее время автоматически переносится на завтра.
- 🔊 **Переключение звука из трея** — наушники ↔ колонки одним кликом в меню трея, без блужданий по настройкам Windows. Пункт показывается, только если устройств вывода больше одного.
- 🛡 **Рейтинг подозрительности в Автозагрузке** — каждая запись проверяется: цифровая подпись, запуск из Temp, случайное имя вида «xk9f2mqa». Один флаг — жёлтая метка «стоит проверить», несколько — красная «подозрительно». Наведи курсор, чтобы увидеть причины.
- 🥚 **Пасхалка** — где-то в программе спряталась. Найдёшь?

**English**

This release is about real optimization: fewer gimmicks, more honest FPS.

New:
- 🎮 **Discrete GPU for games** — a toggle in GameBooster: the detected game is automatically pinned to the powerful GPU (the "High performance" profile in Windows graphics settings). On dual-GPU laptops where Windows sometimes launches a game on the integrated GPU, this is a difference measured in multiples, not percent. Takes effect after the game restarts.
- ⚡ **Game DVR switch-off** — Xbox Game Bar background recording captures frames even when you never use it, and that's a real FPS cost. One toggle in GameBooster turns it off (and back on just as easily).
- ❄ **Freeze heavy background** — instead of killing Discord, the browser and launchers, you can now freeze them while you play: a frozen process stops using CPU entirely but loses nothing — after unfreezing it continues right where it was. The same button unfreezes; everything is resumed automatically when DeepTools exits.
- ⏱ **Scheduled RAM auto-clean** — on the Home page next to "Free RAM": a toggle and a 5/10/30/60 minute interval. Works even when the window is minimized to the tray, and never runs while a game is active so memory is not yanked from under it.

Release addendum:
- 📈 **Per-game FPS history** — every game with measured FPS in the Game Time window now has a 📈 button: a chart of session average FPS and 1% low over time. You instantly see whether your system degrades: 200 FPS six months ago and 140 now — the line honestly slopes down. Comes with an automatic trend verdict.
- 🌡 **"Time to clean the cooler"** — DeepTools already logs temperatures for 7 days; now it compares the averages at the start and end of the week, and if the CPU keeps running hotter (+5°C or more) it warns you from the tray: looks like dust or dried thermal paste. At most once every 3 days, no spam.
- ⏰ **Note reminders** — sticky notes got a ⏰ button: set a time and you'll get a tray notification at the right moment, with the note popping to the front. Past times automatically roll over to tomorrow.
- 🔊 **Audio switcher in the tray** — headphones ↔ speakers in one click from the tray menu, no digging through Windows settings. The item only shows up when there is more than one output device.
- 🛡 **Startup suspicion rating** — every startup entry is checked: digital signature, running from Temp, random-looking names like "xk9f2mqa". One flag — a yellow "worth checking" badge, several — a red "suspicious" one. Hover to see the reasons.
- 🥚 **Easter egg** — hidden somewhere in the app. Can you find it?
