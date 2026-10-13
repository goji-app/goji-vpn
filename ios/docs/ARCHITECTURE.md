# Архитектура iOS-клиента Goji VPN

## Обзор

```
┌──────────────────────────── iPhone ────────────────────────────┐
│                                                                │
│  GojiVPN.app (SwiftUI)                PacketTunnel.appex       │
│  ┌──────────────────────┐             ┌──────────────────────┐ │
│  │ UI: вход, главная,   │  options /  │ NEPacketTunnelProvider│ │
│  │ серверы, аккаунт,    │  messages   │  ├ utun fd ──┐        │ │
│  │ настройки, поддержка │────────────▶│  │           ▼        │ │
│  │                      │◀────────────│  │  libXray (Go)      │ │
│  │ APIClient ─────────┐ │  (stats,    │  │  Xray-core:        │ │
│  │ SubscriptionStore  │ │   status,   │  │  TUN-инбаунд ──▶   │ │
│  │ VPNController      │ │   switch)   │  │  routing ──▶       │ │
│  │ PingService(libXray)│ │             │  │  VLESS/Reality/…   │ │
│  └────────────────────┼─┘             └──┼────────────────────┘ │
│         │             │   App Group      │         │            │
│         │             └──────▶ group.xyz.gojihub.vpn ◀┘         │
│         │                 (профиль узла, настройки,             │
│         │                  геоданные, журналы)                  │
└─────────┼──────────────────────────────────────────┼────────────┘
          ▼                                          ▼
  gojihub.xyz (шоп-API:                     servN.gojihub.xyz / cdnya…
  вход, подписка, поддержка)                (VPN-узлы Remnawave)
  subs.gojihub.xyz/<токен>
  (список серверов, X-HWID)
```

Два процесса:

1. **Приложение** `GojiVPN` (`xyz.gojihub.vpn`) — интерфейс, работа с API, выбор узла,
   управление системным VPN-профилем.
2. **Расширение** `PacketTunnel` (`xyz.gojihub.vpn.tunnel`) — живёт отдельно от приложения,
   его запускает система. Внутри — ядро Xray-core (через libXray), которое читает IP-пакеты прямо
   из utun-дескриптора и отправляет их через прокси-аутбаунды профиля подписки.

Общие данные — только через контейнер **App Group** `group.xyz.gojihub.vpn`.

## Цели сборки (project.yml)

| Цель | Тип | Исходники | Зависимости |
|---|---|---|---|
| `GojiVPN` | application | `App/Sources`, `App/Resources`, `Shared` | `PacketTunnel` (embed), `LibXray.xcframework` (static), NetworkExtension |
| `PacketTunnel` | app-extension | `PacketTunnel`, `Shared` | `LibXray.xcframework` (static), NetworkExtension |
| `GojiVPNTests` | unit-test | `Tests` | host = GojiVPN |

`Shared/` компилируется в обе цели (не отдельный фреймворк — меньше накладных расходов и
памяти в расширении).

Минимальная iOS — **16.0**. Только iPhone.

## Ядро: libXray + встроенный TUN Xray-core

- **LibXray.xcframework** — официальная сборка XTLS/libXray (вариант `apple-cgo`, статическая
  библиотека Go c C-интерфейсом). Версия и SHA256 зафиксированы в `Scripts/fetch-libxray.sh`
  (сейчас **v26.9.30**, Xray-core v26.9.30). В git не хранится (≈100 МБ) — скачивается скриптом.
- Единственная точка входа: `CGoInvoke(requestJSON) → responseJSON` (+ `CGoFree`). Обёртка —
  `Shared/XrayCore.swift` (`runXray`, `stopXray`, `pingBatch`, `xrayVersion`,
  `getFreePorts`, `convertShareLinksToXrayJson`).
