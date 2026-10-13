#!/bin/bash
# Карантин как у скачанного из браузера .dmg: запускается ли ядро с com.apple.quarantine,
# если его стартует root-обёртка (а не само приложение)?
set -u
cd "$(dirname "$0")/.."
work=$(mktemp -d)
cp Runtime/arm64/sing-box Runtime/arm64/xray "$work/"
codesign --force --sign - "$work/sing-box" "$work/xray"
q="0083;$(printf %x "$(date +%s)");Safari;$(uuidgen)"
xattr -w com.apple.quarantine "$q" "$work/sing-box"
xattr -w com.apple.quarantine "$q" "$work/xray"
xattr -l "$work/sing-box"
spctl --status || true
echo "== от пользователя"
"$work/xray" version | head -1; echo "exit=$?"
echo "== от root через /bin/sh (как обёртка)"
sudo /bin/sh -c "'$work/sing-box' version" ; echo "exit=$?"
sudo env -i PATH=/usr/bin:/bin:/usr/sbin:/sbin nohup /bin/sh -c "'$work/sing-box' version > '$work/out.txt' 2>&1"; echo "exit=$?"; cat "$work/out.txt"
log show --last 2m --predicate 'process == "syspolicyd" OR process == "kernel"' 2>/dev/null | grep -iE "sing-box|quarantine|gatekeeper|deny" | tail -10 || true
