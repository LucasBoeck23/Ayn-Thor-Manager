# Ayn Thor Manager - Build & Run
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "Ayn Thor Manager" -ForegroundColor Cyan
Write-Host ""
Write-Host "Compilando..." -ForegroundColor Gray

$out = "$root\publish-desktop"
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish "$root\src\AynThorManager.Desktop" -o $out --nologo 2>&1 | ForEach-Object {
    if ($_ -match "error" -and $_ -notmatch "Criando o diret") { Write-Host $_ -ForegroundColor Red }
}

if (!(Test-Path "$out\AynThorManager.Desktop.exe")) { Write-Host "Erro na compilacao!" -ForegroundColor Red; exit 1 }

# Copy companion APK
$apkSrc = "$root\mobile\ayn-thor-link\release\ayn-thor-link.apk"
if (Test-Path $apkSrc) {
    New-Item -ItemType Directory -Path "$out\assets" -Force | Out-Null
    Copy-Item $apkSrc "$out\assets\ayn-thor-link.apk" -Force
}

Write-Host "Iniciando..." -ForegroundColor Green
& "$out\AynThorManager.Desktop.exe"
