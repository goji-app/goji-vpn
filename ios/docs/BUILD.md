# Сборка и разработка

## Требования

- Mac с **Xcode 16** или новее (macOS 14.5+).
- [Homebrew](https://brew.sh) → `brew install xcodegen` (генерация проекта из `project.yml`).
- Python 3 (есть в macOS) — проверка локализации.
- Для запуска VPN — **реальный iPhone** (iOS 16+): NetworkExtension не работает в симуляторе.
  В симуляторе работает весь интерфейс и демо-режим.

На Windows собрать iOS-приложение нельзя. Для этого есть CI на macOS-раннере GitHub
(см. ниже) — им пользовались при разработке.

## Первая сборка на Mac

```bash
git clone https://github.com/goji-app/goji-vpn-ios.git
cd goji-vpn-ios
./Scripts/bootstrap.sh        # ядро LibXray (сверка SHA256) + геоданные + GojiVPN.xcodeproj
open GojiVPN.xcodeproj
```

Подпись для запуска на своём iPhone: создайте `Config/Signing.local.xcconfig` (файл в
`.gitignore`):

```
DEVELOPMENT_TEAM = ABCDE12345
CODE_SIGN_STYLE = Automatic
```

и перегенерируйте проект (`xcodegen generate`). Team ID — в
[developer.apple.com → Membership](https://developer.apple.com/account#MembershipDetailsCard).
Для запуска расширения Packet Tunnel нужен **платный** аккаунт Apple Developer Program: у
бесплатного Personal Team нет capability Network Extension.

Схема **GojiVPN** → устройство → Run. При первом «Включить» iOS спросит разрешение добавить
конфигурацию VPN.

## Скрипты

| Скрипт | Назначение |
|---|---|
| `Scripts/bootstrap.sh` | всё сразу: `fetch-libxray.sh` + `fetch-geodata.sh` + `xcodegen generate` |
| `Scripts/fetch-libxray.sh` | скачивает `LibXray.xcframework` нужной версии в `Frameworks/`, сверяет SHA256 |
| `Scripts/fetch-geodata.sh` | встроенные геоданные расширения: `geosite.dat`, `mobile_whitelist_*.txt` → `PacketTunnel/Resources/GeoData/` (`FORCE=1` — перекачать) |
| `Scripts/check-strings.py` | все ключи `L("…")` есть в ru/en, подстановки совпадают |
| `Scripts/screenshots.sh` | снимки экрана App Store в демо-режиме на симуляторе |
| `Scripts/bump-version.sh` | +0.0.1 к версии и +1 к номеру сборки |

## Демо-режим

Для снимков экрана и проверки интерфейса без сети:

```bash
xcrun simctl launch booted xyz.gojihub.vpn -GojiDemo YES -GojiDemoTab servers -GojiDemoVPN on
```

`-GojiDemoTab`: `home | servers | subscription | settings`; `-GojiDemoVPN`: `on | off | connecting`.
В Xcode — Edit Scheme → Run → Arguments. Из интерфейса режим недоступен, в сеть не ходит.

## Версии

- `Config/Version.xcconfig` — единый источник: `MARKETING_VERSION` (1.0.0) и
  `CURRENT_PROJECT_VERSION` (номер сборки, обязан расти с каждой загрузкой в App Store Connect).
- Правило проекта: **одна правка = одна версия = один коммит** → `./Scripts/bump-version.sh`
  в том же коммите. Тег `vX.Y.Z` на каждую версию.
- Публикация в TestFlight/App Store — только по решению владельца.

## Обновление ядра Xray

1. Найти релиз на https://github.com/XTLS/libXray/releases (CalVer, напр. `v26.10.15`).
2. Взять SHA256 ассета `libxray-apple-cgo.zip`:
   ```bash
   gh api repos/XTLS/libXray/releases/tags/v26.10.15 \
     --jq '.assets[] | select(.name=="libxray-apple-cgo.zip") | .digest'
   ```
3. Поменять `LIBXRAY_VERSION` и `LIBXRAY_SHA256` в `Scripts/fetch-libxray.sh`.
4. `./Scripts/fetch-libxray.sh`, сборка, проверка подключения на устройстве (особенно — смена узла
   и потребление памяти расширением в Xcode → Debug Navigator).
5. Изучить release notes libXray: API (`apiVersion`, методы) не гарантирует совместимость.

## CI (GitHub Actions, `.github/workflows/ios.yml`)

На каждый push в `main` и PR, раннер `macos-15`:

1. ядро (кэш по хэшу скрипта) + геоданные + XcodeGen;
2. проверка локализации;
3. сборка для симулятора;
4. юнит-тесты (`Tests/`) на симуляторе iPhone 16;
5. сборка Release для устройства (arm64) **без подписи** — проверяет линковку с LibXray;
6. снимки экрана в демо-режиме (iPhone 16 Pro Max, светлая/тёмная тема).

Артефакт `ios-build`: журналы сборки и `screenshots/`.

```bash
gh run list -R goji-app/goji-vpn-ios
gh run download <id> -R goji-app/goji-vpn-ios -n ios-build
```

Ручной запуск с `testflight = true` дополнительно собирает подписанный архив и загружает его в
App Store Connect — см. [RELEASE.md](RELEASE.md).

## Отладка туннеля на устройстве

- Журналы: «Настройки → Журналы» (основной, ядро, подписка, туннель; во вкладке «Ядро» —
  хвост `xray-error.log`). Уровень «Отладка» включает access-журнал Xray (только для диагностики).
- Консоль: Xcode → Debug → Attach to Process → `PacketTunnel` (расширение запускается системой).
  Или Console.app с фильтром `PacketTunnel`.
- Память расширения: Xcode → Debug Navigator при подключённом отладчике; лимит ~50 МБ — при
  превышении iOS убивает расширение (в журнале туннеля обрыв без ошибки).
- Сброс VPN-профиля: Настройки iOS → Основные → VPN и управление устройством → VPN → Goji VPN →
  Удалить VPN (или «Выйти из аккаунта» в приложении).

## Типичные проблемы

| Симптом | Причина / решение |
|---|---|
| `fetch-libxray.sh`: SHA256 не совпадает | файл повреждён/подменён — удалите `.downloads/` и повторите; при смене версии обновите хэш |
| Ошибка линковки `_res_9_*` / `SecTrust*` | не подтянулись `OTHER_LDFLAGS` из `Config/Base.xcconfig` — перегенерируйте проект |
| «Не удалось сохранить конфигурацию VPN» | нет capability Network Extension / App Groups у App ID или профиля; пользователь нажал «Не разрешать» |
| Туннель поднимается и сразу падает | см. журнал «Туннель»: нет профиля (`active-profile.json`), ошибка Xray в конфиге, не найден utun |
| В симуляторе «Включить» не работает | ожидаемо: NetworkExtension только на устройстве; используйте демо-режим |
