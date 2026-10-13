#!/usr/bin/env bash
# Полная подготовка рабочей копии на Mac: ядро + геоданные + генерация Xcode-проекта.
#   ./Scripts/bootstrap.sh && open GojiVPN.xcodeproj
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "${ROOT}"

"${ROOT}/Scripts/fetch-libxray.sh"
"${ROOT}/Scripts/fetch-geodata.sh"

if ! command -v xcodegen >/dev/null 2>&1; then
  echo "Нужен XcodeGen: brew install xcodegen" >&2
  exit 1
fi
xcodegen generate --spec project.yml
echo "Готово: откройте GojiVPN.xcodeproj"
