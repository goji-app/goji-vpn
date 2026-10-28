# Goji VPN — Windows

Десктоп-клиент Goji VPN для Windows 10/11 на WPF (.NET 8). Туннель держат два процесса:
[Xray-core](https://github.com/XTLS/Xray-core) (`xray.exe`, протокол VLESS, локальный SOCKS) и
[sing-box](https://github.com/SagerNet/sing-box) (`sing-box.exe`, системный TUN-адаптер через Wintun).
Общий обзор всех приложений — в [README репозитория](../README.md).

## Возможности

**Подключение**
- Подключение в один клик, 3D-глобус (three.js в WebView2) со страной подключения.
- Весь трафик компьютера идёт через TUN-адаптер (`strict_route`); российские сайты и сервисы — напрямую по геоданным, свои «Сайты мимо VPN» добавляются вручную.
- **Kill switch** (Настройки → Подключение): если `xray.exe` упадёт, sing-box не снимается — трафик остаётся в туннеле, пока ядро перезапускается.
- **Приложения мимо VPN**: выбранные программы (`.exe`) работают напрямую — правило sing-box по имени процесса.
- Правила Wi-Fi: доверенные сети и автоподключение в чужих (в Windows 11 24H2+ для имени сети нужен доступ к геолокации).
- Значок в трее показывает состояние: серый — отключено, жёлтая точка — подключаемся, зелёная — подключено. Закрытие окна сворачивает в трей, туннель остаётся поднят.

**Вход и подписка**
- Вход по коду на email; три экрана знакомства при первом запуске; пробный период — карточкой на главной.
- Серверы с честным пингом через туннель, избранное, сортировка; кнопки «Пинг» и «Обновить» раздельные.
- Подписка: устройства, приглашения друзей с QR-кодом, новости, кнопка «Оплатить в личном кабинете», перенос входа на телефон QR-кодом (в коде только почта).
- Напоминания за 3 дня и за 1 день до окончания подписки.

**Прочее**
- Поддержка в приложении: обращения, чат, вложения, FAQ.
- Понятные ошибки подключения с кнопкой «Сменить сервер» или «Открыть подписку».
- Отчёт о сбое одним нажатием: при следующем запуске после падения приложение находит запись в журнале Windows и предлагает отправить её в поддержку.
- Проверка утечек IP/DNS, журнал сети, журналы приложения, ядра и туннеля.
- Светлая и тёмная тема, размер шрифта, окно подстраивается под экран без прокрутки (длинные списки — постранично).
- Автообновление: `latest.json` на сайте загрузки, запасной путь — последний релиз здесь.

## Как устроено

- Приложение запускается **с правами администратора** (`requireAdministrator`), без них нельзя поднять TUN-адаптер.
- `xray.exe` слушает SOCKS на `127.0.0.1:10808`; `sing-box.exe` поднимает Wintun и отправляет в этот SOCKS весь трафик.
- Ядро лежит в `GodjiVpn/Runtime`: `xray.exe` (26.10.21), `sing-box.exe` (1.14.3), `wintun.dll`, геоданные. Сами бинарники в git не хранятся; их SHA-256 — в `Runtime/SHA256SUMS`.
- Данные приложения: `%LOCALAPPDATA%\GodjiVpn` (настройки, токены под DPAPI, журналы, кэш кабинета).
- Новости и описание обновлений выводятся `Controls/RichTextView` (обычные TextBlock): `RichTextBox` не использовать — его движок разметки на Windows 10 при некоторых эмодзи завершает процесс через `FailFast`.

## Сборка

Нужны .NET 8 SDK и [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install --id JRSoftware.InnoSetup -e`). Адреса сервисов — `goji-hosts.properties` в корне репозитория (см. README там).

```powershell
dotnet build GodjiVpn/GodjiVpn.csproj -c Release   # проверить, что собирается
./build-installer.ps1                              # dist/GodjiVpn-Setup-<версия>.exe
```

`build-installer.ps1` сначала сверяет ядро с `Runtime/SHA256SUMS` и останавливается, если файлы не те. Версия установщика берётся из `GodjiVpn.csproj`.

**Просмотр интерфейса без VPN** (не трогает сеть и аккаунт): запустить собранный `GodjiVpn.exe` с `__COMPAT_LAYER=RunAsInvoker`, `GODJI_UI_PREVIEW=1` и по желанию `GODJI_UI_PREVIEW_THEME=light|dark`, `GODJI_UI_PREVIEW_DEMO=1`, `GODJI_UI_PREVIEW_SCREEN=onboarding|login|verify`, `GODJI_UI_PREVIEW_TAB`.

## Структура

```
GodjiVpn/
├─ Assets/       шрифты, флаги, глобус (three.js), иконки, значки трея tray-*.png
├─ Controls/     GlobeHost (WebView2), PagedItems, RichTextView, GlassPanel и др.
├─ Models/       DTO API кабинета и манифеста сайта загрузки
├─ Runtime/      ядро (в git только README и SHA256SUMS)
├─ Services/     VpnEngine, ApiClient, TokenStore, CrashReporter, TrayIconService, UpdateService…
├─ Themes/       светлая/тёмная палитра (как Android GodjiColors)
├─ Utils/        разбор новостей (RichContent), даты, шрифт, память
├─ ViewModels/
└─ Views/        Onboarding, Login, Connect, Servers, Plans, Settings, Support

installer/           скрипт Inno Setup
build-installer.ps1  проверка ядра + publish + установщик одной командой
```
