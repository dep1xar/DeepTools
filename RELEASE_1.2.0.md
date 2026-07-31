# DeepTools v1.2.0

**Русский**

Новое:
- 🟢 **Автозапуск с Windows** — тумблер в Настройках (по умолчанию выключен).
- ⚡ **Быстрые действия питания** — перезагрузка в BIOS/UEFI, тумблер быстрого запуска Windows, перезапуск проводника.
- 🎯 **Прицел и оверлей теперь сохраняются** — форма/цвет/размер прицела и включённое состояние переживают перезапуск.
- 📌 **Закрепление в буфере обмена** — кнопка «Закрепить» прямо в списке.
- 🖼️ **История буфера с картинками** — скриншоты и картинки попадают в историю с превью и восстановлением.
- 🧩 **Деинсталлятор программ** — список установленного + удаление (в разделе Умная очистка).
- 🧹 **Чистилка хвостов** — после удаления ищет остаточные папки и удаляет по выбору.
- 📝 **Заметки-стикеры** — жёлтые заметки поверх стола, живут в трее (меню трея → «Новая заметка»), восстанавливаются при запуске.

Исправлено:
- Температура CPU: добавлен запасной ACPI-датчик, когда основной движок не поднимается.
- F6 (скриншот области) теперь реально работает как горячая клавиша.
- Убрано падение меню трея (ошибка «доступ к ликвидированному объекту»).
- Буфер обмена теперь сохраняет историю между запусками.

---

**English**

New:
- 🟢 **Start with Windows** — toggle in Settings (off by default).
- ⚡ **Power quick actions** — restart to BIOS/UEFI, Fast Startup toggle, restart Explorer.
- 🎯 **Crosshair & overlay now persist** — shape/color/size and enabled state survive restarts.
- 📌 **Pin clipboard entries** — a Pin button right in the list.
- 🖼️ **Clipboard image history** — screenshots and images are kept with preview and restore.
- 🧩 **Program uninstaller** — list installed apps + uninstall (in Smart Cleanup).
- 🧹 **Leftover cleaner** — after uninstall, finds and removes leftover folders you choose.
- 📝 **Sticky notes** — desktop notes living in the tray (tray menu → New note), restored on launch.

Fixed:
- CPU temperature: added an ACPI sensor fallback when the main engine fails to load.
- F6 (region screenshot) now actually works as a hotkey.
- Fixed a tray-menu crash ("access to a disposed object").
- Clipboard now persists history across restarts.
