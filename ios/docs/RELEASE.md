# Выпуск в App Store — пошагово

Порядок действий от нуля до опубликованного приложения. Шаги 1–4 выполняются **один раз**.
Перед началом прочитайте блокеры в [APP_STORE_COMPLIANCE.md](APP_STORE_COMPLIANCE.md).

## 1. Аккаунт Apple Developer Program (организация)

VPN-приложения Apple принимает **только от организаций** (правило 5.4).

1. Юрлицо и номер **D-U-N-S** (бесплатно: https://developer.apple.com/enroll/duns-lookup/ —
   выдача 1–14 дней). На D-U-N-S должно совпадать точное юридическое название и адрес.
2. Apple ID владельца с двухфакторной аутентификацией.
3. Регистрация: https://developer.apple.com/programs/enroll/ → Organization. Нужен сайт
   организации на её домене и право подписи от имени организации. Стоимость — $99/год.
4. Проверка Apple — обычно несколько дней (могут позвонить).
5. После вступления: Agreements, Tax, and Banking → принять Free Apps Agreement (для
   бесплатного приложения достаточно; Paid Apps — только если появятся встроенные покупки).

> Оплата и регистрация из России могут быть затруднены санкциями — обычно используют юрлицо и
> банковскую карту в другой юрисдикции. Это решение владельца.

## 2. Идентификаторы и capabilities (developer.apple.com → Certificates, IDs & Profiles)

При автоматической подписи Xcode/CI создаст их сам, но проверить нужно:

| Идентификатор | Тип | Capabilities |
|---|---|---|
| `xyz.gojihub.vpn` | App ID | **Network Extensions**, **App Groups** (`group.xyz.gojihub.vpn`) |
| `xyz.gojihub.vpn.tunnel` | App ID | **Network Extensions**, **App Groups** (`group.xyz.gojihub.vpn`) |
| `group.xyz.gojihub.vpn` | App Group | — |

Если идентификатор `xyz.gojihub.vpn` уже занят другой командой (маловероятно), поменяйте
`GOJI_APP_BUNDLE_ID`/`GOJI_TUNNEL_BUNDLE_ID`/`GOJI_APP_GROUP` в `Config/Base.xcconfig` —
больше нигде их менять не нужно.

## 3. Запись приложения в App Store Connect

https://appstoreconnect.apple.com → Apps → «+» → New App:

- Platform: iOS; Name: **Goji VPN** (если занято — «Goji VPN: быстрый и надёжный»);
- Primary language: Russian; Bundle ID: `xyz.gojihub.vpn`; SKU: `goji-vpn-ios`;
- User Access: Full Access.

Далее заполнить по [APP_STORE_METADATA.md](APP_STORE_METADATA.md): описание, ключевые
слова, категории, URL поддержки и политики, возрастной рейтинг, App Privacy (по
[PRIVACY.md](PRIVACY.md)), Pricing and Availability (бесплатно; витрины — без России и Китая).

## 4. Ключ App Store Connect API и секреты CI

CI подписывает и загружает сборки сам, через **облачную подпись** Apple — сертификаты и
профили создаются автоматически, хранить `.p12` не нужно.

1. App Store Connect → Users and Access → Integrations → App Store Connect API → **Team Keys**
   → «+»: имя `goji-ci`, роль **Admin** (нужна для облачных сертификатов распространения).
2. Скачать `AuthKey_XXXXXXXXXX.p8` (скачивается один раз!), записать **Key ID** и **Issuer ID**.
3. GitHub → `goji-app/goji-vpn-ios` → Settings → Environments → создать окружение
   **`app-store`** (можно добавить обязательное подтверждение запуска) и в нём секреты:

| Секрет | Значение |
|---|---|
| `APPLE_TEAM_ID` | Team ID (10 символов, developer.apple.com → Membership) |
| `ASC_KEY_ID` | Key ID ключа API |
| `ASC_ISSUER_ID` | Issuer ID |
| `ASC_KEY_P8_BASE64` | содержимое `.p8` в base64: `base64 -i AuthKey_XXXX.p8 \| pbcopy` |

`.p8` после этого удалить с диска или сохранить в менеджере паролей. В репозиторий — никогда.

## 5. Каждая версия

1. Изменения → `./Scripts/bump-version.sh` → коммит → тег `vX.Y.Z`.
2. Дождаться зелёного CI (сборка, тесты, скриншоты).
3. GitHub → Actions → **iOS** → Run workflow → `testflight = true` (или
   `gh workflow run ios.yml -R goji-app/goji-vpn-ios -f testflight=true`).
   Задание `release` соберёт архив, подпишет и загрузит в App Store Connect; dSYM — в
   артефакте `ios-release`.
4. App Store Connect → TestFlight: через 5–30 минут сборка пройдёт обработку.
   - **Missing Compliance** → ответы про шифрование (см. compliance, раздел «Экспортный контроль»).
   - Внутреннее тестирование (до 100 человек из команды) — сразу; внешнее — после
     бета-ревью Apple (обычно < 1 суток).
5. Проверить на реальных устройствах по [TESTING.md](TESTING.md).
6. App Store → версия → выбрать сборку → «Что нового» → **Add for Review** → Submit.
   Обычно ревью — до 24–48 часов.
7. После одобрения: выпуск вручную или автоматически (настройка «Version Release»).

### Без CI (с Mac)

Xcode → Product → Archive → Organizer → Distribute App → App Store Connect → Upload.
Подпись — автоматическая под своей командой (`Config/Signing.local.xcconfig`).

## 6. Если ревью отклонило

| Причина | Что делать |
|---|---|
| 5.4 — разработчик не организация | только перевод аккаунта на организацию |
| 2.1 — не смогли войти | проверить демо-аккаунт и статический код, подписку демо-аккаунта |
| 3.1.1 — «доступ к платному контенту без IAP» | ответить в Resolution Center: приложение — клиент для аккаунтов, купленных вне приложения (3.1.3(b)), в приложении нет покупок и ссылок; если настаивают — интеграция StoreKit (см. compliance) |
| 5.1.1(v) — удаление аккаунта | показать путь: Настройки → Аккаунт → Удалить аккаунт; при необходимости — реализовать эндпоинт удаления |
| 5.1.1 — политика конфиденциальности | обновить текст по PRIVACY.md, проверить ссылку |
| 4.0 / 2.3 — скриншоты | снимки должны показывать реальный интерфейс; демо-данные — допустимы |

Ответы в Resolution Center пишите на английском, коротко и по пунктам; приложите путь по
экранам и, при необходимости, видеозапись экрана с реального устройства.

## 7. Поддержка после выпуска

- Ежегодно до 1 февраля — отчёт BIS об экспорте шифрования (см. compliance).
- Ежегодно — продление Apple Developer Program ($99), иначе приложение пропадёт из App Store.
- Следить за письмами App Store Connect (обновления правил, требования к новым SDK: Apple
  требует собирать новыми Xcode/SDK — обычно с апреля каждого года).
- Обновлять ядро Xray (см. [BUILD.md](BUILD.md#обновление-ядра-xray)).
