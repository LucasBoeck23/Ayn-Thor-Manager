#!/usr/bin/env bash
# Conta linhas dos arquivos de código do src/.
#   bruto     = todas as linhas (igual wc -l)
#   so-codigo = sem linhas em branco e sem comentários de linha (// e ///)
#
# Uso:
#   ./scripts/count-lines.sh            # tabela de todos os arquivos + totais
#   ./scripts/count-lines.sh <dir>      # limita a um subdiretório de src/

set -euo pipefail

# Raiz do repositório (o diretório pai de scripts/).
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
target="${1:-$root/src}"

# Extensões de código consideradas.
mapfile -t files < <(
  find "$target" \( -name '*.cs' -o -name '*.axaml' \) \
    -not -path '*/bin/*' -not -path '*/obj/*' | sort
)

if [ "${#files[@]}" -eq 0 ]; then
  echo "Nenhum arquivo .cs/.axaml encontrado em: $target"
  exit 0
fi

code_lines() {
  # linhas sem branco e sem comentários de linha inteira (// ou ///)
  grep -vE '^[[:space:]]*//' "$1" | grep -cvE '^[[:space:]]*$' || true
}

printf '%-58s %7s %10s\n' "ARQUIVO" "BRUTO" "SO-CODIGO"
printf '%s\n' "---------------------------------------------------------------------------"

total_raw=0
total_code=0

# Ordena a exibição pelo tamanho bruto (maior primeiro).
for f in "${files[@]}"; do
  raw=$(wc -l < "$f")
  printf '%s\t%s\n' "$raw" "$f"
done | sort -rn | while IFS=$'\t' read -r raw f; do
  code=$(code_lines "$f")
  rel="${f#"$root"/}"
  printf '%-58s %7s %10s\n' "$rel" "$raw" "$code"
done

printf '%s\n' "---------------------------------------------------------------------------"

# Totais (recalculados fora do subshell do pipe acima).
for f in "${files[@]}"; do
  total_raw=$((total_raw + $(wc -l < "$f")))
  total_code=$((total_code + $(code_lines "$f")))
done
printf '%-58s %7s %10s\n' "TOTAL (${#files[@]} arquivos)" "$total_raw" "$total_code"