- **Отличие от Android.** На Android Xray поднимает SOCKS, а пакеты из tun в SOCKS гонит отдельный
  tun2socks (hev-socks5-tunnel), потому что переменная окружения `xray.tun.fd` не доходила до
  Go-рантайма. В Xray-core ≥ 26 окружение задаётся **в самом конфиге** (корневой объект `env`),
  и iOS официально поддерживается встроенным TUN-инбаундом: расширение находит дескриптор utun и
  кладёт его номер в `env["xray.tun.fd"]`. Итог — один Go-рантайм на процесс, без tun2socks,
  меньше памяти.
- Ядру передаётся **копия** дескриптора (`dup`): Go закрывает файл при сборке мусора
  остановленного экземпляра, а сам utun принадлежит NetworkExtension и нужен для следующего
  запуска ядра (смена узла без разрыва туннеля).
- Память: лимит расширения Packet Tunnel ≈ 50 МБ. libXray на iOS сам включает агрессивный GC
  (`SetGCPercent(10)`, лимит 30 МБ, `FreeOSMemory` раз в секунду). Геоданные не грузятся в
  Xray целиком — см. ниже.

## Сборка конфигурации ядра (`Shared/XrayConfigBuilder.swift`)

Профиль узла приходит с `subs.gojihub.xyz` готовым Xray-конфигом (`dns`, `routing`,
`outbounds`). Перед запуском расширение достраивает его — логика перенесена из
`GodjiVpnService.establishTunnel` Android-клиента:

1. **Инбаунды:** `tun-in` (protocol `tun`, MTU 1500, sniffing http/tls/quic) и `socks-in` на
   127.0.0.1 (для сторожа связи). Sniffing обязателен: в TUN приходят IP, а правила профиля —
   доменные.
2. **env:** `xray.tun.fd`, `xray.location.asset`.
3. **Геоданные:** ссылки `geosite:X` / `geoip:X` во всём конфиге (правила, DNS) заменяются
   готовыми доменами/подсетями, разобранными из `geosite.dat`/`geoip.dat` собственным
   protobuf-парсером (`GeoDataParser`, mmap, материализуются только запрошенные категории).
   Ненайденные теги и `ext:` удаляются; правила без матчеров — выбрасываются.
4. **Российские сервисы → direct:** всегда добавляются `category-ru` + известные пробелы
   (ozone.ru, tbank.ru, …) + `mobile_whitelist` домены и подсети — перед последним (catch-all)
   правилом, если в профиле есть аутбаунд `direct`.
5. **Сайты мимо VPN:** пользовательский список — первым правилом на freedom-аутбаунд.
6. **Надёжность:** `domainMatcher: hybrid`, `domainStrategy: IPIfNonMatch`, handshake 8 с,
   журнал ошибок Xray в App Group.
7. **Keepalive:** `tcpKeepAliveIdle/Interval = 30` для прокси-аутбаундов, `hKeepAlivePeriod = 30`
   для XHTTP (лечение «рвётся через 5–10 минут» — как на Android/Windows).
8. **Статистика:** `stats` + `policy.system.statsInbound*` + `metrics.listen` на свободном
   локальном порту. Расширение читает `/debug/vars` и отдаёт приложению счётчики `tun-in`.

## Расширение туннеля (`PacketTunnel/PacketTunnelProvider.swift`)

- `startTunnel`: профиль берётся из `options` (запуск из приложения) или из
  `active-profile.json` в App Group (запуск системой — VPN On Demand, после перезагрузки).
  Настройки сети: IPv4 172.19.0.1/30, маршрут по умолчанию, DNS 1.1.1.1 / 8.8.8.8 для всех
  доменов, MTU 1500. Затем поиск utun-дескриптора (`getsockopt(fd, SYSPROTO_CONTROL,
  UTUN_OPT_IFNAME)`), сборка конфига, `runXray`.
- **Сторож связи:** раз в 30 с запрос `cp.cloudflare.com/generate_204` через локальный SOCKS ядра;
  3 провала подряд — перезапуск ядра на том же utun.
