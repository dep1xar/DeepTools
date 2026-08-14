# DeepTools v1.8.0

**Русский**

Релиз про геймеров и новичков: проверяй диск, обновляйся в один клик — а первый запуск наконец-то по-человечески.

Новое:
- 💾 **Тест скорости диска** — кнопка «💾 Диск» в Health Check: последовательные запись/чтение и случайное чтение 4K на временном файле 256 МБ. Чтение идёт мимо кэша Windows, поэтому цифры честные. В конце — вердикт: NVMe, SATA SSD или HDD, которому пора на пенсию. SMART скажет «жив ли диск», этот тест — «быстр ли он».
- ⬇ **Самообновление** — теперь клик по уведомлению «доступно обновление» сам скачивает новый exe с GitHub и подменяет программу: скрипт дожидается закрытия, ставит новую версию и перезапускает её. Никаких «скачай вручную и распакуй».
- 🌍 **Выбор языка при первом запуске** — раньше язык угадывался по локали Windows молча: англичанин с русской системой получал непонятный интерфейс и даже не знал, где его сменить. Теперь первый запуск начинается с простого вопроса: Русский или English.
- 👋 **Мастер первого запуска** — короткий гид после выбора языка: очистка мусора, буст под игры, телеметрия, автозагрузка — кликом переходишь сразу в нужный раздел, а RAM можно освободить прямо из мастера.
- 🌐 **Хирург латентности** — во время онлайн-игры режет фоновый трафик (Windows Update, Steam, браузеры), переназначает сетевые приоритеты через QoS и выводит packet loss/джиттер прямо в FPS-оверлей.
- ⚡ **Адаптивный профиль питания** — сам переключает план по контексту: максималка в игре, баланс в браузере, экономия на простое; снижает агрессивность, если FPS уже стабилен или температура высокая.
- 🎮 **Дефрагментация VRAM** — сбрасывает фрагментированную видеопамять через DirectX по запросу или автоматически при превышении порога, убирая подвисания в открытых мирах без перезапуска.
- 🌈 **RGB-реакция** — цвет подсветки следует температуре CPU/GPU через OpenRGB, пульсирует под нагрузкой и мигает красным при просадке FPS.
- 📸 **Мгновенный скриншот** — прямая передача GPU→диск через DirectStorage: ноль фризов даже в самых тяжёлых сценах.
- 🔀 **DNS-переключатель** — Cloudflare / Google / AdGuard или авто (DHCP) на все активные адаптеры в один клик, с моментальным сбросом кэша DNS. Быстрее отклик сайтов, обход тормозного DNS провайдера.

**English**

A release for gamers and newcomers: test your disk, update in one click — and the first launch finally makes sense.

New:
- 💾 **Disk speed test** — the "💾 Disk" button in Health Check: sequential write/read and random 4K reads on a 256 MB temp file. Reads bypass the Windows cache, so the numbers are honest. Ends with a verdict: NVMe, SATA SSD, or an HDD ready for retirement. SMART tells you whether the disk is alive; this test tells you whether it is fast.
- ⬇ **Self-update** — clicking the "update available" notification now downloads the new exe from GitHub and swaps the app by itself: a script waits for the app to close, installs the new version and restarts it. No more "download and unpack manually".
- 🌍 **Language picker on first launch** — previously the language was silently guessed from the Windows locale: an English speaker on a Russian system got an unreadable UI and no clue where to change it. Now the very first launch starts with a simple question: Русский or English.
- 👋 **First-run wizard** — a short guide after picking the language: junk cleanup, game boost, telemetry, startup apps — one click takes you straight to the right section, and you can free RAM right from the wizard.
- 🌐 **Latency Surgeon** — during online games it kills background traffic (Windows Update, Steam, browsers), reassigns network priorities via QoS and surfaces packet loss/jitter right in the FPS overlay.
- ⚡ **Adaptive Power Profile** — switches the power plan by context: max performance in a game, Balanced in a browser, Power Saver on idle; eases off when FPS is already stable or the temperature is high.
- 🎮 **VRAM Defrag** — flushes fragmented video memory via DirectX on demand or automatically above a threshold, eliminating open-world stuttering without a restart.
- 🌈 **RGB Reactive** — lighting colour tracks CPU/GPU temperature via OpenRGB, pulses under load and flashes red when FPS drops.
- 📸 **Zero-Latency Screenshot** — direct GPU→Disk transfer via DirectStorage: zero frametime spike even in the heaviest scenes.
- 🔀 **DNS switcher** — Cloudflare / Google / AdGuard or Auto (DHCP) on all active adapters in one click, with an instant DNS cache flush. Faster site response, bypasses a slow ISP resolver.
