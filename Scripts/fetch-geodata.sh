#!/usr/bin/env bash
# Встроенные в расширение туннеля геоданные — то же, что приложение потом само обновляет
# раз в неделю (GeoDataUpdater): geosite.dat (runetfreedom/russia-v2ray-rules-dat) и списки
# hxehex/russia-mobile-internet-whitelist. geoip.dat (~23 МБ) в сборку НЕ кладём — приложение
# скачивает его при первом запуске, до этого правила geoip:* просто пропускаются.
# Источники обновляются непрерывно, хэш зафиксировать нельзя — проверяем минимальный размер,
# чтобы HTML-страница ошибки не попала в сборку вместо данных.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$ROOT/PacketTunnel/Resources/GeoData"
mkdir -p "$DEST"

fetch() {
  local url="$1" out="$2" min="$3"
  if [[ -f "$DEST/$out" && "${FORCE:-0}" != "1" ]]; then
    echo "$out уже есть"
    return
  fi
  echo "Скачиваю $out…"
  curl -fL --retry 3 -o "$DEST/$out.part" "$url"
  local size
  size=$(wc -c < "$DEST/$out.part" | tr -d ' ')
  if (( size < min )); then
    echo "ОШИБКА: $out подозрительно мал ($size байт)" >&2
    rm -f "$DEST/$out.part"
    exit 1
  fi
  mv "$DEST/$out.part" "$DEST/$out"
}

fetch "https://raw.githubusercontent.com/runetfreedom/russia-v2ray-rules-dat/release/geosite.dat" geosite.dat 50000
fetch "https://raw.githubusercontent.com/hxehex/russia-mobile-internet-whitelist/main/whitelist.txt" mobile_whitelist_domains.txt 1000
fetch "https://raw.githubusercontent.com/hxehex/russia-mobile-internet-whitelist/main/cidrwhitelist.txt" mobile_whitelist_cidr.txt 1000
ls -la "$DEST"
