# Goji VPN для macOS

Клиент Goji VPN для Mac: Avalonia 11 / .NET 8, Xray-core + sing-box (TUN).
Интерфейс и возможности такие же, как у Windows- и Android-версий: дизайн «Стекло», глобус,
серверы с пингом и избранным, подписка, поддержка прямо в приложении, «Сайты мимо VPN»,
проверка утечек, значок в строке меню.

Работает на **macOS 10.15 Catalina и новее** (Mac с Intel) и **macOS 11 Big Sur и новее**
(Mac с Apple Silicon).

## Установка

1. Скачайте установщик из [последнего релиза](https://github.com/goji-app/goji-vpn-desktop/releases/latest):
   `GojiVPN-<версия>-macOS.dmg` — один для любого Mac (Intel и Apple Silicon).
2. Откройте `.dmg` и перетащите **Goji VPN** в «Программы».
3. Приложение не заверено Apple, поэтому при первом запуске macOS его остановит. Откройте
   «Системные настройки → Конфиденциальность и безопасность» и нажмите «Всё равно открыть».
   Другой способ — выполнить в Терминале:
   ```
   xattr -dr com.apple.quarantine "/Applications/Goji VPN.app"
   ```
4. При подключении macOS попросит пароль администратора — он нужен, чтобы создать
   VPN-интерфейс для всего трафика Mac.

## Сборка

Основной путь — GitHub Actions (`.github/workflows/macos.yml`, запуск вручную): сборка на Mac,
подпись `codesign`, универсальный установщик `.dmg`, zip для автообновления и пробный запуск со снимками экрана.

Локально (на Windows или macOS):

Нужны .NET 8 SDK, Python 3 с Pillow и `tools/rcodesign` (ad-hoc подпись без Mac).
Ядра под обе архитектуры кладутся в `Runtime/arm64` и `Runtime/x64` — см. `Runtime/README.md`.

```
python build-macos.py            # обе архитектуры
python build-macos.py arm64      # только Apple Silicon
```

Результат — `dist/GojiVPN-<версия>-macOS-<arch>.zip`.
