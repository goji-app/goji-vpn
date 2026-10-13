#!/bin/bash
# Пробный запуск на macOS (GitHub Actions): ставит приложение из универсального .dmg как
# пользователь, запускает оба среза (arm64 нативно, x64 через Rosetta), снимает экран и собирает
# журналы в smoke/. Туннель не поднимается — для него нужна подписка и пароль администратора.
set -u
cd "$(dirname "$0")/.."
mkdir -p smoke
status=0
APP="/Applications/Goji VPN.app"
EXE="$APP/Contents/MacOS/GojiVpn"

shot() { sleep "$2"; screencapture -x "smoke/$1.png" || true; }
alive() { if pgrep -x GojiVpn >/dev/null; then echo "OK: $1 работает"; else echo "FAIL: $1 не запущен"; status=1; fi; }
stop_app() { pkill -x GojiVpn || true; sleep 2; }

dmg=$(ls dist/*-macOS.dmg)
echo "== Образ: $dmg"
hdiutil attach "$dmg" -mountpoint /Volumes/goji -noautoopen
ls -la /Volumes/goji
open /Volumes/goji
shot 0-dmg-window 4
osascript -e 'tell application "Finder" to close every window' || true
rm -rf "$APP"
ditto "/Volumes/goji/Goji VPN.app" "$APP"
hdiutil detach /Volumes/goji -force

echo "== Универсальный запускатель и подпись"
lipo -info "$EXE" || status=1
lipo -archs "$EXE" | grep -q x86_64 && lipo -archs "$EXE" | grep -q arm64 && echo "OK: оба среза" || { echo "FAIL: нет одного из срезов"; status=1; }
/usr/libexec/PlistBuddy -c "Print :LSMinimumSystemVersion" -c "Print :LSMinimumSystemVersionByArchitecture" "$APP/Contents/Info.plist"
codesign --verify --deep --strict --verbose=2 "$APP" || status=1
codesign -dv "$APP" 2>&1 | grep -E "Identifier|Signature|Format"
spctl --assess --type execute -vv "$APP" 2>&1 || true  # без Developer ID отказ ожидаем

echo "== Обычный запуск (экран входа, строка меню)"
open "$APP"
shot 1-arm64-login 25
alive "arm64"
stop_app

echo "== Превью оболочки с демо-данными"
for tab in 0 1 2 3; do
  open -n --env GODJI_UI_PREVIEW=1 --env GODJI_UI_PREVIEW_DEMO=1 --env GODJI_UI_PREVIEW_SCREEN=shell \
       --env GODJI_UI_PREVIEW_TAB=$tab --env GODJI_UI_PREVIEW_THEME=$([ $tab = 0 ] && echo dark || echo light) \
       "$APP"
  shot "2-arm64-tab$tab" 15
  alive "arm64 tab $tab"
  stop_app
done

echo "== Intel-срез того же приложения через Rosetta"
if /usr/bin/arch -x86_64 /usr/bin/true 2>/dev/null; then
  GODJI_UI_PREVIEW=1 GODJI_UI_PREVIEW_DEMO=1 GODJI_UI_PREVIEW_SCREEN=shell /usr/bin/arch -x86_64 "$EXE" > smoke/x64-stdout.log 2>&1 &
  shot 3-x64-rosetta 40
  alive "x64 (Rosetta)"
  ps -o pid=,command= -p "$(pgrep -x GojiVpn | head -1)" || true
  stop_app
else
  echo "Rosetta нет — Intel-срез не проверяю"
fi

echo "== Ядро и TLS: проверка сертификатов системными корнями (патч crypto/x509 для старых macOS)"
for arch in arm64 x64; do
  rt="$APP/Contents/Resources/app-$arch/Runtime"
  run=""; [ $arch = x64 ] && run="/usr/bin/arch -x86_64"
  for f in xray sing-box; do
    printf '%s %s: ' "$arch" "$f"; vtool -show-build "$rt/$f" | awk '/minos|^ *version/{print $1 " " $2; exit}'
    $run "$rt/$f" version | head -1 || status=1
  done
  if $run "$rt/xray" tls ping www.apple.com 2>&1 | grep -qi "succeeded"; then echo "OK: $arch xray TLS"; else echo "FAIL: $arch xray TLS"; $run "$rt/xray" tls ping www.apple.com 2>&1 | tail -5; status=1; fi
  if $run "$rt/sing-box" tools fetch https://www.apple.com/ > /dev/null 2>smoke/sb-fetch.err; then echo "OK: $arch sing-box TLS"; else echo "FAIL: $arch sing-box TLS"; cat smoke/sb-fetch.err; status=1; fi
done

echo "== Журналы приложения"
find "$HOME/Library/Application Support" "$HOME/.local/share" -path "*GodjiVpn*" -name "*.log" 2>/dev/null | while read -r f; do
  echo "--- $f"; tail -n 60 "$f"; cp "$f" "smoke/$(basename "$f")"
done
exit $status
