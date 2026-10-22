#!/usr/bin/env bash
# Скачивает официальную сборку LibXray.xcframework (XTLS/libXray, вариант apple-cgo)
# и сверяет SHA256 с зафиксированным значением. Обновление ядра = смена двух строк ниже
# (версия + хэш из https://api.github.com/repos/XTLS/libXray/releases/tags/<версия>, поле digest).
set -euo pipefail

LIBXRAY_VERSION="v26.10.21"
LIBXRAY_SHA256="f318d216bb3b16b18d7b04c54789d1feaa6b7571433ed5582c1fe776e3776ef1"

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DEST="${ROOT}/Frameworks"
CACHE="${ROOT}/.downloads"
ZIP="${CACHE}/libxray-apple-cgo-${LIBXRAY_VERSION}.zip"
STAMP="${DEST}/.libxray-version"

if [[ -f "${STAMP}" && "$(cat "${STAMP}")" == "${LIBXRAY_VERSION}" && -d "${DEST}/LibXray.xcframework" ]]; then
  echo "LibXray ${LIBXRAY_VERSION} уже на месте"
  exit 0
fi

mkdir -p "${CACHE}" "${DEST}"
if [[ ! -f "${ZIP}" ]]; then
  echo "Скачиваю LibXray ${LIBXRAY_VERSION}…"
  curl -fL --retry 3 -o "${ZIP}.part" \
    "https://github.com/XTLS/libXray/releases/download/${LIBXRAY_VERSION}/libxray-apple-cgo.zip"
  mv "${ZIP}.part" "${ZIP}"
fi

ACTUAL="$(shasum -a 256 "${ZIP}" | awk '{print $1}')"
if [[ "${ACTUAL}" != "${LIBXRAY_SHA256}" ]]; then
  echo "ОШИБКА: SHA256 не совпадает (${ACTUAL} != ${LIBXRAY_SHA256})" >&2
  rm -f "${ZIP}"
  exit 1
fi

TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT
unzip -q "${ZIP}" -d "${TMP}"
rm -rf "${DEST}/LibXray.xcframework"
cp -R "${TMP}/libxray-apple-cgo/LibXray.xcframework" "${DEST}/"
echo "${LIBXRAY_VERSION}" > "${STAMP}"
echo "LibXray ${LIBXRAY_VERSION} → ${DEST}/LibXray.xcframework"
