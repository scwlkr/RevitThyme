"""Run local portable checks; retain exact target/checker revisions and host gaps."""
import argparse
import json
import os
import platform
import re
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

CHECKER_ROOT = Path(__file__).resolve().parents[1]


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=CHECKER_ROOT,
                        help='Validate another clean checkout using this checker')
    parser.add_argument('--base', default='HEAD^')
    parser.add_argument('--require-clean', action='store_true')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args(argv)
    root = args.root.resolve()
    report_path = (args.report or root / 'artifacts/local-ci/portable.json').resolve()
    env = {**os.environ, 'PYTHONUTF8': '1', 'RUSTUP_AUTO_INSTALL': '0'}
    checks = []

    def run(name, command, expected=0, cwd=root):
        command = [str(value) for value in command]
        started = time.monotonic()
        try:
            result = subprocess.run(command, cwd=cwd, env=env, capture_output=True,
                                    text=True, encoding='utf-8', errors='replace', timeout=180)
        except (OSError, subprocess.TimeoutExpired) as error:
            result = subprocess.CompletedProcess(command, 1, '', str(error))
        checks.append({'name': name, 'command': command, 'cwd': str(cwd),
                       'expected_exit': expected, 'exit': result.returncode,
                       'passed': result.returncode == expected,
                       'seconds': round(time.monotonic() - started, 3),
                       'output': (result.stdout + result.stderr).strip()})
        print(('PASS: ' if checks[-1]['passed'] else 'FAIL: ') + name, flush=True)
        if not checks[-1]['passed']:
            print(checks[-1]['output'], flush=True)
        return result

    def require(name, passed, detail=None):
        checks.append({'name': name, 'passed': bool(passed), 'detail': detail})
        print(('PASS: ' if passed else 'FAIL: ') + name, flush=True)

    sha = run('Resolve checked SHA', ['git', 'rev-parse', '--verify', 'HEAD^{commit}']).stdout.strip()
    base = run('Resolve base SHA', ['git', 'rev-parse', '--verify', args.base + '^{commit}']).stdout.strip()
    top = run('Resolve checkout root', ['git', 'rev-parse', '--show-toplevel']).stdout.strip()
    require('Requested root is a checkout root', bool(top) and Path(top).resolve() == root, top)
    status = run('Inspect target worktree', ['git', 'status', '--porcelain']).stdout.strip()
    checker_sha = run('Resolve checker SHA', ['git', 'rev-parse', '--verify', 'HEAD^{commit}'],
                      cwd=CHECKER_ROOT).stdout.strip()
    checker_status = run('Inspect checker worktree', ['git', 'status', '--porcelain'],
                         cwd=CHECKER_ROOT).stdout.strip()
    if args.require_clean:
        require('Exact clean target revision', not status, status or 'Clean tracked and untracked files')
        require('Exact clean checker revision', not checker_status,
                checker_status or 'Clean tracked and untracked files')
    changed = run('Changed paths', ['git', 'diff', '--name-only', base, sha]).stdout.splitlines()
    run('Commit whitespace', ['git', 'diff', base, sha, '--check'])
    run('Working diff whitespace', ['git', 'diff', '--check'])
    versions = {}
    for name, command in (('python', [sys.executable, '--version']),
                          ('cargo', ['cargo', '--version']), ('rustc', ['rustc', '--version']),
                          ('rustfmt', ['cargo', 'fmt', '--version']),
                          ('clippy', ['cargo', 'clippy', '--version'])):
        versions[name] = run('Identify ' + name, command).stdout.strip()
    pin = root / 'rust-toolchain.toml'
    try:
        match = re.search(r'^channel\s*=\s*"([^"]+)"', pin.read_text(encoding='utf-8'), re.MULTILINE)
    except OSError:
        match = None
    pinned = match.group(1) if match else None
    require('Declared Rust pin is readable', pinned is not None, str(pin))
    matches_pin = bool(pinned and versions['rustc'].split()[1:2] == [pinned])
    not_run = [
        {'name': 'Windows installer lifecycle', 'status': 'not_run',
         'reason': 'Separate Windows check-release gate; this path checks package bytes only'},
        {'name': 'Windows foundation and CLI contract', 'status': 'not_run',
         'reason': 'PowerShell and external TimberFold are required by full local CI'},
        {'name': 'IronPython 2.7 syntax and runtime', 'status': 'not_run',
         'reason': 'CPython parsing and fixtures do not qualify the Revit adapter runtime'},
        {'name': 'Live Revit and second computer', 'status': 'not_run',
         'reason': 'No Revit host or project models are used by portable checks'},
    ]
    if not matches_pin:
        not_run.append({'name': 'Pinned Rust toolchain', 'status': 'not_run',
                        'reason': 'Compiler does not match declared pin: ' + str(pinned)})
    manifest = root / 'tools/project-cli/Cargo.toml'
    run('Rust formatting', ['cargo', 'fmt', '--manifest-path', manifest, '--check'])
    run('Rust lint', ['cargo', 'clippy', '--offline', '--locked', '--all-targets',
                     '--manifest-path', manifest, '--', '-D', 'warnings'])
    run('Rust unit tests', ['cargo', 'test', '--offline', '--locked', '--manifest-path', manifest])
    run('Rust build', ['cargo', 'build', '--offline', '--locked', '--manifest-path', manifest])
    launcher = root / ('project.cmd' if os.name == 'nt' else 'project')
    run('CLI help', [launcher, '--help'])
    run('CLI rejects unknown command', [launcher, 'unknown-command'], expected=2)
    run('CLI rejects invalid doctor arguments', [launcher, 'doctor', 'unexpected'], expected=2)
    run('Python unit and contract tests', [sys.executable, '-X', 'utf8', '-m', 'unittest',
                                        'discover', '-s', 'tests', '-p', 'test*.py', '-v'])
    syntax = (
        "import pathlib, subprocess; "
        "names=subprocess.check_output(['git','ls-files'],text=True).splitlines(); "
        "sources=[name for name in names if name.endswith('.py')]; "
        "[compile(pathlib.Path(name).read_bytes(),name,'exec') for name in sources]; "
        "print('CPython syntax:',len(sources),'tracked files')"
    )
    run('CPython source syntax', [sys.executable, '-X', 'utf8', '-c', syntax])
    package_report = report_path.parent / (report_path.stem + '-package.json')
    command = [sys.executable, '-X', 'utf8', CHECKER_ROOT / 'scripts/check-release.py',
               '--root', root, '--package-only', '--report', package_report]
    if args.require_clean:
        command.append('--require-clean')
    run('Portable release package', command)
    try:
        package = json.loads(package_report.read_text(encoding='utf-8'))
    except (OSError, ValueError) as error:
        package = {'passed': False, 'error': str(error)}
    require('Package evidence confirms portable scope', package.get('passed') is True and
            package.get('scope') == 'portable_package' and
            package.get('windows_installer_checked') is False and
            package.get('source_root') == str(root) and
            package.get('source_revision') == sha and
            package.get('source_dirty') == bool(status), package)
    final_sha = run('Final target SHA', ['git', 'rev-parse', 'HEAD']).stdout.strip()
    final_status = run('Final target worktree', ['git', 'status', '--porcelain']).stdout.strip()
    final_checker_sha = run('Final checker SHA', ['git', 'rev-parse', 'HEAD'],
                            cwd=CHECKER_ROOT).stdout.strip()
    final_checker_status = run('Final checker worktree', ['git', 'status', '--porcelain'],
                               cwd=CHECKER_ROOT).stdout.strip()
    require('Checks preserve target revision and worktree', final_sha == sha and final_status == status)
    require('Checks preserve checker revision and worktree',
            final_checker_sha == checker_sha and final_checker_status == checker_status)
    report = {
        'schema_version': 1, 'checked_at_utc': datetime.now(timezone.utc).isoformat(),
        'scope': 'portable_offline', 'sha': sha, 'base_sha': base,
        'source_root': str(root), 'checker_sha': checker_sha, 'checker_root': str(CHECKER_ROOT),
        'checker_worktree_clean': not checker_status, 'worktree_clean': not status,
        'changed_paths': changed, 'platform': {'system': platform.system(),
                                              'release': platform.release(),
                                              'machine': platform.machine()},
        'toolchain': {**versions, 'pinned_rust': pinned, 'matches_pin': matches_pin},
        'passed': all(check['passed'] for check in checks), 'checks': checks, 'not_run': not_run,
        'windows_installer_checked': False, 'ironpython_checked': False,
        'live_revit_checked': False, 'second_computer_checked': False, 'physical_checked': False,
    }
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('Portable CI: passed={0}; SHA={1}; report={2}'.format(report['passed'], sha, report_path))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
