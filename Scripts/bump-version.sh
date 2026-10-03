#!/usr/bin/env bash
# +0.0.1 к MARKETING_VERSION и +1 к CURRENT_PROJECT_VERSION (правило: одна правка = одна версия).
#   ./Scripts/bump-version.sh          → 1.0.0 (1) → 1.0.1 (2)
set -euo pipefail
FILE="$(cd "$(dirname "$0")/.." && pwd)/Config/Version.xcconfig"
ver=$(grep '^MARKETING_VERSION' "${FILE}" | awk -F'= ' '{print $2}')
build=$(grep '^CURRENT_PROJECT_VERSION' "${FILE}" | awk -F'= ' '{print $2}')
IFS=. read -r major minor patch <<< "${ver}"
newver="${major}.${minor}.$((patch + 1))"
newbuild=$((build + 1))
sed -i.bak -e "s/^MARKETING_VERSION = .*/MARKETING_VERSION = ${newver}/" \
           -e "s/^CURRENT_PROJECT_VERSION = .*/CURRENT_PROJECT_VERSION = ${newbuild}/" "${FILE}"
rm -f "${FILE}.bak"
echo "${ver} (${build}) → ${newver} (${newbuild})"
