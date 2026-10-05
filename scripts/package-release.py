"""Build a reproducible, allowlisted release ZIP and SHA-256 manifest."""
import argparse
import hashlib
import json
import re
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def build(output, require_clean=False, quiet=False):
    suite = json.loads((ROOT / 'suite.json').read_text(encoding='utf-8'))
    version = suite['version']
    if not re.fullmatch(r'\d+\.\d+\.\d+', version):
        raise ValueError('suite.json must contain a stable three-part version')
    extension = ROOT / 'extensions/RevitThyme.extension/extension.json'
    if json.loads(extension.read_text(encoding='utf-8'))['version'] != version:
        raise ValueError('Extension and suite versions differ')
    sha = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip())
    if require_clean and dirty:
        raise ValueError('Release requires an exact clean checkout')
    spec = json.loads((ROOT / 'packaging/release-files.json').read_text(encoding='utf-8'))
    files = {name: name for name in spec['files']}
    files.update(spec['mapped_files'])
    payload = {}
    for source, target in files.items():
        path = (ROOT / source).resolve()
        if not path.is_relative_to(ROOT) or Path(target).is_absolute() or '..' in Path(target).parts:
            raise ValueError('Release path escapes package: ' + source)
        if path.suffix.lower() not in ('.py', '.ps1', '.json', '.yaml', '.md', '.png', '.svg', ''):
            raise ValueError('Unapproved release file type: ' + source)
        payload[target] = path.read_bytes()
    record = {
        'schema_version': 1, 'product': 'RevitThyme', 'version': version,
        'source_revision': sha, 'source_dirty': dirty,
        'timberfold_bundled': False, 'pyrevit_bundled': False,
        'files': {name: hashlib.sha256(data).hexdigest() for name, data in sorted(payload.items())},
    }
    payload['release.json'] = (json.dumps(record, indent=2) + '\n').encode('utf-8')
    output.mkdir(parents=True, exist_ok=True)
    archive = output / ('RevitThyme-' + version + '.zip')
    with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as stream:
        for name, data in sorted(payload.items()):
            entry = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = 0o100644 << 16
            stream.writestr(entry, data)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix('.zip.sha256').write_text(digest + '  ' + archive.name + '\n', encoding='utf-8')
    if not quiet:
        print(json.dumps({'archive': str(archive), 'sha256': digest,
                          'version': version, 'source_revision': sha, 'source_dirty': dirty}, indent=2))
    return archive


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'packages')
    parser.add_argument('--require-clean', action='store_true')
    args = parser.parse_args()
    build(args.output.resolve(), args.require_clean)
