# Ядро для сборки macOS-клиента

Бинарники не хранятся в git. Клиент работает на **macOS 10.15+ (Intel)** и **11+ (Apple Silicon)** —
ниже не пускает .NET 8. Официальные сборки ядра требуют macOS 12+, поэтому часть ядра собирается
сама (на Mac):

```
bash build-legacy-cores.sh   # x64/xray, arm64/xray, arm64/sing-box — Go 1.26.7 + патчи SagerNet
sh fetch.sh                  # geoip/geosite и x64/sing-box — готовые, со сверкой SHA256SUMS
```

| Файл | Источник |
|---|---|
| `x64/xray`, `arm64/xray` | [Xray-core v26.3.27](https://github.com/XTLS/Xray-core/releases/tag/v26.3.27), собирается `build-legacy-cores.sh` (minos 10.13 / 11.0) |
| `arm64/sing-box` | [sing-box v1.14.0](https://github.com/SagerNet/sing-box/releases/tag/v1.14.0), собирается `build-legacy-cores.sh` с тегами legacy-сборок sing-box (minos 11.0) |
| `x64/sing-box` | там же → `sing-box-1.14.0-darwin-amd64-legacy-macos-10.13.tar.gz` (minos 10.13) |
| `*/geoip.dat`, `*/geosite.dat` | Xray-core v26.3.27 → `Xray-macos-arm64-v8a.zip` / `Xray-macos-64.zip` |
| `../tools/rcodesign.exe` | [apple-codesign 0.29.0](https://github.com/indygreg/apple-platform-rs/releases/tag/apple-codesign%2F0.29.0) → `apple-codesign-0.29.0-x86_64-pc-windows-msvc.zip` (SHA-256 `54bb500e2da7a8de02fcae0f331d1cac6e6d7173b4281042ff9c528ba3159aaa`) |

Патчи Go (как у legacy-сборок sing-box): возврат старых версий в заголовках Mach-O и прежнего
способа проверки цепочек сертификатов в `crypto/x509` — новый использует API macOS 12+.
`build-macos.py` останавливает сборку, если хоть один бинарник требует macOS новее допустимой.

Версии совпадают с Windows-клиентом.
