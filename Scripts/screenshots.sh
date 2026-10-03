#!/usr/bin/env bash
# Снимки экрана для App Store в демо-режиме (без сети и без NetworkExtension).
#   ./Scripts/screenshots.sh <путь к GojiVPN.app для симулятора> <папка вывода>
# Размер 6.9" (iPhone 16 Pro Max, 1320×2868) — обязательный для App Store Connect.
set -euo pipefail
APP="$1"
OUT="${2:-screenshots}"
DEVICE_NAME="${SCREENSHOT_DEVICE:-iPhone 16 Pro Max}"
BUNDLE_ID="xyz.gojihub.vpn"
mkdir -p "$OUT"

UDID=$(xcrun simctl list devices available -j | python3 -c "
import json, sys
name = sys.argv[1]
data = json.load(sys.stdin)['devices']
for runtime in sorted(data, reverse=True):
    for d in data[runtime]:
        if d['name'] == name and d['isAvailable']:
            print(d['udid']); sys.exit(0)
sys.exit(1)
" "$DEVICE_NAME")
echo "Симулятор: $DEVICE_NAME ($UDID)"
xcrun simctl boot "$UDID" 2>/dev/null || true
xcrun simctl bootstatus "$UDID" -b
# Чистая статус-строка: 9:41, полный заряд и сигнал.
xcrun simctl status_bar "$UDID" override --time "9:41" --batteryState charged --batteryLevel 100 \
  --cellularMode active --cellularBars 4 --wifiBars 3 --dataNetwork wifi || true
xcrun simctl install "$UDID" "$APP"

shot() {
  local name="$1" tab="$2" vpn="$3" appearance="$4"
  xcrun simctl ui "$UDID" appearance "$appearance"
  xcrun simctl terminate "$UDID" "$BUNDLE_ID" 2>/dev/null || true
  xcrun simctl launch "$UDID" "$BUNDLE_ID" -GojiDemo YES -GojiDemoTab "$tab" -GojiDemoVPN "$vpn" >/dev/null
  sleep 6
  xcrun simctl io "$UDID" screenshot "$OUT/$name.png"
  echo "  $name.png"
}

shot 01-home-connected home on light
shot 02-servers servers on light
shot 03-account subscription on light
shot 04-settings settings on light
shot 05-home-dark home on dark
shot 06-home-off home off light

xcrun simctl shutdown "$UDID" || true
ls -la "$OUT"
