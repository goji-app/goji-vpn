# Полная пересборка Godji VPN: dotnet publish (self-contained win-x64) +
# упаковка в установщик через Inno Setup (ISCC.exe). Результат — единственный
# файл dist\GodjiVpn-Setup-<версия>.exe, промежуточные папки после сборки
# установщика удаляются.
#
# Требования: .NET 8 SDK, Inno Setup 6 (ставится через:
#   winget install --id JRSoftware.InnoSetup -e
# ISCC.exe ищется автоматически в стандартных путях установки).

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$proj = Join-Path $root "GodjiVpn\GodjiVpn.csproj"
$publishDir = Join-Path $root "publish_stage"
$issFile = Join-Path $root "installer\GodjiVpn.iss"

function Find-Iscc {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    $found = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($found) { return $found.Source }
    throw "ISCC.exe не найден. Установите Inno Setup: winget install --id JRSoftware.InnoSetup -e"
}

Write-Host "== Очистка старых артефактов сборки ==" -ForegroundColor Cyan
Remove-Item -Recurse -Force (Join-Path $root "GodjiVpn\bin"), (Join-Path $root "GodjiVpn\obj"), $publishDir -ErrorAction SilentlyContinue

Write-Host "== dotnet publish (Release, self-contained, win-x64) ==" -ForegroundColor Cyan
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishReadyToRun=false -p:PublishTrimmed=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой" }

Write-Host "== Удаление лишнего из publish (pdb/xml/локали) ==" -ForegroundColor Cyan
Remove-Item -Force (Join-Path $publishDir "GodjiVpn.pdb"), `
    (Join-Path $publishDir "Microsoft.Web.WebView2.Core.xml"), `
    (Join-Path $publishDir "Microsoft.Web.WebView2.WinForms.xml"), `
    (Join-Path $publishDir "Microsoft.Web.WebView2.Wpf.xml") -ErrorAction SilentlyContinue
Remove-Item -Force (Join-Path $publishDir "Runtime\README.txt") -ErrorAction SilentlyContinue
$satelliteLocales = "cs","de","es","fr","it","ja","ko","pl","pt-BR","ru","tr","zh-Hans","zh-Hant"
foreach ($loc in $satelliteLocales) {
    Remove-Item -Recurse -Force (Join-Path $publishDir $loc) -ErrorAction SilentlyContinue
}

Write-Host "== Компиляция установщика (Inno Setup) ==" -ForegroundColor Cyan
$iscc = Find-Iscc
# Версия — из GodjiVpn.csproj, чтобы установщик не отставал от приложения.
$version = ([xml](Get-Content $proj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Не нашёл <Version> в $proj" }
& $iscc "/DMyAppVersion=$version" $issFile
if ($LASTEXITCODE -ne 0) { throw "ISCC.exe завершился с ошибкой" }

Write-Host "== Уборка промежуточной publish-папки ==" -ForegroundColor Cyan
Remove-Item -Recurse -Force $publishDir, (Join-Path $root "GodjiVpn\bin"), (Join-Path $root "GodjiVpn\obj") -ErrorAction SilentlyContinue

Write-Host "Готово: dist\GodjiVpn-Setup-*.exe" -ForegroundColor Green
