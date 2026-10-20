#!/usr/bin/env python3
"""Remove comments from C# (.cs) and XAML (.axaml) files under a directory.

C#:   strips // line comments, /* ... */ block comments and /// doc comments,
      while leaving // or /* that appear inside string/char/verbatim literals.
XAML: strips <!-- ... --> comments.

Blank lines left behind by a removed full-line comment are dropped; blank lines
that were already in the source are preserved.
"""
import sys
import re
from pathlib import Path


def strip_csharp(text: str) -> str:
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""

        # Verbatim / interpolated-verbatim string: @"..." or $@"..." or @$"..."
        if (c == '@' and nxt == '"') or text[i:i + 3] in ('$@"', '@$"'):
            # find the start quote
            j = text.index('"', i)
            out.append(text[i:j + 1])
            i = j + 1
            while i < n:
                if text[i] == '"' and text[i + 1:i + 2] == '"':  # escaped "" 
                    out.append('""')
                    i += 2
                    continue
                out.append(text[i])
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue

        # Regular string "..."
        if c == '"':
            out.append(c)
            i += 1
            while i < n:
                out.append(text[i])
                if text[i] == '\\':
                    if i + 1 < n:
                        out.append(text[i + 1])
                        i += 2
                        continue
                elif text[i] == '"':
                    i += 1
                    break
                i += 1
            continue

        # Char literal '.'
        if c == "'":
            out.append(c)
            i += 1
            while i < n:
                out.append(text[i])
                if text[i] == '\\' and i + 1 < n:
                    out.append(text[i + 1])
                    i += 2
                    continue
                elif text[i] == "'":
                    i += 1
                    break
                i += 1
            continue

        # Line comment // or ///
        if c == '/' and nxt == '/':
            while i < n and text[i] != '\n':
                i += 1
            continue

        # Block comment /* ... */
        if c == '/' and nxt == '*':
            i += 2
            while i < n and text[i:i + 2] != '*/':
                i += 1
            i += 2
            continue

        out.append(c)
        i += 1

    result = ''.join(out)
    # Drop lines that became empty/whitespace solely because a comment was removed,
    # and trim trailing whitespace left where an end-of-line comment was cut off.
    lines = result.split('\n')
    cleaned = []
    for ln in lines:
        stripped_tail = ln.rstrip()
        if stripped_tail == '':
            if ln != '':
                # whitespace-only line left by a comment -> drop
                continue
            cleaned.append('')
            continue
        cleaned.append(stripped_tail)
    # collapse 3+ blank lines into max 1
    out_lines = []
    blank = 0
    for ln in cleaned:
        if ln.strip() == '':
            blank += 1
            if blank > 1:
                continue
        else:
            blank = 0
        out_lines.append(ln)
    return '\n'.join(out_lines)


def strip_xaml(text: str) -> str:
    text = re.sub(r'<!--.*?-->', '', text, flags=re.DOTALL)
    lines = text.split('\n')
    out_lines = []
    blank = 0
    for ln in lines:
        if ln.strip() == '':
            blank += 1
            if blank > 1:
                continue
        else:
            blank = 0
        out_lines.append(ln)
    return '\n'.join(out_lines)


def main():
    root = Path(sys.argv[1])
    changed = 0
    for path in root.rglob('*'):
        if any(part in ('bin', 'obj') for part in path.parts):
            continue
        if path.suffix == '.cs':
            new = strip_csharp(path.read_text())
        elif path.suffix == '.axaml':
            new = strip_xaml(path.read_text())
        else:
            continue
        if not new.endswith('\n'):
            new += '\n'
        if new != path.read_text():
            path.write_text(new)
            changed += 1
            print(f"stripped: {path}")
    print(f"\n{changed} arquivo(s) alterado(s).")


if __name__ == '__main__':
    main()
