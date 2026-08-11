# Ayn Thor Manager - Build & Run
# Uso: .\run.ps1 [desktop|web]
param([string]$Mode = "desktop")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "Ayn Thor Manager" -ForegroundColor Cyan
Write-Host ""

if ($Mode -eq "web") {
    Write-Host "Compilando API web..." -ForegroundColor Gray
    $out = "$root\publish"
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish "$root\src\AynThorManager.Api" -o $out --nologo -q
    if ($LASTEXITCODE -ne 0) { Write-Host "Erro na compilacao!" -ForegroundColor Red; exit 1 }
    Write-Host "Iniciando em http://localhost:5000" -ForegroundColor Green
    Set-Location $out
    & "$out\AynThorManager.Api.exe"
}
else {
    Write-Host "Compilando app desktop..." -ForegroundColor Gray
    $out = "$root\publish-desktop"
    Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish "$root\src\AynThorManager.Desktop" -o $out --nologo -q
    if ($LASTEXITCODE -ne 0) { Write-Host "Erro na compilacao!" -ForegroundColor Red; exit 1 }
    Write-Host "Iniciando..." -ForegroundColor Green
    & "$out\AynThorManager.Desktop.exe"
}
