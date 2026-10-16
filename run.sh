#!/usr/bin/env bash
# Ayn Thor Manager - Build & Run (Linux/macOS)
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "Ayn Thor Manager"
echo ""
echo "Compilando..."

out="$root/publish-desktop"
rm -rf "$out"
dotnet publish "$root/src/AynThorManager.Desktop" -o "$out" --nologo

bin="$out/AynThorManager.Desktop"
if [[ ! -f "$bin" ]]; then
  echo "Erro na compilacao!" >&2
  exit 1
fi

# Copia o APK companion, se existir
apk_src="$root/mobile/ayn-thor-link/release/ayn-thor-link.apk"
if [[ -f "$apk_src" ]]; then
  mkdir -p "$out/assets"
  cp -f "$apk_src" "$out/assets/ayn-thor-link.apk"
fi

echo "Iniciando..."
exec "$bin"
