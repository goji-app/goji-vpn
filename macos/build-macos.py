"""Сборка macOS-клиента Goji VPN.

Для каждой архитектуры (arm64 — Apple Silicon, x64 — Intel):
  dotnet publish (self-contained) -> Goji VPN.app -> ad-hoc подпись -> zip для автообновления.

На Mac (основной путь, GitHub Actions — .github/workflows/macos.yml): подпись штатным codesign,
zip через ditto и один универсальный установщик GojiVPN-<v>-macOS.dmg (Intel и Apple Silicon,
см. build_universal) с окном "перетащите в Программы".
На Windows (запасной путь): подпись rcodesign (tools/rcodesign.exe) и только zip.

Раскладка бандла по правилам Apple: в Contents/MacOS только нативный запускатель (apphost),
всё остальное — .NET-сборки, dylib, Runtime/xray, sing-box, geo*.dat — в Contents/Resources/app.
В apphost вшит относительный путь ../Resources/app/GojiVpn.dll (corehost поддерживает подпути).
Иначе .dll в Contents/MacOS не запечатываются подписью, и Gatekeeper называет приложение
"повреждённым" без кнопки "Всё равно открыть".

Результат: dist/GojiVPN-<версия>-macOS-<arch>.zip (имя ждёт UpdateService) и .dmg;
на Windows ещё копия в «Приложения/macOS/Сборки» рядом с репозиторием.
Запуск: python build-macos.py [arm64|x64 ...]
"""
import io
import os
import re
import shutil
import struct
import subprocess
import sys
import time
import zipfile

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJ = os.path.join(ROOT, "GojiVpn.Mac", "GojiVpn.Mac.csproj")
RCODESIGN = os.path.join(ROOT, "tools", "rcodesign.exe")
ICON_SRC = os.path.join(ROOT, "packaging", "icon-512.png")
FONTS = os.path.join(ROOT, "GojiVpn.Mac", "Assets", "Fonts")
OUT = os.path.join(ROOT, "out")
DIST = os.path.join(ROOT, "dist")
ARCHIVE = os.path.join(ROOT, "..", "..", "Приложения", "macOS", "Сборки")
APP_NAME = "Goji VPN"
EXE_NAME = "GojiVpn"
BUNDLE_ID = "xyz.gojihub.vpn.mac"
MACHO_MAGIC = {b"\xcf\xfa\xed\xfe", b"\xce\xfa\xed\xfe", b"\xca\xfe\xba\xbe", b"\xfe\xed\xfa\xcf"}
ON_MAC = sys.platform == "darwin"
# Нижняя граница macOS: её задаёт .NET 8 (10.15 на Intel, 11.0 на Apple Silicon — раньше
# Apple Silicon не бывает). Ядро собрано под неё же (Runtime/build-legacy-cores.sh).
MIN_OS = {"x64": "10.15", "arm64": "11.0"}
CPU_ARCH = {0x01000007: "x64", 0x0100000C: "arm64"}

# Окно установщика (в точках): значок приложения слева, "Программы" справа.
DMG_W, DMG_H = 640, 420
DMG_APP_POS, DMG_LINK_POS = (170, 205), (470, 205)


def version():
    with open(PROJ, encoding="utf-8") as f:
        return re.search(r"<Version>([^<]+)</Version>", f.read()).group(1)


def run(cmd, check=True):
    print(">", " ".join(cmd), flush=True)
    return subprocess.run(cmd, check=check)


def app_icon_image():
    """Иконка в стиле macOS: скруглённый квадрат 824/1024 с тенью, внутри — иконка приложения."""
    src = Image.open(ICON_SRC).convert("RGBA")
    canvas = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    box, off, radius = 824, 100, 185
    shadow = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((off, off + 12, off + box, off + box + 12), radius, fill=(0, 0, 0, 90))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(14)))
    mask = Image.new("L", (box, box), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, box - 1, box - 1), radius, fill=255)
    tile = src.resize((box, box), Image.LANCZOS)
    tile.putalpha(mask)
    canvas.alpha_composite(tile, (off, off))
    return canvas


