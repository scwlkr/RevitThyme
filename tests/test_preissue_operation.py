"""Exercise the actual shared dispatch and Routes binding without a live host."""
import importlib.util
import json
import os
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
LIB = ROOT / 'extensions/RevitThyme.extension/lib'
sys.path.insert(0, str(LIB))
from revitthyme import config
import revitthyme


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, LIB / 'revitthyme' / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def model_snapshot():
    return {'schema_version': 1, 'scope': 'fixture_no_links',
            **{name: {'status': 'complete', 'items': []}
               for name in ('rooms', 'doors', 'views', 'sheets')}}


def uiapp(document=True, family=False):
    doc = types.SimpleNamespace(CreationGUID='fixture-guid', GetHashCode=lambda: 123,
                                IsFamilyDocument=family, IsModified=False)
    uidoc = types.SimpleNamespace(Document=doc)
    return types.SimpleNamespace(ActiveUIDocument=uidoc if document else None)


class Api:
    def __init__(self, name):
        self.name, self.handlers = name, {}

    def route(self, path, methods):
        def bind(handler):
            self.handlers[path] = (methods, handler)
            return handler
        return bind


class OperationBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.snapshot = types.ModuleType('revitthyme.preissue_snapshot')
        self.snapshot.collect = Mock(return_value=model_snapshot())
        self.patches = [patch.dict(sys.modules, {'revitthyme.preissue_snapshot': self.snapshot}),
                        patch.object(revitthyme, 'preissue_snapshot', self.snapshot, create=True),
                        patch.object(config, 'preissue_standards', return_value=None),
                        patch.object(config, 'read_json', return_value={'version': '0.2.0'})]
        for context in self.patches:
            context.start()
        self.operation = load('fixture_preissue_operation', 'preissue_operation.py')
        pyrevit = types.ModuleType('pyrevit')
        pyrevit.DB = types.SimpleNamespace(ViewType=object())
        pyrevit.versionmgr = object()
        pyrevit.routes = types.SimpleNamespace(API=Api)
        loader = types.ModuleType('pyrevit.loader')
        loader.sessioninfo = types.SimpleNamespace(get_session_uuid=lambda: 'fixture-session')
        diagnostics = types.ModuleType('System.Diagnostics')
        diagnostics.Process = types.SimpleNamespace(GetCurrentProcess=lambda: types.SimpleNamespace(Id=42))
        userconfig = types.ModuleType('pyrevit.userconfig')
        userconfig.user_config = types.SimpleNamespace(routes_host='127.0.0.1')
        system = types.ModuleType('System')
        system.Enum = types.SimpleNamespace(GetNames=Mock(return_value=['FloorPlan', 'Section', 'ThreeD']))
        self.host_modules = {'pyrevit': pyrevit, 'pyrevit.loader': loader,
                             'System.Diagnostics': diagnostics, 'pyrevit.userconfig': userconfig,
                             'System': system,
                             'revitthyme.preissue_operation': self.operation}
        with patch.dict(sys.modules, self.host_modules):
            self.dispatch = load('fixture_operations', 'operations.py')

    def tearDown(self):
        for context in reversed(self.patches):
            context.stop()

    def test_shared_dispatch_requires_correct_target_and_rejects_unknown_fields(self):
        app = uiapp()
        target = self.dispatch.identity(app)
        self.assertEqual('target_required', self.dispatch.execute('preissue_check', app)['status'])
        self.assertEqual('target_mismatch', self.dispatch.execute('preissue_check', app, {'target': {}})['status'])
        self.assertEqual('invalid_request', self.dispatch.execute('preissue_check', app,
                         {'target': target, 'path': '/tmp/anything'})['status'])
        self.assertEqual('invalid_request', self.dispatch.execute('preissue_check', app, [])['status'])
        self.snapshot.collect.assert_not_called()
        result = self.dispatch.execute('preissue_check', app, {'target': target})
        self.assertEqual('checked', result['status'])
        self.assertEqual([], result['changed_ids'])
        self.assertEqual(['read_model'], result['effects'])
        self.assertEqual('incomplete', result['data']['summary']['conclusion'])
        self.assertTrue(result['data']['readback']['modified_flag_unchanged'])

    def test_no_document_family_and_invalid_standards_do_not_collect(self):
        app = uiapp(document=False)
        result = self.dispatch.execute('preissue_check', app, {'target': self.dispatch.identity(app)})
        self.assertEqual('no_document', result['status'])
        app = uiapp(family=True)
        result = self.dispatch.execute('preissue_check', app, {'target': self.dispatch.identity(app)})
        self.assertEqual('unsupported_document', result['status'])
        app = uiapp()
        result = self.dispatch.execute('preissue_check', app,
                         {'target': self.dispatch.identity(app), 'standards': {'schema_version': 9}})
        self.assertEqual('invalid_standards', result['status'])
        self.snapshot.collect.assert_not_called()

    def test_explicit_standards_override_user_file_and_readback_is_narrow(self):
        app = uiapp()
        def changed(doc):
            doc.IsModified = True
            return model_snapshot()
        self.snapshot.collect.side_effect = changed
        result = self.dispatch.execute('preissue_check', app,
                         {'target': self.dispatch.identity(app), 'standards': None})
        config.preissue_standards.assert_not_called()
        self.assertEqual('request', result['data']['standards_source'])
        self.assertEqual('document_changed_during_check', result['status'])
        self.assertEqual('document_modified_flag_only', result['data']['readback']['scope'])
        self.assertFalse(result['data']['readback']['modified_flag_unchanged'])

    def test_native_view_type_validation_rejects_typos_before_collection(self):
        app = uiapp()
        target = self.dispatch.identity(app)
        standards = {'schema_version': 1, 'views': {'types': ['FloorPlna'], 'name_patterns': ['A-*']}}
        with patch.dict(sys.modules, self.host_modules):
            result = self.dispatch.execute('preissue_check', app, {'target': target, 'standards': standards})
            self.assertEqual('invalid_standards', result['status'])
            self.assertIn('FloorPlna', result['data']['diagnostics'][0])
            self.snapshot.collect.assert_not_called()
            standards['views']['types'] = ['FloorPlan']
            result = self.dispatch.execute('preissue_check', app, {'target': target, 'standards': standards})
        self.assertEqual('checked', result['status'])
        self.snapshot.collect.assert_called_once_with(app.ActiveUIDocument.Document)
        check = next(x for x in result['data']['checks'] if x['check_id'] == 'view.name')
        self.assertEqual('checked', check['status'])
        self.assertEqual(['empty_checked_scope'], check['diagnostics'])
        self.host_modules['System'].Enum.GetNames.assert_called_with(self.host_modules['pyrevit'].DB.ViewType)

    def test_unavailable_native_enum_returns_readiness_diagnostic_before_collection(self):
        app = uiapp()
        standards = {'schema_version': 1, 'views': {'types': ['FloorPlan'], 'require_template': True}}
        get_names = self.host_modules['System'].Enum.GetNames
        for unavailable in (RuntimeError('Fixture runtime boundary failure'), []):
            with self.subTest(unavailable=unavailable), patch.dict(sys.modules, self.host_modules):
                get_names.side_effect = unavailable if isinstance(unavailable, Exception) else None
                get_names.return_value = unavailable
                result = self.dispatch.execute('preissue_check', app,
                                 {'target': self.dispatch.identity(app), 'standards': standards})
                self.assertEqual('host_validation_unavailable', result['status'])
                self.assertIn('not checked', result['data']['diagnostics'][0])
                self.snapshot.collect.assert_not_called()

    def test_route_is_named_post_and_forwards_to_same_operation(self):
        modules = dict(self.host_modules)
        modules['revitthyme.operations'] = self.dispatch
        with patch.dict(sys.modules, modules):
            http = load('fixture_http', 'http.py')
            api = http.register()
        methods, handler = api.handlers['/preissue/check/']
        self.assertEqual(['POST'], methods)
        self.assertIn('uiapp', handler.__code__.co_varnames[:handler.__code__.co_argcount])
        app = uiapp()
        result = handler(types.SimpleNamespace(data={'target': self.dispatch.identity(app)}), app)
        self.assertEqual('preissue_check', result['operation'])
        self.assertEqual('checked', result['status'])
        modules['pyrevit.userconfig'].user_config.routes_host = '0.0.0.0'
        with patch.dict(sys.modules, modules), self.assertRaises(ValueError):
            http.register()


