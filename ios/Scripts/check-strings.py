#!/usr/bin/env python3
"""Проверка локализации: каждый ключ L("...") из кода есть во всех Localizable.strings,
число и типы подстановок (%@, %ld, %lld) совпадают между языками. Запускается в CI."""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LANGS = ["ru", "en"]

# Ключи, которые собираются в коде динамически: L("theme.\(rawValue)") и т.п.
DYNAMIC = {
    "theme": ["system", "light", "dark"],
    "ping.method": ["proxyGet", "proxyHead", "tcp", "proxyGet.desc", "proxyHead.desc", "tcp.desc"],
    "servers.sort": ["favorites", "ping", "name"],
    "autoconnect": ["off", "untrustedWifi", "always", "off.desc", "untrustedWifi.desc", "always.desc"],
    "log.level": ["none", "error", "warning", "info", "debug"],
    "log.cat": ["main", "core", "subscription", "service"],
}
PLURALS = ["servers.count", "days", "days.word"]
EXTRA = [
    "tab.home", "tab.servers", "tab.subscription", "tab.settings",
    "privacy.item.email", "privacy.item.device", "privacy.item.support",
    "privacy.item.no_logs", "privacy.item.no_tracking", "privacy.item.no_sale",
    "intent.connect.title", "intent.connect.desc", "intent.connect.short",
    "intent.disconnect.title", "intent.disconnect.desc", "intent.disconnect.short",
]

def parse_strings(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    pairs = re.findall(r'^"((?:[^"\\]|\\.)+)"\s*=\s*"((?:[^"\\]|\\.)*)";', text, re.M)
    return dict(pairs)

def placeholders(value: str) -> list:
    return re.findall(r"%(?:\d+\$)?(?:@|ld|lld|d|02ld)", value)

def main() -> int:
    code = ""
    for p in list((ROOT / "App").rglob("*.swift")) + list((ROOT / "Shared").rglob("*.swift")):
        code += p.read_text(encoding="utf-8")
    keys = set(re.findall(r'\bL\("([a-zA-Z0-9_.]+)"', code))
    keys |= set(EXTRA)
    for base, suffixes in DYNAMIC.items():
        keys |= {f"{base}.{s}" for s in suffixes}
    for base in PLURALS:
        keys |= {f"{base}.{s}" for s in ("one", "few", "many")}

    tables = {lang: parse_strings(ROOT / "App/Resources" / f"{lang}.lproj/Localizable.strings") for lang in LANGS}
    errors = []
    for lang, table in tables.items():
        for key in sorted(keys - table.keys()):
            errors.append(f"[{lang}] нет ключа: {key}")
    base = tables["ru"]
    for lang in LANGS[1:]:
        for key, value in tables[lang].items():
            if key in base and placeholders(base[key]) != placeholders(value):
                errors.append(f"[{lang}] подстановки не совпадают с ru: {key}")
    if errors:
        print("\n".join(errors))
        return 1
    print(f"OK: {len(keys)} ключей, языки: {', '.join(LANGS)}")
    return 0

if __name__ == "__main__":
    sys.exit(main())