- **Сообщения от приложения** (`handleAppMessage`, JSON):
  - `stats` → `{up, down}` (байты, счётчики tun-in);
  - `status` → `{label, connectedSince, lastError}`;
  - `switchProfile` → перезапуск ядра с новым профилем без разрыва VPN;
  - `reloadSettings` → перезапуск ядра (изменились «Сайты мимо VPN», уровень журнала);
  - `directIP` → настоящий IP (собственные соединения расширения идут мимо туннеля).
- Ошибка запуска сохраняется в `TunnelSettings.lastError` — приложение показывает её
  пользователю (система сообщает только «не удалось подключиться»).

## Приложение

### Слои

```
App/Sources
├── App/            точка входа, AppModel (маршрут, deeplink, тосты), RootView, демо-режим
├── Core/
│   ├── Networking/ APIClient (Bearer, X-Requested-With, refresh по куке), модели API
│   ├── Auth/       AuthService (email-код, QR-перенос, выход, удаление), Keychain, DeviceID
│   ├── Subscription/ SubscriptionStore (подписка, узлы, кэш), PingService
│   ├── VPN/        VPNController (NETunnelProviderManager, On Demand, kill switch, статистика)
│   ├── Network/    NetworkMonitor (глушение → LTE), LeakChecker
│   ├── Settings/   AppSettings
│   ├── Notifications/ локальные напоминания
│   ├── Background/ BGAppRefreshTask
│   ├── Geo/        CountryGeo, GeoDataUpdater
│   └── Intents/    Siri / Быстрые команды
├── DesignSystem/   токены «Стекло» v5, шрифты, стеклянные компоненты
└── Features/       Onboarding, Login, Connect (+глобус), Servers, Subscription, Settings, Support
```

Состояние — `ObservableObject`-синглтоны на `@MainActor` (`AppModel`, `AppSettings`,
`SubscriptionStore`, `VPNController`, `PingService`, `NetworkMonitor`), прокинутые в SwiftUI
через `environmentObject`. Сетевые вызовы — `async/await`.

### Бэкенд

| Хост | Что | Клиент |
|---|---|---|
| `gojihub.xyz` | вход (`/api/auth/*`), подписки, рефералы, устройства, новости, поддержка, FAQ, пробный период | `APIClient` |
| `subs.gojihub.xyz/<токен>` | список серверов (JSON-массив Xray-профилей). Заголовки: `User-Agent: v2rayNG/1.8.29` (иначе старый base64-формат), `X-HWID`, `X-Device-OS/OS-Version/Model`, `X-App-Name/Version` | `SubscriptionStore.fetchNodes` |
| `host.gojihub.xyz` | админка Remnawave | **никогда** не вызывается из приложения |

Сессия: JWT из куки `rw_session_token` (24 ч) используется как Bearer; при 401 —
`POST /api/auth/refresh` с `rw_refresh_token` (один на все параллельные запросы, `actor`
`RefreshCoordinator`), повтор запроса; если не вышло — выход на экран входа.

### Хранение данных

| Что | Где | Защита |
|---|---|---|
| JWT, refresh-токен, HWID | Keychain | `AfterFirstUnlockThisDeviceOnly` (не в бэкапах, не переезжает на другое устройство) |
| Кэш подписки и серверов (содержит учётные данные профилей) | App Group `Cache/servers.json` | Data Protection `completeUntilFirstUserAuthentication` |
| Активный профиль узла | App Group `active-profile.json` | то же |
| Настройки туннеля (сайты мимо VPN, уровень журнала, последняя ошибка) | App Group UserDefaults | — |
| Настройки интерфейса | UserDefaults приложения | — |
| Геоданные | App Group `GeoData/` (обновления), бандл расширения (встроенные) | — |
| Журналы | App Group `Logs/*.log` (по 512 КБ на категорию, обрезка до 256 КБ) | без токенов и профилей |

**HWID** — случайный UUID, созданный один раз и хранящийся в Keychain: переживает переустановку,
поэтому не занимает новые места в лимите устройств (у пробного тарифа лимит = 1).

### VPN-профиль и автоподключение

