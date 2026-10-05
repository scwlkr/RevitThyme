"""Verify ZIP reproducibility, payload boundaries and real installer outcomes."""
import argparse
import hashlib
import importlib.util
import json
import subprocess
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def verify(report):
    spec = importlib.util.spec_from_file_location('release_builder', ROOT / 'scripts/package-release.py')
    builder = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(builder)
    checks = []

    def require(name, passed, detail=None):
        checks.append({'name': name, 'passed': bool(passed), 'detail': detail})

    with tempfile.TemporaryDirectory(prefix='RevitThyme release ') as folder:
        root = Path(folder)
        archive = builder.build(root / 'first', quiet=True)
        repeat = builder.build(root / 'second', quiet=True)
        require('Reproducible ZIP bytes', archive.read_bytes() == repeat.read_bytes())
        require('SHA256 sidecar', archive.with_suffix('.zip.sha256').read_text().split()[0] ==
                hashlib.sha256(archive.read_bytes()).hexdigest())
        with zipfile.ZipFile(archive) as stream:
            record = json.loads(stream.read('release.json'))
            require('ZIP exactly matches release manifest', set(stream.namelist()) == set(record['files']) | {'release.json'})
            require('Payload checksum read-back', all(hashlib.sha256(stream.read(name)).hexdigest() == digest
                                                     for name, digest in record['files'].items()))
            require('Private models and local configuration excluded', not any(
                name.endswith(('.rvt', '.rfa', '.dwg', '.dxf', '.env', 'local.json')) or
                any(part in ('runs', '.venv', '.git', 'private') for part in Path(name).parts)
                for name in stream.namelist()))
            package = root / 'extracted package'
            stream.extractall(package)
        extensions = root / 'user extensions'
        command = ['powershell', '-NoProfile', '-NonInteractive', '-File',
                   str(package / 'scripts/install.ps1'), '-PackageRoot', str(package),
                   '-ExtensionsRoot', str(extensions)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=20)
        require('Installer succeeds with spaces in paths', result.returncode == 0, result.stdout + result.stderr)
        if result.returncode != 0:
            raise RuntimeError(result.stdout + result.stderr)
        installed = extensions / 'RevitThyme.extension'
        record = json.loads((installed / 'install-record.json').read_text(encoding='utf-8-sig'))
        require('Installed files match recorded hashes', all(
            hashlib.sha256((installed / name).read_bytes()).hexdigest() == digest
            for name, digest in record['files'].items()))
        sentinel = installed / 'user-file.txt'
        sentinel.write_text('preserve me', encoding='utf-8')
        result = subprocess.run([*command, '-Action', 'Uninstall'], capture_output=True, text=True, timeout=20)
        require('Uninstall refuses unmanaged files', result.returncode != 0 and sentinel.read_text() == 'preserve me'
                and 'Unmanaged file' in result.stderr, result.stderr)
        sentinel.unlink()
        target = package / 'extensions/RevitThyme.extension/startup.py'
        target.write_text('tampered', encoding='utf-8')
        fresh = root / 'fresh extensions'
        result = subprocess.run([*command[:-1], str(fresh)], capture_output=True, text=True, timeout=20)
        require('Tampered package fails before installation', result.returncode != 0 and
                not (fresh / 'RevitThyme.extension').exists() and 'checksum mismatch' in result.stderr, result.stderr)
        # A running Revit protects installations. Closed-Revit lifecycle is a separate gate.
        result = subprocess.run([*command, '-Action', 'Uninstall'], capture_output=True, text=True, timeout=20)
        if result.returncode == 0:
            require('Uninstall retains recoverable backup', not installed.exists() and any(
                (root / 'RevitThyme-backups').glob('removed-*')))
        else:
            require('Running Revit blocks uninstall', installed.exists() and 'Close Revit' in result.stderr, result.stderr)
    value = {'scope': 'offline_package_and_installer', 'passed': all(x['passed'] for x in checks),
             'checks': checks, 'live_revit_checked': False, 'second_computer_checked': False}
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    print('Release checks: passed={0}; report={1}'.format(value['passed'], report))
    return 0 if value['passed'] else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, default=ROOT / 'artifacts/local-ci/release.json')
    args = parser.parse_args()
    raise SystemExit(verify(args.report.resolve()))
