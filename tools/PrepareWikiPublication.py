"""Stage the canonical wiki/ for manual review; never commit, push or post."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import quote


def stage(repo, destination):
    if destination.exists():
        raise FileExistsError('Use a new wiki staging directory.')
    pages = sorted((repo / 'wiki').glob('*.md'))
    if not pages:
        raise ValueError('No canonical Wiki pages.')
    destination.mkdir(parents=True)
    names = {page.name for page in pages}
    manifest = []
    for page in pages:
        def link(match):
            label, target = match.groups()
            if re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:', target) or target.startswith('#'):
                return match[0]
            path, _, anchor = target.partition('#')
            if path in names:
                url = quote(Path(path).stem)
            else:
                relative = (page.parent / path).resolve().relative_to(repo)
                if not (repo / relative).exists():
                    raise FileNotFoundError(f'{page.name}: {target}')
                url = 'https://github.com/Katsu2013/PDF-Correctorium/blob/main/' + quote(relative.as_posix())
            return f'[{label}]({url}' + (('#' + anchor) if anchor else '') + ')'
        text = re.sub(r'\[([^\]]*)\]\(([^)]+)\)', link, page.read_text(encoding='utf-8-sig'))
        data = text.encode('utf-8')
        (destination / page.name).write_bytes(data)
        manifest.append({'page': page.name, 'sha256': hashlib.sha256(data).hexdigest()})
    (destination / 'wiki-publication-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', required=True, type=Path)
    args = parser.parse_args()
    result = stage(Path(__file__).resolve().parent.parent, args.output.resolve())
    print(f'Prepared {len(result)} Wiki pages. No remote changes were made.')
