# Goji VPN — Windows

Десктоп-клиент VPN-сервиса на WPF (.NET 8), с реальным туннелем на связке
[Xray-core](https://github.com/XTLS/Xray-core) (протокол VLESS + XHTTP + Reality) и
[sing-box](https://github.com/SagerNet/sing-box) (системный TUN-адаптер через Wintun).
Работает с тем же бэкендом, что и наша [Android-версия](https://github.com/goji-app/goji-vpn-android).

## Возможности

**Подключение**
- VPN-туннель (VLESS + Reality) — двухпроцессная архитектура: `xray.exe` занят только
  протоколом и отдаёт локальный SOCKS5, `sing-box.exe` поднимает системный TUN-адаптер и
  форвардит на него весь трафик машины
- 3D-глобус (three.js, встроен через WebView2) с подсветкой страны подключения — тот же
  веб-компонент, с которого изначально был сделан Android-порт на OpenGL
- Split-tunneling: домены зоны `.ru` и правила с бэкенда (российские IP/торрент-трекеры)
  идут в обход туннеля напрямую

**Вход и подписка**
- Email + одноразовый код, а также нативный OAuth (Google, Яндекс) через PKCE — открывает
  системный браузер, возвращается в приложение по кастомной URI-схеме `godjivpn://`
- Список серверов подписки с проверкой пинга через реальный прокси-туннель (не просто
  TCP-рукопожатие — честная задержка с учётом оверхеда VLESS+Reality)
- Экран тарифов с реальными ценами/сроком/лимитом трафика, локальные уведомления о скором
  окончании подписки и об успешной оплате

**Настройки**
- Тёмная/светлая тема
- Настраиваемый способ проверки пинга серверов (через прокси GET/HEAD, TCP или ICMP)
- Журнал: отдельно приложение, ядро (xray), туннель (sing-box) и сбои — хранится только на
  устройстве
- «О приложении»: версия, HWID (с копированием)
- Трей-иконка — закрытие окна сворачивает в трей, туннель остаётся поднят

## Стек

.NET 8 · WPF · CommunityToolkit.Mvvm · Microsoft.Web.WebView2 · Hardcodet.NotifyIcon.Wpf ·
Xray-core · sing-box

## Сборка

Открывается в Visual Studio 2022+ или `dotnet build` (нужен .NET 8 SDK). Перед первым
запуском положите в `GodjiVpn/Runtime/` бинарники ядра — см.
[GodjiVpn/Runtime/README.txt](GodjiVpn/Runtime/README.txt).

```powershell
dotnet build GodjiVpn/GodjiVpn.csproj -c Release
```

Установщик (self-contained, .NET-рантайм не нужен на целевой машине) собирается через
[Inno Setup 6](https://jrsoftware.org/isinfo.php) одной командой:

```powershell
winget install --id JRSoftware.InnoSetup -e   # если ещё не установлен
./build-installer.ps1
```

Результат — `dist/GodjiVpn-Setup-<версия>.exe`. Готовые установщики за все версии — во
вкладке [Releases](../../releases) этого репозитория.

## Структура

```
GodjiVpn/
├─ Assets/          # Шрифты, флаги, глобус (three.js), иконки
├─ Controls/        # GlobeHost (WebView2-обёртка), TrafficBar, DaysRing
├─ Converters/       # XAML value-конвертеры
├─ Models/          # DTO бэкенд-API
├─ Runtime/         # Бинарники ядра (xray.exe/sing-box.exe/…) — см. README.txt внутри
├─ Services/        # VpnEngine, ApiClient, TokenStore, TrayIconService и т.д.
├─ Themes/          # Светлая/тёмная палитра (1:1 с Android GodjiColors)
├─ Utils/           # Форматирование дат, разбор remark-строк узлов
├─ ViewModels/
└─ Views/           # Login, Connect, Servers, Plans, Settings

installer/           # Скрипт Inno Setup
build-installer.ps1   # publish + упаковка установщика одной командой
```
