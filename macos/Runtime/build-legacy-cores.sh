#!/bin/bash
# Собирает ядро для старых macOS (запускать на Mac, в CI — шаг workflow macos.yml):
#   x64/xray        — Intel, macOS 10.13+
#   arm64/xray      — Apple Silicon, macOS 11+
#   arm64/sing-box  — Apple Silicon, macOS 11+
# (x64/sing-box берётся готовым: официальный sing-box-…-darwin-amd64-legacy-macos-10.13.)
#
# Официальные сборки Go 1.26 требуют macOS 12+. Как и sing-box для своих legacy-сборок, берём
# Go 1.26.7 с тремя патчами SagerNet (возврат старых версий Mach-O и прежнего способа
# проверки цепочек сертификатов в crypto/x509) и собираем с CGO, чтобы минимальную версию
# проставил системный линкер по MACOSX_DEPLOYMENT_TARGET. В xray нет cgo-кода, поэтому ему
# внешний линкер включается явно (-linkmode=external) — иначе встроенный линкер Go пишет 12.0.
# with_usbip из тегов sing-box убран: он зовёт API macOS 12, а USB/IP клиенту не нужен.
# Все загрузки сверяются по SHA-256.
set -euo pipefail
cd "$(dirname "$0")"

GO_VERSION=1.26.7
GO_SHA256=020a1e8224811be75163e920bc77e0926a1390a6aeea19bdcf23f74b9d749f6d
XRAY_TAG=v26.3.27
SINGBOX_TAG=v1.14.0
SINGBOX_TAGS=with_gvisor,with_quic,with_dhcp,with_wireguard,with_utls,with_acme,with_clash_api,with_tailscale,with_ccm,with_ocm,with_cloudflared,with_openvpn,with_openconnect,badlinkname,tfogo_checklinkname0
SINGBOX_LDFLAGS="-X runtime.godebugDefault=multipathtcp=0,tlssha1=1 -checklinkname=0"
PATCHES=(
  "f080b0c6346eb690c0dc82497b35925f385b35ac 0a389ce628917baa6436628303facd37fa0d7080229ef918cb9d3ccc037551dc"
  "2d9c12887c342fb9051d231aa5388743cb7e9cb6 80f7162a08762f319a61710e233b07463af227dce694a01e7ae3c13f1379280f"
  "367663849c656612fc3c8875ef6e7f6b0c93dbd0 56b682ea452c68c49cb7041e534069777349267f3ee6cf7280f267f4ae3293de"
)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
check() { echo "$2  $1" | shasum -a 256 -c -; }

echo "== Go $GO_VERSION с патчами для старых macOS"
curl -fsSL -o "$work/go.tgz" "https://dl.google.com/go/go${GO_VERSION}.darwin-arm64.tar.gz"
check "$work/go.tgz" "$GO_SHA256"
tar -xzf "$work/go.tgz" -C "$work"
for p in "${PATCHES[@]}"; do
  set -- $p
  curl -fsSL -o "$work/$1.diff" "https://github.com/SagerNet/go/commit/$1.diff"
  check "$work/$1.diff" "$2"
  (cd "$work/go" && patch -p1 --quiet < "$work/$1.diff")
done
export GOROOT="$work/go" PATH="$work/go/bin:$PATH" GOTOOLCHAIN=local CGO_ENABLED=1 GOOS=darwin
go version

echo "== Xray-core $XRAY_TAG"
git clone -q --depth 1 --branch "$XRAY_TAG" https://github.com/XTLS/Xray-core.git "$work/xray"
commit=$(git -C "$work/xray" rev-parse --short HEAD)
for arch in amd64 arm64; do
  if [ "$arch" = amd64 ]; then dir=x64; target=10.13; else dir=arm64; target=11.0; fi
  mkdir -p "$dir"
  (cd "$work/xray" && GOARCH=$arch MACOSX_DEPLOYMENT_TARGET=$target \
    go build -o "$work/xray-$arch" -trimpath -buildvcs=false \
      -ldflags "-linkmode=external -X github.com/xtls/xray-core/core.build=$commit -s -w -buildid=" ./main)
  cp "$work/xray-$arch" "$dir/xray"
done

echo "== sing-box $SINGBOX_TAG (arm64)"
git clone -q --depth 1 --branch "$SINGBOX_TAG" https://github.com/SagerNet/sing-box.git "$work/sing-box"
(cd "$work/sing-box" && GOARCH=arm64 MACOSX_DEPLOYMENT_TARGET=11.0 \
  go build -o "$work/sing-box-arm64" -trimpath -tags "$SINGBOX_TAGS" \
    -ldflags "-X 'github.com/sagernet/sing-box/constant.Version=${SINGBOX_TAG#v}' $SINGBOX_LDFLAGS -s -w -buildid=" \
    ./cmd/sing-box)
mkdir -p arm64
cp "$work/sing-box-arm64" arm64/sing-box
chmod 755 x64/xray arm64/xray arm64/sing-box

echo "== Проверка"
arm64/xray version | head -1
arm64/sing-box version | head -1
for f in x64/xray arm64/xray arm64/sing-box; do
  # Для целей до 10.14 линкер пишет LC_VERSION_MIN_MACOSX (поле version), позже — LC_BUILD_VERSION (minos).
  printf '%s: ' "$f"; vtool -show-build "$f" | grep -E "minos|^ *version" | head -1 || true
done
