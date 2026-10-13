# Метаданные App Store Connect

Готовые тексты для карточки приложения. Ограничения длины App Store указаны в скобках —
тексты в них укладываются. Основной язык карточки — **русский**, дополнительная
локализация — **английский (США)**.

> Цены, тарифы и «где купить» в описании не упоминаем: приложение не продаёт подписку
> (см. правило 3.1.1 в [APP_STORE_COMPLIANCE.md](APP_STORE_COMPLIANCE.md#31--платежи)).

## Общие поля

| Поле | Значение |
|---|---|
| Bundle ID | `xyz.gojihub.vpn` (расширение туннеля — `xyz.gojihub.vpn.tunnel`) |
| SKU | `goji-vpn-ios` |
| Основная категория | Utilities (Утилиты) |
| Дополнительная категория | Productivity (Производительность) |
| Цена | Бесплатно |
| Copyright | `© 2026 <название организации-разработчика>` |
| Support URL | <кабинет>/ (лучше — отдельная страница поддержки с контактами) |
| Marketing URL | <кабинет>/ (необязательно) |
| Privacy Policy URL | новая версия политики (см. [PRIVACY.md](PRIVACY.md)); пока — https://telegra.ph/Politika-konfidencialnosti-Goji-VPN-09-20 |
| Устройства | iPhone (iOS 16.0+) |
| Витрины | все, кроме России и Китая (решение владельца — см. compliance) |

## Русский (ru)

**Название** (≤ 30): `Goji VPN`

**Подзаголовок** (≤ 30): `Быстрый и надёжный VPN`

**Текст продвижения** (≤ 170, можно менять без ревью):

```
Держит вас в сети даже при ограничениях мобильного интернета: автоматически выбирает рабочий сервер и возвращается на обычный, когда появляется Wi-Fi.
```

**Описание** (≤ 4000):

```
Goji VPN шифрует ваш интернет-трафик и направляет его через наши серверы в Европе. Провайдер и владельцы публичных Wi-Fi больше не видят, какие сайты вы открываете.

ПОДКЛЮЧЕНИЕ В ОДНО КАСАНИЕ
Выберите страну или доверьте выбор приложению — автовыбор сам подберёт самый быстрый и доступный сервер. На главном экране видно, где вы сейчас «находитесь», скорость и время подключения.

ДЕРЖИТ В СЕТИ
Если мобильный интернет ограничивают, приложение переключается на сервер, который продолжает работать, а при появлении Wi-Fi возвращается на обычный — без вашего участия.

СОВРЕМЕННЫЕ ПРОТОКОЛЫ
В основе — Xray-core: VLESS, Reality, XHTTP и другие протоколы, которые устойчивы к блокировкам и выглядят как обычный HTTPS.

ВАШИ ПРАВИЛА
• Kill switch — если туннель оборвётся, трафик не пойдёт в обход VPN.
• Автоподключение в чужих сетях Wi-Fi и список доверенных сетей, где VPN не нужен.
• Сайты мимо VPN — банки и сервисы, которые не работают через VPN, открываются напрямую.
• Siri и Быстрые команды: «Включи Goji VPN».

ПРОЗРАЧНОСТЬ
• Проверка IP и DNS прямо в приложении — видно, что именно видит внешний мир.
• Мы не ведём журналы посещённых сайтов, DNS-запросов и содержимого трафика.
• Никакой рекламы, трекеров и сторонней аналитики.

АККАУНТ И ПОДДЕРЖКА
Вход по email без пароля, перенос входа на другое устройство QR-кодом, управление подключёнными устройствами, приглашения друзей с бонусными днями и поддержка в чате прямо в приложении.

Для работы нужен аккаунт Goji VPN с активной подпиской. Новым пользователям доступен бесплатный пробный период.

Пользовательское соглашение: https://telegra.ph/Polzovatelskoe-soglashenie-Goji-VPN-09-20
```

**Ключевые слова** (≤ 100, через запятую без пробелов):

```
впн,vpn,прокси,безопасность,приватность,xray,vless,reality,wifi,защита,шифрование,быстрый
```

**Что нового** (для первой версии):

```
Первая версия Goji VPN для iPhone.
```

## English (en-US)

**Name** (≤ 30): `Goji VPN`

**Subtitle** (≤ 30): `Fast & reliable VPN`

**Promotional text** (≤ 170):

```
Stays online even when mobile internet is restricted: picks a working server automatically and switches back when Wi-Fi returns.
```

**Description** (≤ 4000):

```
Goji VPN encrypts your internet traffic and routes it through our servers in Europe. Your provider and public Wi-Fi owners can no longer see which sites you open.

ONE-TAP CONNECTION
Pick a country or let the app choose — auto-select finds the fastest available server. The home screen shows where you "are", your speed and connection time.

KEEPS YOU ONLINE
If mobile internet gets restricted, the app switches to a server that keeps working and returns to your usual one when Wi-Fi is back — automatically.

MODERN PROTOCOLS
Powered by Xray-core: VLESS, Reality, XHTTP and other censorship-resistant protocols that look like regular HTTPS.

YOUR RULES
• Kill switch — if the tunnel drops, traffic never leaks around the VPN.
• Auto-connect on untrusted Wi-Fi and a list of trusted networks where the VPN isn't needed.
• Sites outside the VPN — banks and services that block VPNs open directly.
• Siri & Shortcuts: "Turn on Goji VPN".

TRANSPARENCY
• Built-in IP & DNS check — see exactly what the outside world sees.
• No logs of visited sites, DNS queries or traffic contents.
• No ads, trackers or third-party analytics.

ACCOUNT & SUPPORT
Passwordless email sign-in, move your sign-in to another device with a QR code, manage connected devices, invite friends for bonus days, and chat with support right in the app.

A Goji VPN account with an active plan is required. New users can start a free trial.

Terms of Use: https://telegra.ph/Polzovatelskoe-soglashenie-Goji-VPN-09-20
```

**Keywords** (≤ 100):

```
vpn,proxy,privacy,security,xray,vless,reality,wifi,encryption,fast,secure,tunnel
```

**What's New:** `First release of Goji VPN for iPhone.`

## Снимки экрана

Обязательный размер — **6.9"** (1320 × 2868, iPhone 16 Pro Max); Apple масштабирует их для
остальных iPhone. CI делает их автоматически в демо-режиме (`Scripts/screenshots.sh`) —
артефакт `ios-build` → `screenshots/`:

1. `01-home-connected` — главная, VPN подключён (глобус, скорость, трафик);
2. `02-servers` — список серверов с пингом;
3. `03-account` — тариф, устройства, приглашения;
4. `04-settings` — настройки (kill switch, автоподключение, сайты мимо VPN);
5. `05-home-dark` — главная в тёмной теме;
6. `06-home-off` — главная до подключения.

Можно загрузить как есть или наложить подписи в макете (заголовок сверху, 2–4 слова на экран:
«Подключение в одно касание», «Держит в сети», «Ваш аккаунт», «Ваши правила»). Демо-данные не
должны вводить в заблуждение: на снимках только реальные функции приложения.

## Возрастной рейтинг

Анкета App Store Connect (2025+). Ответ на всё — **None / No**:

| Вопрос | Ответ |
|---|---|
| Cartoon or Fantasy Violence / Realistic Violence / Prolonged Graphic Violence | None |
| Profanity or Crude Humor / Mature or Suggestive Themes / Horror/Fear | None |
| Sexual Content or Nudity / Graphic Sexual Content | None |
| Alcohol, Tobacco, or Drug Use / Simulated Gambling | None |
| Medical or Treatment Information / Contests | None |
| Unrestricted Web Access | **No** (в приложении нет браузера/WebView) |
| User-Generated Content | No (чат поддержки — переписка с командой, не публичный контент) |
| Messaging and Chat | No (только поддержка) |
| Gambling / Loot boxes | No |

Ожидаемый рейтинг: **4+**.

## Заметки для App Review

Поле App Review Information → Notes (на английском):

```
Goji VPN is a client for our own VPN service (Xray-core based, VLESS/Reality protocols) built
with NetworkExtension (NEPacketTunnelProvider). The developer is enrolled as an organization.

DEMO ACCOUNT
Email: appreview@<домен>
Sign-in code: 000000  (static code for this review account only; no inbox access needed)
The account has an active subscription and free device slots.

HOW TO TEST
1. Launch the app → read the privacy disclosure → "Got it, continue".
2. Enter the demo email → "Get a code" → enter 000000.
3. Home tab → "Connect". iOS asks to add a VPN configuration → Allow.
   The status changes to connected; the globe shows the route; speed is displayed.
4. Servers tab → pick another country — the tunnel switches without disconnecting.
5. Settings → Kill switch / Auto-connect / Sites outside the VPN / IP & DNS check.
6. Account tab → plan, devices, referral link, "Move sign-in" QR code.
7. Settings → Account → Delete account (initiates deletion; please don't submit it for the
   demo account).

PURCHASES
The app does not sell subscriptions and contains no links to external purchases. Users sign in
to an existing account; new users can activate a free trial in the app.

DATA
No traffic, DNS or browsing logs. No ads, analytics or tracking SDKs. Data collected: email
(sign-in), a random device identifier and device model (device limit), support messages.
```

Поля **Sign-in required → Yes**, User name / Password — данные демо-аккаунта (см. блокер №2 в
compliance: статический код ревью должен сделать бэкенд).

**Контакт для ревью:** имя, телефон и email человека, который ответит Apple в течение суток.
