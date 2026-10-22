#!/bin/sh
# Скачивает готовые части ядра в Runtime/arm64 и Runtime/x64 и сверяет их с Runtime/SHA256SUMS:
#   xray                   — официальная сборка Xray-core (Go 1.27, macOS 13+): на macOS 13 и новее;
#   geoip.dat, geosite.dat — из того же релиза Xray-core;
#   x64/sing-box           — официальная сборка sing-box legacy-macos-10.13 (Intel, macOS 10.13+).
# xray-legacy (обе архитектуры, для macOS 10.15–12) и arm64/sing-box собирает
# build-legacy-cores.sh: Go 1.27 и официальные сборки на старых macOS не работают.
set -eu
cd "$(dirname "$0")"
XRAY=v26.10.21
SINGBOX=1.14.3
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

fetch_xray() { # $1 — архив релиза Xray, $2 — папка архитектуры
  curl -fsSL -o "$tmp/$1" "https://github.com/XTLS/Xray-core/releases/download/$XRAY/$1"
  mkdir -p "$tmp/$2-xray" "$2"
  unzip -q -o "$tmp/$1" -d "$tmp/$2-xray"
  cp "$tmp/$2-xray/xray" "$tmp/$2-xray/geoip.dat" "$tmp/$2-xray/geosite.dat" "$2/"
  chmod 755 "$2/xray"
}

fetch_xray Xray-macos-arm64-v8a.zip arm64
fetch_xray Xray-macos-64.zip x64
name="sing-box-$SINGBOX-darwin-amd64-legacy-macos-10.13"
curl -fsSL -o "$tmp/$name.tar.gz" "https://github.com/SagerNet/sing-box/releases/download/v$SINGBOX/$name.tar.gz"
tar -xzf "$tmp/$name.tar.gz" -C "$tmp"
cp "$tmp/$name/sing-box" x64/
chmod 755 x64/sing-box
shasum -a 256 -c SHA256SUMS
