"""Release checks shared by portable validation and Windows installer tests."""
import hashlib
import importlib.util
import json
import struct
import zipfile
from pathlib import Path, PurePosixPath


def public_payload(name):
    """Reject private data and ambiguous paths, including Windows case variants."""
    path = PurePosixPath(name.lower())
    if path.is_absolute() or '\\' in name or ':' in name or '..' in path.parts:
        return False
    forbidden = {'.git', '.aws', '.codex', '.agents', 'private', 'runs',
                 '.venv', 'venv', 'node_modules', 'artifacts', 'packages', 'logs'}
    return not (forbidden.intersection(path.parts) or
                path.suffix in {'.rvt', '.rfa', '.rte', '.rft', '.dwg', '.dxf'} or
                path.name == 'local.json' or path.name.endswith('.local.json') or
                path.name == '.env' or path.name.startswith('.env.'))


def rgba_icon(data):
    return (len(data) >= 33 and data[:8] == b'\x89PNG\r\n\x1a\n' and
            data[12:16] == b'IHDR' and
            struct.unpack('>IIBB', data[16:26]) == (64, 64, 8, 6))


def verify_package(source_root, work, require, require_clean=False):
    spec = importlib.util.spec_from_file_location(
        'release_builder', source_root / 'scripts/package-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    archive = builder.build(work / 'first', require_clean=require_clean, quiet=True)
    repeat = builder.build(work / 'second', require_clean=require_clean, quiet=True)
    require('Reproducible ZIP bytes', archive.read_bytes() == repeat.read_bytes())
    require('SHA256 sidecar', archive.with_suffix('.zip.sha256').read_text().split()[0] ==
            hashlib.sha256(archive.read_bytes()).hexdigest())
    with zipfile.ZipFile(archive) as stream:
        names = stream.namelist()
        record = json.loads(stream.read('release.json'))
        require('ZIP exactly matches release manifest',
                set(names) == set(record['files']) | {'release.json'} and
                len(names) == len(set(name.lower() for name in names)))
        require('Payload checksum read-back', all(
            hashlib.sha256(stream.read(name)).hexdigest() == digest
            for name, digest in record['files'].items()))
        public = all(public_payload(name) for name in names)
        require('Private models and local configuration excluded', public)
        if not public:
            raise ValueError('Unsafe payload is not extracted')
        package = work / 'extracted package'
        stream.extractall(package)
    prefix = 'extensions/RevitThyme.extension/'
    buttons = sorted({PurePosixPath(name[len(prefix):]).parent
                      for name in names if name.startswith(prefix) and
                      '.pushbutton/' in name and name.endswith('/script.py')})
    icons = [Path(button) / name for button in buttons
             for name in ('icon.png', 'icon.dark.png')]
    extension = package / 'extensions/RevitThyme.extension'
    require('Every ribbon button ships light and dark RGBA icons', bool(buttons) and all(
        (extension / name).is_file() and rgba_icon((extension / name).read_bytes())
        for name in icons))
    return package, icons