`VPNController` настраивает `NETunnelProviderManager`:

- `includeAllNetworks = killSwitch` — весь трафик устройства только через туннель;
  `excludeLocalNetworks = true` (AirPlay, принтеры остаются доступны);
- **VPN On Demand** вместо Android-правил Wi-Fi:
  - доверенные SSID → `NEOnDemandRuleDisconnect(ssidMatch:)`;
  - «В чужих Wi-Fi» → `NEOnDemandRuleConnect(interfaceTypeMatch: .wiFi)`;
  - «Всегда» → `NEOnDemandRuleConnect()` (аналог Always-on VPN Android);
  - в конце `NEOnDemandRuleIgnore`.
  SSID сопоставляет сама iOS — разрешение на геолокацию не нужно. Ручное отключение снимает
  On Demand до следующего ручного включения (иначе система сразу подняла бы туннель обратно).
- Смена узла при активном VPN — сообщение `switchProfile` расширению (без разрыва).

### «Держит в сети» (NetworkMonitor)

Порт логики `ConnectViewModel` Android: `NWPathMonitor` с дебаунсом 1 с;
нет пути → узел «LTE» (российский выход), появился Wi-Fi → предыдущий узел, мобильная сеть →
«Автовыбор EU», если европейские узлы пингуются, иначе LTE. Работает, пока приложение открыто
(iOS не даёт приложению следить за сетью в фоне).

### Пинг

`PingService`: `pingBatch` libXray в процессе приложения (ядро туннеля — в другом процессе,
конфликта «managed instance» нет), пачки по 5, для узлов-балансировщиков сначала первый
аутбаунд, затем резервные; либо TCP-connect (`NWConnection`). ICMP на iOS недоступен.

### Уведомления и фон

- Напоминания об окончании подписки планируются **заранее** локальными уведомлениями
  (за 3 дня и 1 день, для пробного — за 12 ч) при каждом обновлении подписки.
- «Подписка продлена» — если срок увеличился; новости — новые записи `/api/broadcasts/completed`.
- `BGAppRefreshTask` (`xyz.gojihub.vpn.refresh`, не чаще раза в 4 ч по решению iOS) — обновление
  подписки и геоданных.

### Дизайн

Токены «Стекло» v5 — те же, что в Android/Windows/macOS (`DesignSystem/Theme.swift`),
динамические цвета для светлой/тёмной темы. Шрифты Manrope (текст) и Instrument Serif
(заголовки) с Dynamic Type. Глобус — нативный `Canvas` (ортографическая проекция контуров из
`geo_globe.json`, дуга маршрута по большому кругу), без WebView.

## Соответствие функций Android → iOS

| Android | iOS | Примечание |
|---|---|---|
| VpnService + hev-socks5-tunnel + libXray SOCKS | NEPacketTunnelProvider + TUN Xray-core | один Go-рантайм |
| Kill switch (не закрывать tun) | `includeAllNetworks` | системный, надёжнее |
| Always-on VPN | Автоподключение «Всегда» (On Demand) | |
| Правила Wi-Fi (NetworkRulesManager) | VPN On Demand (SSID) | работает без приложения, без геолокации |
| Прокси для выбранных приложений | — | на iOS per-app VPN только через MDM |
| Исключение RU-приложений из туннеля | маршрутизация category-ru → direct | приложения из туннеля на iOS не исключить |
| Вход Google/Яндекс/Telegram (WebView) | только email + QR | правило 4.8 |
| «Продлить» / тарифы / партнёрка | нет | правило 3.1.1 |
| Самообновление из GitHub | нет | обновления только через App Store |
| Виджет, плитка, ярлык | Siri / Быстрые команды | виджет и пункт Пункта управления — в планах |
| ICMP-пинг | нет | недоступен на iOS |
| Язык в приложении | язык из Настроек iOS | рекомендация Apple |
| Размер шрифта | Dynamic Type | системная настройка |
| Уведомление со скоростью | — | у iOS нет постоянных уведомлений; скорость на главной |
