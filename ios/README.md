# Goji VPN — iOS

iOS-клиент Goji VPN: SwiftUI-приложение + расширение Packet Tunnel с ядром
[Xray-core](https://github.com/XTLS/Xray-core) (через [libXray](https://github.com/XTLS/libXray)).
Тот же бэкенд, что у Android/Windows/macOS-клиентов (кабинет + Remnawave).

- iPhone, iOS 16+; языки — русский (основной) и английский;
- версия **1.0.128 (5)** — `Config/Version.xcconfig`; номер версии общий с Android, Windows и macOS.

## Возможности

- Вход по email-коду, перенос входа QR-кодом с другого устройства (в коде только почта, дальше код из письма), пробный период.
- Экраны знакомства перед обязательным экраном о данных.
- Подключение в одно касание, список серверов с пингом (через прокси или TCP), избранное,
  сортировка, смена узла без разрыва туннеля.
- Главная: глобус с маршрутом, реальная страна выхода, скорость, время, трафик и срок подписки.
- «Держать в сети»: при глушении мобильной сети — переход на LTE-узел и обратно.
- Kill switch, автоподключение (в чужих Wi-Fi / всегда), доверенные Wi-Fi, сайты мимо VPN.
- Маршрутизация российских сервисов напрямую (category-ru + mobile-whitelist), автообновление
  геоданных.
- Аккаунт: тариф, устройства (переименование/удаление), приглашения, новости.
- Поддержка в приложении: обращения, чат, вложения (фото/видео/PDF/журналы), FAQ.
- Понятные ошибки подключения с кнопкой следующего шага, отчёт о сбое одним нажатием (MetricKit).
- Проверка IP/DNS-утечек, журналы, напоминания об окончании подписки, Siri/Быстрые команды,
  светлая/тёмная тема, Dynamic Type, VoiceOver.

## Быстрый старт (Mac)

```bash
brew install xcodegen
./Scripts/bootstrap.sh       # ядро + геоданные + GojiVPN.xcodeproj
open GojiVPN.xcodeproj
```

VPN работает только на реальном iPhone и с платным аккаунтом Apple Developer
(capability Network Extension). Интерфейс без сети — в демо-режиме: аргумент запуска
`-GojiDemo YES`.

## Документация

| Документ | О чём |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | устройство приложения и туннеля, ядро, данные, отличия от Android |
| [docs/BUILD.md](docs/BUILD.md) | сборка, скрипты, CI, обновление ядра, отладка |
| [docs/RELEASE.md](docs/RELEASE.md) | выпуск в App Store: аккаунт, App Store Connect, секреты CI, TestFlight, ревью |
| [docs/APP_STORE_COMPLIANCE.md](docs/APP_STORE_COMPLIANCE.md) | разбор правил App Store, **блокеры публикации** |
| [docs/APP_STORE_METADATA.md](docs/APP_STORE_METADATA.md) | тексты карточки RU/EN, ключевые слова, рейтинг, заметки для ревью |
| [docs/PRIVACY.md](docs/PRIVACY.md) | ответы App Privacy, раздел политики конфиденциальности для iOS |
| [docs/TESTING.md](docs/TESTING.md) | автотесты и чек-лист ручной проверки на устройстве |
| [docs/USER_GUIDE.md](docs/USER_GUIDE.md) | руководство пользователя (для поддержки) |
| [CHANGELOG.md](CHANGELOG.md) | история версий |

## Структура

```
App/Sources        приложение (SwiftUI): App, Core, DesignSystem, Features
App/Resources      шрифты, ассеты, строки ru/en, geo_globe.json, PrivacyInfo.xcprivacy
PacketTunnel/      расширение NEPacketTunnelProvider
Shared/            общий код: XrayCore, XrayConfigBuilder, GeoDataParser, хранилище, журнал
Tests/             юнит-тесты
Config/            xcconfig, Info.plist, entitlements, ExportOptions
Scripts/           bootstrap, загрузка ядра/геоданных, скриншоты, версия, проверка строк
project.yml        описание проекта для XcodeGen
```

## Лицензии сторонних компонентов

- Xray-core — MPL-2.0; libXray — MIT (Project X / XTLS).
- Шрифты Manrope, Instrument Serif — SIL Open Font License 1.1.
- Геоданные: runetfreedom/russia-v2ray-rules-dat, hxehex/russia-mobile-internet-whitelist.
