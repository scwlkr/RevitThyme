"""Verify portable package checks and, on Windows, real installer outcomes."""
import argparse
import hashlib
import json
import os
import subprocess
import tempfile
from pathlib import Path

from package_checks import verify_package

ROOT = Path(__file__).resolve().parents[1]


def verify_installer(root, package, icons, require):
    extension = package / 'extensions/RevitThyme.extension'
    extensions = root / 'user extensions'
    command = ['powershell', '-NoProfile', '-NonInteractive', '-File',
               str(package / 'scripts/install.ps1'), '-PackageRoot', str(package),
               '-ExtensionsRoot', str(extensions)]
    redirected = root / 'AppData/Local/Packages/Example/LocalCache/Roaming/pyRevit/Extensions'
    result = subprocess.run([*command[:-1], str(redirected)], capture_output=True, text=True, timeout=60)
    require('Installer refuses package-cache destinations before changing host files', result.returncode != 0
            and 'Package-redirected AppData' in result.stderr and not redirected.exists(), result.stderr)
    result = subprocess.run(command, capture_output=True, text=True, timeout=60)
    require('Installer succeeds with spaces in paths', result.returncode == 0, result.stdout + result.stderr)
    if result.returncode != 0:
        raise RuntimeError(result.stdout + result.stderr)
    installed = extensions / 'RevitThyme.extension'
    record = json.loads((installed / 'install-record.json').read_text(encoding='utf-8-sig'))
    require('Installed files match recorded hashes', all(
        hashlib.sha256((installed / name).read_bytes()).hexdigest() == digest
        for name, digest in record['files'].items()))
    # Reproduce the initial v0.2.0 installation, which had no button artwork.
    for name in icons:
        (installed / name).unlink()
        del record['files'][name.as_posix()]
    (installed / 'install-record.json').write_text(json.dumps(record), encoding='utf-8')
    before = {name: (installed / name).read_bytes() for name in record['files']}
    result = subprocess.run([*command, '-Action', 'Icons'], capture_output=True, text=True, timeout=60)
    refreshed = json.loads((installed / 'install-record.json').read_text(encoding='utf-8-sig'))
    require('Icon refresh adds artwork without replacing extension code', result.returncode == 0 and all(
        (installed / name).read_bytes() == data for name, data in before.items()) and all(
        hashlib.sha256((installed / name).read_bytes()).hexdigest() == refreshed['files'].get(name.as_posix())
        for name in icons), result.stdout + result.stderr)
    before = {name: (installed / name).read_bytes() for name in refreshed['files']}
    # A self-consistent package with changed code must still refuse the icon-only path.
    startup = extension / 'startup.py'
    original = startup.read_bytes()
    startup.write_bytes(original + b'\n# incompatible icon-only code change\n')
    release_path = package / 'release.json'
    release_bytes = release_path.read_bytes()
    release = json.loads(release_bytes)
    release['files']['extensions/RevitThyme.extension/startup.py'] = hashlib.sha256(startup.read_bytes()).hexdigest()
    release_path.write_text(json.dumps(release), encoding='utf-8')
    result = subprocess.run([*command, '-Action', 'Icons'], capture_output=True, text=True, timeout=60)
    require('Icon refresh refuses code changes before touching installed files', result.returncode != 0 and
            'cannot change extension code' in result.stderr and all(
                (installed / name).read_bytes() == data for name, data in before.items()), result.stderr)
    startup.write_bytes(original)
    release_path.write_bytes(release_bytes)
    update = subprocess.run(command, capture_output=True, text=True, timeout=60)
    if update.returncode == 0:
        require('Reinstall preserves previous code backup', installed.exists() and any(
            (root / 'RevitThyme-backups').glob(record['version'] + '-*')))
    else:
        require('Running Revit blocks replacement', installed.exists() and 'Close Revit' in update.stderr,
                update.stderr)
    sentinel = installed / 'user-file.txt'
    sentinel.write_text('preserve me', encoding='utf-8')
    result = subprocess.run([*command, '-Action', 'Uninstall'], capture_output=True, text=True, timeout=60)
    require('Uninstall refuses unmanaged files', result.returncode != 0 and sentinel.read_text() == 'preserve me'
            and 'Unmanaged file' in result.stderr, result.stderr)
    sentinel.unlink()
    target = package / 'extensions/RevitThyme.extension/startup.py'
    target.write_text('tampered', encoding='utf-8')
    fresh = root / 'fresh extensions'
    result = subprocess.run([*command[:-1], str(fresh)], capture_output=True, text=True, timeout=60)
    require('Tampered package fails before installation', result.returncode != 0 and
            not (fresh / 'RevitThyme.extension').exists() and 'checksum mismatch' in result.stderr, result.stderr)
    # A running Revit protects installations. Closed-Revit lifecycle is a separate gate.
    result = subprocess.run([*command, '-Action', 'Uninstall'], capture_output=True, text=True, timeout=60)
    if result.returncode == 0:
        require('Uninstall retains recoverable backup', not installed.exists() and any(
            (root / 'RevitThyme-backups').glob('removed-*')))
    else:
        require('Running Revit blocks uninstall', installed.exists() and 'Close Revit' in result.stderr, result.stderr)


def verify(report, package_only=False, require_clean=False, source_root=ROOT):
    checks = []
    not_run = []

    def require(name, passed, detail=None):
        checks.append({'name': name, 'passed': bool(passed), 'detail': detail})
        print(('PASS: ' if passed else 'FAIL: ') + name, flush=True)

    installer_checked = False
    source_revision = None
    source_dirty = None
    try:
        with tempfile.TemporaryDirectory(prefix='RevitThyme release ') as folder:
            root = Path(folder)
            package, icons = verify_package(source_root, root, require, require_clean)
            release = json.loads((package / 'release.json').read_text(encoding='utf-8'))
            source_revision, source_dirty = release['source_revision'], release['source_dirty']
            if package_only or os.name != 'nt':
                reason = ('Package-only scope requested' if package_only else
                          'Windows installer lifecycle requires Windows')
                not_run.append({'name': 'Windows installer lifecycle',
                                'status': 'not_run', 'reason': reason})
                if not package_only:
                    require('Windows installer lifecycle is available', False, reason)
            else:
                verify_installer(root, package, icons, require)
                installer_checked = True
    except (OSError, ValueError, KeyError, RuntimeError, subprocess.SubprocessError) as error:
        require('Release verification completed', False, str(error))
    value = {'scope': ('portable_package' if package_only else 'offline_package_and_installer'),
             'passed': all(x['passed'] for x in checks), 'checks': checks, 'not_run': not_run,
             'source_root': str(source_root), 'windows_installer_checked': installer_checked,
             'source_revision': source_revision, 'source_dirty': source_dirty,
             'live_revit_checked': False, 'second_computer_checked': False}
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')
    print('Release checks: passed={0}; report={1}'.format(value['passed'], report))
    return 0 if value['passed'] else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, default=ROOT / 'artifacts/local-ci/release.json')
    parser.add_argument('--package-only', action='store_true',
                        help='Run portable payload checks; leave Windows lifecycle not_run')
    parser.add_argument('--require-clean', action='store_true')
    parser.add_argument('--root', type=Path, default=ROOT,
                        help='Inspect another checkout without altering it')
    args = parser.parse_args()
    raise SystemExit(verify(args.report.resolve(), args.package_only, args.require_clean,
                            args.root.resolve()))
