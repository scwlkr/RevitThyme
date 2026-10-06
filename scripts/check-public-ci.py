"""Portable CI checks; no Revit, external tools or user-specific skills required."""
import json
import os
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    checks = []
    commands = [
        [str(ROOT / 'project.cmd'), 'check-view-range'],
        ['git', 'diff', '--check'],
        ['cargo', 'fmt', '--manifest-path', 'tools/project-cli/Cargo.toml', '--check'],
        ['cargo', 'clippy', '--offline', '--locked', '--all-targets', '--manifest-path',
         'tools/project-cli/Cargo.toml', '--', '-D', 'warnings'],
        [str(ROOT / 'project.cmd'), 'check-release'],
        [str(ROOT / 'project.cmd'), 'package', '--require-clean'],
    ]
    for command in commands:
        try:
            result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True,
                                    encoding='utf-8', errors='replace', timeout=180,
                                    env={**os.environ, 'PYTHONUTF8': '1'})
        except (OSError, subprocess.TimeoutExpired) as error:
            result = subprocess.CompletedProcess(command, 1, '', str(error))
        checks.append({'command': command, 'passed': result.returncode == 0,
                       'exit': result.returncode, 'output': result.stdout + result.stderr})
        print(('PASS: ' if result.returncode == 0 else 'FAIL: ') + ' '.join(command))
        if result.returncode:
            print(result.stdout + result.stderr)
    report = {'sha': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'scope': 'portable_offline_release', 'passed': all(x['passed'] for x in checks),
              'checks': checks, 'live_revit_checked': False, 'physical_checked': False}
    path = ROOT / 'artifacts/public-ci/latest.json'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