def make_icns(path):
    # PNG-вложения icns: icp4 16, icp5 32, ic07 128, ic08 256, ic09 512, ic10 1024,
    # ic11 32 (16@2x), ic12 64 (32@2x), ic13 256 (128@2x), ic14 512 (256@2x).
    canvas = app_icon_image()
    chunks = b""
    for tag, size in [("icp4", 16), ("icp5", 32), ("ic11", 32), ("ic12", 64), ("ic07", 128),
                      ("ic08", 256), ("ic13", 256), ("ic09", 512), ("ic14", 512), ("ic10", 1024)]:
        buf = io.BytesIO()
        canvas.resize((size, size), Image.LANCZOS).save(buf, "PNG")
        data = buf.getvalue()
        chunks += tag.encode("ascii") + struct.pack(">I", len(data) + 8) + data
    with open(path, "wb") as f:
        f.write(b"icns" + struct.pack(">I", len(chunks) + 8) + chunks)


def dmg_background(path, scale):
    """Фон окна установщика: мягкий градиент в цветах "Стекла", стрелка и подсказки."""
    w, h = DMG_W * scale, DMG_H * scale
    top, bottom = (232, 246, 242), (243, 238, 250)
    img = Image.new("RGB", (w, h))
    px = ImageDraw.Draw(img)
    for y in range(h):
        t = y / (h - 1)
        px.line([(0, y), (w, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    d = ImageDraw.Draw(img)
    bold = ImageFont.truetype(os.path.join(FONTS, "manrope_bold.ttf"), 22 * scale)
    reg = ImageFont.truetype(os.path.join(FONTS, "manrope_medium.ttf"), 12 * scale)
    ink, muted, teal = (20, 32, 30), (92, 104, 102), (18, 150, 128)

    def centered(text, y, font, fill):
        tw = d.textlength(text, font=font)
        d.text(((w - tw) / 2, y), text, font=font, fill=fill)

    centered("Перетащите Goji VPN в «Программы»", 38 * scale, bold, ink)
    # Стрелка между значками.
    y = DMG_APP_POS[1] * scale
    x0, x1 = (DMG_APP_POS[0] + 78) * scale, (DMG_LINK_POS[0] - 78) * scale
    d.line([(x0, y), (x1 - 10 * scale, y)], fill=teal, width=5 * scale)
    d.polygon([(x1, y), (x1 - 18 * scale, y - 11 * scale), (x1 - 18 * scale, y + 11 * scale)], fill=teal)
    centered("Первый запуск: если macOS не открывает приложение, зайдите в", 296 * scale, reg, muted)
    centered("Системные настройки → Конфиденциальность и безопасность → «Всё равно открыть».", 314 * scale, reg, muted)
    centered("При подключении VPN macOS спросит пароль администратора.", 338 * scale, reg, muted)
    img.save(path)


def info_plist(ver, min_os, by_arch=None):
    arch_min = ""
    if by_arch:
        items = "".join(f"<key>{k}</key><string>{v}</string>" for k, v in by_arch.items())
        arch_min = f"\n    <key>LSMinimumSystemVersionByArchitecture</key><dict>{items}</dict>"
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>{APP_NAME}</string>
    <key>CFBundleDisplayName</key><string>{APP_NAME}</string>
    <key>CFBundleIdentifier</key><string>{BUNDLE_ID}</string>
    <key>CFBundleExecutable</key><string>{EXE_NAME}</string>
    <key>CFBundleIconFile</key><string>goji.icns</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleVersion</key><string>{ver}</string>
    <key>CFBundleShortVersionString</key><string>{ver}</string>
    <key>LSMinimumSystemVersion</key><string>{min_os}</string>{arch_min}
    <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSRequiresAquaSystemAppearance</key><false/>
    <key>NSHumanReadableCopyright</key><string>© Goji VPN</string>
</dict>
</plist>
"""


def patch_apphost(src, dst, subdir="app"):
    """Переносит apphost в Contents/MacOS, заменив имя сборки на путь до Resources/<subdir>."""
    with open(src, "rb") as f:
        data = bytearray(f.read())
    old = f"{EXE_NAME}.dll".encode() + bytes(1)
    new = f"../Resources/{subdir}/{EXE_NAME}.dll".encode()
    at = data.find(old)
    if at < 0 or data.find(old, at + 1) >= 0 or any(data[at + len(old):at + len(new) + 1]):
        sys.exit("Не нашёл однозначное место для пути сборки в apphost")
    data[at:at + len(new)] = new
    data[at + len(new)] = 0
    with open(dst, "wb") as f:
        f.write(data)
    os.chmod(dst, 0o755)
    os.remove(src)


def macho_min_os(path):
    """[(arch, minos)] по LC_BUILD_VERSION / LC_VERSION_MIN_MACOSX всех срезов Mach-O."""
    with open(path, "rb") as f:
        data = f.read()
    offsets = [0]
    if data[:4] == b"\xca\xfe\xba\xbe":  # fat (universal)
        n = struct.unpack_from(">I", data, 4)[0]
        offsets = [struct.unpack_from(">I", data, 8 + i * 20 + 8)[0] for i in range(n)]
    result = []
    for off in offsets:
        if struct.unpack_from("<I", data, off)[0] != 0xFEEDFACF:
            continue
        cpu, _, _, ncmds = struct.unpack_from("<iiII", data, off + 4)
        p = off + 32
        for _ in range(ncmds):
            cmd, size = struct.unpack_from("<II", data, p)
            if cmd in (0x32, 0x24):
                v = struct.unpack_from("<I", data, p + (12 if cmd == 0x32 else 8))[0]
                result.append((CPU_ARCH.get(cpu, hex(cpu)), (v >> 16, (v >> 8) & 0xFF)))
            p += size
    return result


# Свежий официальный xray (Go 1.27) требует macOS 13 — приложение запускает его только там,
# а на macOS 10.15–12 берёт Runtime/xray-legacy (см. VpnEngine.XrayExe).
MODERN_ONLY = {os.path.join("Runtime", "xray"): (13, 0)}


def check_min_os(app, arch):
    """Сборка не должна требовать macOS новее MIN_OS[arch] — иначе на старых системах не запустится."""
    limit = tuple(int(x) for x in MIN_OS[arch].split("."))
    bad = []
    for dirpath, _, filenames in os.walk(app):
        for name in filenames:
            full = os.path.join(dirpath, name)
            if is_macho(full):
                rel = os.path.relpath(full, app)
                allowed = next((v for k, v in MODERN_ONLY.items() if rel.endswith(k)), limit)
                for a, v in macho_min_os(full):
                    if a == arch and v > allowed:
                        bad.append(f"{os.path.relpath(full, app)}: {v[0]}.{v[1]}")
    if bad:
        sys.exit(f"Требуют macOS новее {MIN_OS[arch]}:\n  " + "\n  ".join(bad))
    print(f"OK: всё в сборке {arch} работает с macOS {MIN_OS[arch]}+", flush=True)


def is_macho(path):
    with open(path, "rb") as f:
        return f.read(4) in MACHO_MAGIC


def sign_on_mac(app):
    """Ad-hoc подпись штатным codesign: сначала каждый Mach-O внутри Resources/app
    (--deep туда не заходит, а Apple Silicon не грузит неподписанный код), затем бандл."""
    for dirpath, _, filenames in os.walk(os.path.join(app, "Contents", "Resources")):
        for name in filenames:
            full = os.path.join(dirpath, name)
            if is_macho(full):
                os.chmod(full, 0o755)
                run(["codesign", "--force", "--sign", "-", "--timestamp=none", full])
    run(["codesign", "--force", "--sign", "-", "--timestamp=none", app])
    run(["codesign", "--verify", "--deep", "--strict", "--verbose=2", app])


def zip_app_windows(app_dir, zip_path):
    """zip с unix-правами: Windows их не хранит, а без 0755 macOS не запустит бинарники."""
    base = os.path.dirname(app_dir)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for dirpath, dirnames, filenames in os.walk(app_dir):
            dirnames.sort()
            rel_dir = os.path.relpath(dirpath, base).replace(os.sep, "/")
            zi = zipfile.ZipInfo(rel_dir + "/")
            zi.external_attr = (0o40755 << 16) | 0x10
            zi.create_system = 3
            z.writestr(zi, b"")
            for name in sorted(filenames):
                full = os.path.join(dirpath, name)
                zi = zipfile.ZipInfo.from_file(full, rel_dir + "/" + name)
                zi.create_system = 3
                zi.external_attr = ((0o100755 if is_macho(full) else 0o100644) << 16)
                zi.compress_type = zipfile.ZIP_DEFLATED
                with open(full, "rb") as f:
                    z.writestr(zi, f.read(), compresslevel=9)


FINDER_LAYOUT = """
tell application "Finder"
  tell disk "{vol}"
    open
    set current view of container window to icon view
    set toolbar visible of container window to false
    set statusbar visible of container window to false
    set the bounds of container window to {{200, 120, {r}, {b}}}
    set opts to the icon view options of container window
    set arrangement of opts to not arranged
    set icon size of opts to 112
    set text size of opts to 13
    set background picture of opts to file ".background:background.tiff"
    set position of item "{app}.app" of container window to {{{ax}, {ay}}}
    set position of item "Applications" of container window to {{{lx}, {ly}}}
    close
    open
    update without registering applications
    delay 2
    close
  end tell
end tell
"""


def make_dmg(app, dmg_path, ver):
    """Установщик: окно с фоном, значком приложения и ярлыком "Программы"."""
    work = os.path.join(os.path.dirname(app), "dmg")
    shutil.rmtree(work, ignore_errors=True)
    stage = os.path.join(work, "stage")
    os.makedirs(os.path.join(stage, ".background"))
    run(["ditto", app, os.path.join(stage, os.path.basename(app))])
    os.symlink("/Applications", os.path.join(stage, "Applications"))
    bg1, bg2 = os.path.join(work, "bg.png"), os.path.join(work, "bg@2x.png")
    dmg_background(bg1, 1)
    dmg_background(bg2, 2)
    run(["tiffutil", "-cathidpicheck", bg1, bg2, "-out", os.path.join(stage, ".background", "background.tiff")])
    shutil.copy2(os.path.join(app, "Contents", "Resources", "goji.icns"), os.path.join(stage, ".VolumeIcon.icns"))

    vol = f"{APP_NAME} {ver}"
    rw = os.path.join(work, "rw.dmg")
    run(["hdiutil", "create", "-srcfolder", stage, "-volname", vol, "-fs", "HFS+", "-format", "UDRW", "-ov", rw])
    mount = f"/Volumes/{vol}"
    run(["hdiutil", "attach", "-readwrite", "-noverify", "-noautoopen", "-mountpoint", mount, rw])
    try:
        run(["SetFile", "-a", "C", mount], check=False)  # свой значок тома
        script = FINDER_LAYOUT.format(vol=vol, app=APP_NAME, r=200 + DMG_W, b=120 + DMG_H,
                                      ax=DMG_APP_POS[0], ay=DMG_APP_POS[1], lx=DMG_LINK_POS[0], ly=DMG_LINK_POS[1])
        if run(["osascript", "-e", script], check=False).returncode != 0:
            print("! Finder не оформил окно — образ будет без раскладки", flush=True)
        run(["sync"])
    finally:
        # Finder отпускает том не сразу: без ожидания convert падает с "Resource temporarily unavailable".
        for attempt in range(10):
            if run(["hdiutil", "detach", mount] + (["-force"] if attempt >= 5 else []), check=False).returncode == 0:
                break
            time.sleep(3)
    for attempt in range(5):
        if os.path.exists(dmg_path):
            os.remove(dmg_path)
        if run(["hdiutil", "convert", rw, "-format", "UDZO", "-imagekey", "zlib-level=9", "-o", dmg_path],
               check=False).returncode == 0:
            return
        time.sleep(5)
    sys.exit("hdiutil convert не удался")


def build(arch, ver):
    rid = f"osx-{arch}"
    pub = os.path.join(OUT, rid, "publish")
    shutil.rmtree(os.path.join(OUT, rid), ignore_errors=True)
    run(["dotnet", "publish", PROJ, "-c", "Release", "-r", rid, "--self-contained", "true",
         "-p:PublishSingleFile=false", "-p:PublishTrimmed=false", "-p:DebugType=None", "-o", pub])

    app = os.path.join(OUT, rid, f"{APP_NAME}.app")
    macos = os.path.join(app, "Contents", "MacOS")
    res = os.path.join(app, "Contents", "Resources")
    app_dir = os.path.join(res, "app")
    shutil.copytree(pub, app_dir)
    os.makedirs(macos)
    shutil.copy2(os.path.join(app_dir, EXE_NAME), os.path.join(OUT, rid, "apphost"))
    patch_apphost(os.path.join(app_dir, EXE_NAME), os.path.join(macos, EXE_NAME))
    runtime_src = os.path.join(ROOT, "Runtime", arch)
    runtime_dst = os.path.join(app_dir, "Runtime")
    os.makedirs(runtime_dst)
    for name in ("xray", "xray-legacy", "sing-box", "geoip.dat", "geosite.dat"):
        src = os.path.join(runtime_src, name)
        if not os.path.exists(src):
            sys.exit(f"Нет {src} — см. Runtime/README.md")
        shutil.copy2(src, runtime_dst)
    make_icns(os.path.join(res, "goji.icns"))
    with open(os.path.join(app, "Contents", "Info.plist"), "w", encoding="utf-8", newline="\n") as f:
        f.write(info_plist(ver, MIN_OS[arch]))

    check_min_os(app, arch)

    os.makedirs(DIST, exist_ok=True)
    zip_path = os.path.join(DIST, f"GojiVPN-{ver}-macOS-{arch}.zip")
    if os.path.exists(zip_path):
        os.remove(zip_path)
    # Ad-hoc подпись: без неё Apple Silicon не запускает Mach-O вовсе (Developer ID нет).
    if ON_MAC:
        sign_on_mac(app)
        # zip под архитектуру — для автообновления (UpdateService ищет -macOS-<arch>.zip).
        run(["ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", app, zip_path])
    else:
        run([RCODESIGN, "sign", app])
        zip_app_windows(app, zip_path)
        os.makedirs(ARCHIVE, exist_ok=True)
        shutil.copy2(zip_path, ARCHIVE)
    for f in sorted(os.listdir(DIST)):
        if f"-macOS-{arch}." in f:
            print(f"OK {f} ({os.path.getsize(os.path.join(DIST, f)):,} байт)", flush=True)


def build_universal(ver):
    """Один Goji VPN.app для любого Mac: "толстый" apphost (срезы arm64 и x64), каждый срез
    указывает на свою копию .NET и ядра — Resources/app-arm64 или Resources/app-x64.
    macOS сама запускает нужный срез, поэтому установщик не перепутать с чужой архитектурой
    ("программа не поддерживается этим компьютером Mac")."""
    root = os.path.join(OUT, "universal")
    shutil.rmtree(root, ignore_errors=True)
    app = os.path.join(root, f"{APP_NAME}.app")
    macos = os.path.join(app, "Contents", "MacOS")
    res = os.path.join(app, "Contents", "Resources")
    os.makedirs(macos)
    slices = []
    for arch in ("arm64", "x64"):
        rid = f"osx-{arch}"
        shutil.copytree(os.path.join(OUT, rid, f"{APP_NAME}.app", "Contents", "Resources", "app"),
                        os.path.join(res, f"app-{arch}"))
        tmp = os.path.join(root, f"apphost-{arch}")
        shutil.copy2(os.path.join(OUT, rid, "apphost"), tmp)
        patch_apphost(tmp, tmp + ".patched", subdir=f"app-{arch}")
        slices.append(tmp + ".patched")
    run(["lipo", "-create"] + slices + ["-output", os.path.join(macos, EXE_NAME)])
    run(["lipo", "-info", os.path.join(macos, EXE_NAME)])
    make_icns(os.path.join(res, "goji.icns"))
    with open(os.path.join(app, "Contents", "Info.plist"), "w", encoding="utf-8", newline="\n") as f:
        f.write(info_plist(ver, MIN_OS["x64"], {"arm64": MIN_OS["arm64"], "x86_64": MIN_OS["x64"]}))
    check_min_os(app, "arm64")
    check_min_os(app, "x64")
    sign_on_mac(app)
    dmg = os.path.join(DIST, f"GojiVPN-{ver}-macOS.dmg")
    make_dmg(app, dmg, ver)
    print(f"OK {os.path.basename(dmg)} ({os.path.getsize(dmg):,} байт)", flush=True)


if __name__ == "__main__":
    v = version()
    archs = sys.argv[1:] or ["arm64", "x64"]
    for a in archs:
        build(a, v)
    if ON_MAC and set(archs) >= {"arm64", "x64"}:
        build_universal(v)
