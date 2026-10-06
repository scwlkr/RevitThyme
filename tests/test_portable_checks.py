"""Portable release and real launcher regressions; standard-library dependencies."""
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from package_checks import public_payload, rgba_icon, verify_package

SPEC = importlib.util.spec_from_file_location('release_check', ROOT / 'scripts/check-release.py')
RELEASE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RELEASE)


class PortableReleaseTests(unittest.TestCase):
    def test_public_boundary_rejects_windows_case_and_private_config(self):
        for name in ('models/House.RVT', 'Families/Type.RFA', 'private/settings.json',
                     'Config/office.LOCAL.JSON', 'CONFIG/LOCAL.JSON', 'a/.env.production',
                     '.AWS/config', 'a/../settings.json', 'a\\settings.json', 'C:/a.json'):
            with self.subTest(name=name):
                self.assertFalse(public_payload(name))
        self.assertTrue(public_payload('extensions/RevitThyme.extension/suite.json'))

    def test_package_evidence_does_not_pass_windows_gate(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / 'portable.json'
            self.assertEqual(RELEASE.verify(report, package_only=True), 0)
            result = json.loads(report.read_text())
            self.assertTrue(result['passed'])
            self.assertEqual(result['scope'], 'portable_package')
            self.assertFalse(result['windows_installer_checked'])
            self.assertEqual(result['not_run'][0]['status'], 'not_run')
            self.assertFalse(result['live_revit_checked'])

    @unittest.skipIf(os.name == 'nt', 'Windows installer exercised by full release checks')
    def test_full_release_fails_with_truthful_report_off_windows(self):
        with tempfile.TemporaryDirectory() as folder:
            report = Path(folder) / 'release.json'
            self.assertEqual(RELEASE.verify(report), 1)
            result = json.loads(report.read_text())
            self.assertFalse(result['passed'])
            self.assertFalse(result['windows_installer_checked'])
            self.assertTrue(any(not check['passed'] for check in result['checks']))

    def test_icons_check_reads_every_packaged_button(self):
        checks = []
        with tempfile.TemporaryDirectory() as folder:
            package, icons = verify_package(ROOT, Path(folder),
                                            lambda name, passed, detail=None: checks.append(passed))
            self.assertTrue(all(checks))
            buttons = [path.parent for path in package.rglob('script.py')
                       if path.parent.name.endswith('.pushbutton')]
            self.assertEqual(len(icons), 2 * len(buttons))
            self.assertTrue(all(rgba_icon((package / 'extensions/RevitThyme.extension' / icon).read_bytes())
                                for icon in icons))
            self.assertFalse(rgba_icon(b'not an icon'))

    def test_new_packaged_button_without_icons_fails(self):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'source'
            files = {'suite.json': json.dumps({'version': '0.2.0'}),
                     'extensions/RevitThyme.extension/extension.json': json.dumps({'version': '0.2.0'}),
                     'extensions/RevitThyme.extension/RevitThyme.tab/QA.panel/New.pushbutton/script.py': ''}
            files['scripts/package-release.py'] = (ROOT / 'scripts/package-release.py').read_text()
            files['packaging/release-files.json'] = json.dumps({
                'files': [name for name in files if not name.startswith('scripts/')], 'mapped_files': {}})
            for name, data in files.items():
                path = source / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(data, encoding='utf-8')
            checks = {}
            with mock.patch('subprocess.check_output', side_effect=lambda command, **kwargs:
                            '0' * 40 if command[1] == 'rev-parse' else ''):
                verify_package(source, Path(folder) / 'output',
                               lambda name, passed, detail=None: checks.update({name: passed}))
            self.assertFalse(checks['Every ribbon button ships light and dark RGBA icons'])


class PortableLauncherTests(unittest.TestCase):
    def test_python_route_runs_and_forwards_underlying_help(self):
        launcher = ROOT / ('project.cmd' if os.name == 'nt' else 'project')
        result = subprocess.run([str(launcher), 'check-release', '--', '--help'],
                                cwd=ROOT, capture_output=True, text=True, timeout=60,
                                env={**os.environ, 'RUSTUP_AUTO_INSTALL': '0'})
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('--package-only', result.stdout)
        self.assertIn('--root', result.stdout)


if __name__ == '__main__':
    unittest.main()
