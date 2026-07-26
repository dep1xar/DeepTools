# DeepTools v1.5.0

**Русский**

Новое:
- 🌐 **Раздел «Сеть»** — живые графики скорости загрузки/отдачи, счётчик трафика за сессию, пинг-тест (Cloudflare, Google, Яндекс + свой адрес: средний/мин/макс/джиттер) и таблица «кто использует сеть» — процессы с активными соединениями и их скоростью.
- 🎯 **Точный FPS** — оверлей теперь считает настоящий FPS игры через ETW-трейсинг Present-событий DirectX (как PresentMon), а не частоту композитора: цифра больше не упирается в герцовку монитора и работает в fullscreen. Если игра не рисует — прежний метод остаётся запасным.
- 📊 **FPS-оверлей+: 1% low** — FPS в худшем проценте кадров: виден статтер, который средний FPS прячет. Включается в настройках оверлея.
- 📈 **FPS-оверлей+: график фреймтайма** — мини-график времени кадра в стиле Afterburner прямо в оверлее, спайки-фризы видны сразу. С линией-ориентиром 60 FPS.
- 🌡️ **Настраиваемая тревога перегрева** — пороги CPU и GPU теперь задаются степперами в Health Check (60–105°C, шаг 5°), тревогу можно выключить целиком. Добавлен отдельный алерт по GPU.
- 🖼️ **Детект игр в borderless** — GameBooster, время в играх и отчёт после сессии теперь работают и с играми в режиме «окно без рамки», а не только в полном экране. Развёрнутый браузер за игру не принимается: проверяется, что процесс реально рисует кадры.
- ✨ **Полировка интерфейса** — сайдбар сгруппирован по разделам (Оптимизация / Мониторинг / Инструменты), акцентная полоска плавно переезжает между пунктами, подсветка пунктов при наведении, тень у окна и отклик кнопок при нажатии.

**English**

New:
- 🌐 **Network section** — live download/upload speed graphs, session traffic counter, ping test (Cloudflare, Google, Yandex + custom host: avg/min/max/jitter), and a "who is using the network" table — processes with active connections and their rates.
- 🎯 **Accurate FPS** — the overlay now measures the game's real FPS via ETW tracing of DirectX Present events (PresentMon-style) instead of the compositor rate: the number is no longer capped by your monitor's refresh rate and works in fullscreen. The old method remains as a fallback when nothing is rendering.
- 📊 **FPS overlay+: 1% low** — FPS in the worst 1% of frames: reveals stutter that average FPS hides. Toggle in overlay settings.
- 📈 **FPS overlay+: frametime graph** — Afterburner-style mini frametime graph right in the overlay; freeze spikes are instantly visible. With a 60 FPS reference line.
- 🌡️ **Configurable overheat alert** — CPU and GPU thresholds are now set with steppers in Health Check (60–105°C, step 5°), and the alert can be turned off entirely. Added a separate GPU alert.
- 🖼️ **Borderless game detection** — GameBooster, game time tracking and the post-session report now also work with borderless-windowed games, not just exclusive fullscreen. A maximized browser won't be mistaken for a game: the process must actually be rendering frames.
- ✨ **UI polish** — the sidebar is grouped into sections (Optimization / Monitoring / Tools), the accent indicator glides between items, nav items highlight on hover, the window got a drop shadow and buttons a pressed-state response.
