# Ayn Thor Manager - Start script
# Compila e roda o projeto (contorna bloqueio do Smart App Control)
$ErrorActionPreference = "Stop"

Write-Host "Compilando Ayn Thor Manager..." -ForegroundColor Cyan
dotnet publish src/AynThorManager.Api -o "$PSScriptRoot\publish" --nologo -q

if ($LASTEXITCODE -ne 0) {
    Write-Host "Erro na compilacao!" -ForegroundColor Red
    exit 1
}

Write-Host "Iniciando em http://localhost:5000" -ForegroundColor Green
Write-Host "Pressione Ctrl+C para parar" -ForegroundColor Gray
Write-Host ""

Set-Location "$PSScriptRoot\publish"
& ".\AynThorManager.Api.exe"
