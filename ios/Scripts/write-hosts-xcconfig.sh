#!/usr/bin/env bash
# Пишет Config/Hosts.local.xcconfig (в .gitignore) с адресами кабинета и сайта загрузки.
# Значения — из переменных окружения GOJI_SITE_URL / GOJI_DOWNLOAD_URL (в CI — из секретов)
# или из ../goji-hosts.properties в корне монорепозитория (образец — goji-hosts.example.properties).
set -euo pipefail
cd "$(dirname "$0")/.."
PROPS=../goji-hosts.properties
value() {
  local v="${!1:-}"
  if [[ -z "$v" && -f "$PROPS" ]]; then v=$(grep -E "^$1=" "$PROPS" | head -1 | cut -d= -f2- | tr -d '\r'); fi
  v="${v%/}"
  [[ "$v" == https://* ]] || { echo "Не задан $1 (https://…): переменная окружения или $PROPS" >&2; exit 1; }
  echo "${v#https://}"
}
SITE=$(value GOJI_SITE_URL)
DOWNLOAD=$(value GOJI_DOWNLOAD_URL)
# В xcconfig «//» начинает комментарий, поэтому храним только хосты, схему добавляет код.
printf 'GOJI_SITE_HOST = %s\nGOJI_DOWNLOAD_HOST = %s\n' "$SITE" "$DOWNLOAD" > Config/Hosts.local.xcconfig
echo "Config/Hosts.local.xcconfig записан"
