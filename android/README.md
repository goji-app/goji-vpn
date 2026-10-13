# Goji VPN — Android

Android-клиент Goji VPN: Kotlin, Jetpack Compose (Material 3 Expressive), Hilt. Туннель — Xray-core через [libXray](https://github.com/XTLS/libXray) и [hev-socks5-tunnel](https://github.com/heiher/hev-socks5-tunnel). Общий обзор всех приложений — в [README репозитория](../README.md).

| | |
|---|---|
| Пакет | `xyz.gojihub.vpn` |
| Android | 7.0+ (minSdk 24), targetSdk 37 |
| Процессоры | arm64-v8a, armeabi-v7a — одна универсальная сборка |
| Скачать | [последний релиз](https://github.com/goji-app/goji-vpn/releases/latest) → `Goji-<версия>.apk` |

## Возможности

**Подключение**
- VPN-туннель VLESS (Reality, XHTTP) через Xray-core; служба VPN с уведомлением: страна, скорость, кнопка «Отключить».
- 3D-глобус (OpenGL ES) со страной подключения и маршрутом.
- «Держит тебя в сети»: при глушении мобильной связи — переход на российский узел, на Wi-Fi — обратно, автоматически.
- Kill switch, системный Always-on VPN, плитка в шторке, ярлык «Подключиться: <последний узел>».
- Правила сетей (доверенные Wi-Fi, автоподключение в чужих), приложения и сайты мимо VPN, российские сервисы напрямую по геоданным.

**Главная** — компактная кнопка, глобус и плитки без прокрутки: скорость с графиком, качество канала (задержка и потери), подписка; карточка пробного периода для аккаунта без подписки.

**Аккаунт** — вход по коду на email или QR-кодом с другого устройства; подписка с кольцом оставшихся дней; устройства с названиями как на сайте (модель, система, версия, клиент); приглашения друзей; новости; удаление аккаунта. Данные кабинета хранятся на телефоне в зашифрованном виде и показываются сразу, обновление в фоне — не чаще раза в час.

**Прочее** — поддержка в приложении (обращения, чат, вложения, FAQ), журнал сети, проверка утечек IP/DNS, журналы по категориям, виджеты (основной 2×1–3×3, «Статус», «Подписка»), языки RU/EN/ZH, светлая и тёмная тема, размер шрифта.

## Две сборки

| | `site` — сайт загрузки и GitHub | `play` — Google Play |
|---|---|---|
| Самообновление («Настройки» → «Обновления») | есть | нет |
| Кнопка «Оплатить в личном кабинете» | есть | нет |
| Формат | APK | AAB |

```bash
./gradlew assembleSiteRelease   # app/build/outputs/apk/site/release/Goji.apk
./gradlew bundlePlayRelease     # app/build/outputs/bundle/playRelease/app-play-release.aab
```

## Сборка

Android Studio (JDK 17), Android SDK 37. Перед первой сборкой положите:

- `app/libs/libXray.aar` — собирается по инструкции [XTLS/libXray](https://github.com/XTLS/libXray) (`python build/main.py android`);
- `app/keystore.properties` и keystore — только для подписанного релиза (`storeFile`, `storePassword`, `keyAlias`, `keyPassword`).

Реализация подъёма туннеля (`GodjiVpnService`) в репозитории не публикуется — в коде оставлена только форма класса, нужная для сборки остального приложения.

> **Windows:** Android Gradle Plugin не собирает проект из папки с кириллицей в пути. Соберите копию `android/` в ASCII-пути (например, `C:\build\goji`).

## Структура

```
app/src/main/java/xyz/gojihub/vpn/
├─ auth/          вход: email-код, перенос QR, токены и их продление
├─ di/            Hilt: Retrofit/OkHttp, продление сессии
├─ geo/           страна и город узла, приветствия на языке страны
├─ globe/         OpenGL-глобус
├─ i18n/          строки RU/EN/ZH
├─ journal/       журнал сети
├─ network/       API кабинета, диагностика утечек, мониторинг сети, правила Wi-Fi
├─ settings/      настройки (DataStore)
├─ subscription/  подписка, серверы, пинг, кэш кабинета, уведомления
├─ update/        самообновление (сборка site) и зеркала загрузки
├─ util/          журналы, даты, длина оплаченного периода и т.п.
├─ vpn/           служба VPN (форма класса), плитка, ярлык
├─ widget/        виджеты
└─ ui/            экраны: connect, servers, plans, settings, login, support, theme
```