class ConfigurationAndPackageTests(unittest.TestCase):
    def test_fixed_user_standards_file_and_timberfold_settings_are_independent(self):
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, {'LOCALAPPDATA': folder}):
            self.assertIsNone(config.preissue_standards())
            root = Path(folder) / 'RevitThyme'
            root.mkdir()
            value = {'schema_version': 1, 'sheets': {'number_patterns': ['A-*']}}
            path = root / 'preissue-standards.json'
            path.write_text(json.dumps(value))
            self.assertEqual(value, config.preissue_standards())
            with patch.object(config, 'missing_entries', return_value=[]):
                config.configure(folder)
            self.assertEqual(value, config.preissue_standards())
            path.write_text('invalid JSON')
            with self.assertRaises(ValueError):
                config.preissue_standards()

    def test_release_allowlist_contains_entire_check_and_existing_rgba_artwork(self):
        paths = json.loads((ROOT / 'packaging/release-files.json').read_text())['files']
        expected = ['docs/PRE-ISSUE-CHECK.md', 'config/preissue-standards.example.json']
        expected += ['extensions/RevitThyme.extension/lib/revitthyme/' + name for name in
                     ('preissue.py', 'preissue_operation.py', 'preissue_snapshot.py', 'preissue_standards.py')]
        button = 'extensions/RevitThyme.extension/RevitThyme.tab/Suite.panel/PreIssue.pushbutton/'
        expected += [button + name for name in ('script.py', 'bundle.yaml', 'icon.png', 'icon.dark.png')]
        self.assertTrue(set(expected).issubset(paths))
        for name in ('icon.png', 'icon.dark.png'):
            self.assertEqual((ROOT / button / name).read_bytes(),
                             (ROOT / button.replace('PreIssue.pushbutton', 'Status.pushbutton') / name).read_bytes())
        self.assertFalse(any(path.endswith('preissue-standards.json') for path in paths))


if __name__ == '__main__':
    unittest.main()
