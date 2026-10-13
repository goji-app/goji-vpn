# Goji VPN

Все клиенты VPN-сервиса Goji VPN в одном репозитории: Android, Windows, macOS и iOS. Один бэкенд (gojihub.xyz на Remnawave), одно ядро (Xray-core), один дизайн «Goji Expressive» (Material 3).

| Папка | Платформа | Стек |
|---|---|---|
| [`android/`](android) | Android 7.0+ | Kotlin, Jetpack Compose, Hilt, Xray-core (libXray) + hev-socks5-tunnel |
| [`windows/`](windows) | Windows 10/11 | WPF / .NET 8, Xray-core + sing-box (Wintun) |
| [`macos/`](macos) | macOS 10.15+ (Intel), 11+ (Apple Silicon) | Avalonia 11 / .NET 8, Xray-core + sing-box (TUN) |
| [`ios/`](ios) | iPhone, iOS 16+ | SwiftUI + Packet Tunnel, Xray-core (libXray) |

## Скачать

Все файлы — в [последнем релизе](https://github.com/goji-app/goji-vpn/releases/latest):

- **Android** — `Goji-<версия>.apk`;
- **Windows** — `GodjiVpn-Setup-<версия>.exe`;
- **Mac** (Intel и Apple Silicon) — `GojiVPN-<версия>-macOS.dmg`; файлы `-macOS-*.zip` нужны для автообновления внутри приложения;
- **iPhone** — готовится к App Store.

## Версии и релизы

- Android, Windows и macOS идут **одним номером версии**, и каждый релиз содержит файлы всех трёх систем: приложения ищут обновления в `/releases/latest` (Android — `.apk`, Windows — `.exe`, macOS — `-macOS-<arch>.zip`), и если в последнем релизе не окажется файла какой-то системы, она перестанет видеть обновления.
- iOS выходит через App Store со своей версией (`ios/Config/Version.xcconfig`).
- Сначала приложения проверяют сайт загрузки `get.gojihub.xyz/latest.json` (открывается без VPN), GitHub — запасной путь.

## Сборка

- **Android** — см. [android/README.md](android/README.md). Две сборки: `site` (APK для сайта и GitHub, с самообновлением) и `play` (AAB для Google Play). Служба VPN в репозитории — заглушка, настоящая реализация туннеля не публикуется.
- **Windows** — `windows/build-installer.ps1`, см. [windows/README.md](windows/README.md).
- **macOS** — GitHub Actions `macOS build` (запуск вручную) или `python macos/build-macos.py`, см. [macos/README.md](macos/README.md).
- **iOS** — только GitHub Actions `iOS` (собирается сам при изменениях в `ios/`), см. [ios/README.md](ios/README.md).

## История

Репозиторий собран 2026-10-13 из `goji-vpn-android` (переименован в `goji-vpn`), `goji-vpn-desktop` и `goji-vpn-ios` — история всех трёх сохранена.
