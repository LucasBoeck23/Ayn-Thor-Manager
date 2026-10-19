#!/usr/bin/env bash
# roda com o mount já ativo passado como $1
MP="$1"
set -x
echo "conteudo" > "$MP/rn-a.txt"
ls -la "$MP/rn-a.txt"
mv "$MP/rn-a.txt" "$MP/rn-b.txt"
echo "mv exit=$?"
ls -la "$MP/rn-b.txt"
cat "$MP/rn-b.txt"
rm -f "$MP/rn-a.txt" "$MP/rn-b.txt"
