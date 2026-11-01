"""Reject personal profile paths and known stale public entry points (offline)."""
import argparse
import re
from pathlib import Path
from urllib.parse import unquote


def check(root):
    files = list(root.glob('*.md'))
    for name in ('docs', 'wiki', 'reports', 'outputs/PdfCorrectorium-Documentation'):
        base = root / name
        if base.exists():
            files.extend(p for p in base.rglob('*') if p.suffix.lower() in ('.md', '.html'))
    errors = []
    for path in sorted(set(files)):
        if path.name == 'pdf-correctorium-review-2026-11-01.md':
            continue  # User-supplied original is not a publication input.
        for number, line in enumerate(path.read_text(encoding='utf-8-sig').splitlines(), 1):
            decoded = unquote(line)
            if re.search(r'(?i)[a-z]:[/\\]+Users[/\\]+(?!<)[^/\\\s`]+', decoded):
                errors.append(f'{path.relative_to(root)}:{number}: personal Windows profile path')
            if path == root / 'README.md' and 'https://katsu2013.github.io/PDF-Correctorium/' in line:
                errors.append(f'README.md:{number}: unverified Pages entry point')
            if path.parent == root / 'wiki' and '/releases/latest' in line:
                errors.append(f'{path.relative_to(root)}:{number}: stable-only release endpoint')
    return files, errors


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args()
    files, errors = check(args.root.resolve())
    for error in errors:
        print(error)
    print(f'Public documentation: {len(files)} files; {len(errors)} errors.')
    raise SystemExit(bool(errors))
