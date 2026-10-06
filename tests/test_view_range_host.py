"""Owner-boundary fixtures: stale/cancelled work never edits; failed work restores range."""
import copy
import importlib.util
import sys
import types
import unittest
from pathlib import Path
from revitthyme.view_range_state import edited_planes, validate_planes, plane_elevation

ROOT = Path(__file__).resolve().parents[1]


class Id:
    def __init__(self, value):
        self.Value = int(value)


class Range:
    def __init__(self, values):
        self.values = copy.deepcopy(values)

    def GetLevelId(self, key):
        return Id(self.values[key][0])

    def GetOffset(self, key):
        return self.values[key][1]

    def SetLevelId(self, key, value):
        self.values[key][0] = value.Value

    def SetOffset(self, key, value):
        self.values[key][1] = value

    def Dispose(self):
        pass


class Level:
    def __init__(self, value, name, elevation):
        self.Id, self.Name, self.ProjectElevation = Id(value), name, elevation


class View:
    Name, ViewType, IsTemplate, ViewTemplateId = 'Ground plan', 'FloorPlan', False, Id(-1)
    Id, GenLevel = Id(42), Level(1, 'Ground', 10.0)

    def __init__(self, doc):
        self.doc = doc

    def GetPrimaryViewId(self):
        return Id(-1)

    def GetDependentViewIds(self):
        return []

    def get_Parameter(self, key):
        return types.SimpleNamespace(IsReadOnly=False)

    def GetViewRange(self):
        return Range(self.doc.values)

    def SetViewRange(self, candidate):
        self.doc.events.append('set_range')
        self.doc.values = copy.deepcopy(candidate.values)

    def CheckPlanViewRangeValidity(self, candidate):
        return self.doc.api_errors


class Collector:
    def __init__(self, doc):
        self.doc = doc

    def OfClass(self, cls):
        return self.doc.levels


class Scope:
    def __init__(self, doc, name):
        self.doc, self.state = doc, 'Uninitialized'

    def Start(self):
        self.before = copy.deepcopy(self.doc.values)
        self.state = 'Started'
        self.doc.events.append('start')
        return self.state

    def GetStatus(self):
        return self.state

    def RollBack(self):
        self.doc.values = copy.deepcopy(self.before)
        self.state = 'RolledBack'
        self.doc.events.append('rollback')
        return self.state

    def Dispose(self):
        pass


class Transaction(Scope):
    def GetFailureHandlingOptions(self):
        return self

    def SetFailuresPreprocessor(self, preprocessor):
        self.preprocessor = preprocessor

    def SetClearAfterRollback(self, value):
        pass

    def SetForcedModalHandling(self, value):
        pass

    def SetFailureHandlingOptions(self, value):
        pass

    def Commit(self):
        self.state = self.doc.commit_status
        self.doc.pending = self.state == 'Pending'
        if self.state == 'RolledBack':
            self.doc.values = copy.deepcopy(self.before)
        if self.doc.corrupt_readback:
            self.doc.values['cut'][1] += 1
        return self.state


class Group(Scope):
    def RollBack(self):
        if self.doc.pending:
            raise RuntimeError('An inner transaction is pending failure handling')
        return super().RollBack()

    def Assimilate(self):
        self.state = 'Committed'
        self.doc.events.append('assimilate')
        return self.state


DB = types.SimpleNamespace(
    ElementId=Id, Level=Level, ViewPlan=View, FilteredElementCollector=Collector,
    PlanViewPlane=types.SimpleNamespace(TopClipPlane='top', CutPlane='cut',
                                      BottomClipPlane='bottom', ViewDepthPlane='depth'),
    PlanViewRange=types.SimpleNamespace(Current=Id(-2), LevelAbove=Id(-3),
                                       LevelBelow=Id(-4), Unlimited=Id(-5)),
    BuiltInParameter=types.SimpleNamespace(PLAN_VIEW_RANGE=-100), IFailuresPreprocessor=object,
    FailureProcessingResult=types.SimpleNamespace(ProceedWithRollBack='Rollback', Continue='Continue'),
    TransactionStatus=types.SimpleNamespace(Started='Started', Committed='Committed', RolledBack='RolledBack'),
    Transaction=Transaction, TransactionGroup=Group)
Id.InvalidElementId = Id(-1)
dependencies = {'pyrevit': types.SimpleNamespace(DB=DB), 'System': types.SimpleNamespace(Int64=int),
                'revitthyme.operations': types.SimpleNamespace(identity=lambda uiapp: uiapp.target)}
saved = {key: sys.modules.get(key) for key in dependencies}
sys.modules.update(dependencies)
spec = importlib.util.spec_from_file_location('host_under_test', ROOT /
        'extensions/RevitThyme.extension/lib/revitthyme/view_range_host.py')
host = importlib.util.module_from_spec(spec)
spec.loader.exec_module(host)
for key, value in saved.items():
    if value is None:
        sys.modules.pop(key, None)
    else:
        sys.modules[key] = value


class Document:
    IsFamilyDocument, IsReadOnly, IsModifiable = False, False, False

    def __init__(self):
        self.values = {'top': [1, 10.0], 'cut': [1, 4.0], 'bottom': [1, 0.0], 'depth': [1, -1.0]}
        self.levels = [Level(2, 'Upper', 20.0), Level(1, 'Ground', 10.0), Level(3, 'Lower', 0.0)]
        self.events, self.api_errors, self.pending = [], [], False
        self.commit_status, self.corrupt_readback, self.regen_failure = 'Committed', False, False
        self.view = View(self)

    def GetElement(self, value):
        return self.view

    def GetUnits(self):
        raise ValueError('Use explicit mm fallback in fixture')

    def Regenerate(self):
        if self.regen_failure:
            raise RuntimeError('Regeneration failed')


class RangeTests(unittest.TestCase):
    def setUp(self):
        self.doc = Document()
        self.app = types.SimpleNamespace(ActiveUIDocument=types.SimpleNamespace(
            Document=self.doc, ActiveView=self.doc.view), target={'session': 'a', 'document': 'b'},
            Application=types.SimpleNamespace(VersionNumber='2027'))
        self.original = host.snapshot(self.app)
        self.edits = copy.deepcopy(self.original['planes'])
        self.edits['cut']['offset'] = 5.0
        self.before = copy.deepcopy(self.doc.values)

    def test_applies_one_view_with_independent_readback_and_one_undo_group(self):
        result = host.apply(self.app, self.original, self.edits)
        self.assertEqual((result['status'], result['changed_ids']), ('applied', [42]))
        self.assertEqual(self.doc.values['cut'], [1, 5.0])
        self.assertEqual(result['planes']['cut']['elevation'], 15.0)
        self.assertEqual(self.doc.events, ['start', 'start', 'set_range', 'assimilate'])

    def test_stale_range_and_switched_target_do_not_start_transactions(self):
        for change in ('range', 'target'):
            with self.subTest(change=change):
                self.setUp()
                if change == 'range':
                    self.doc.values['cut'][1] = 6.0
                else:
                    self.app.target = {'session': 'other'}
                self.assertEqual(host.apply(self.app, self.original, self.edits)['status'], 'failed')
                self.assertEqual(self.doc.events, [])

    def test_invalid_order_and_api_rejection_never_start_transactions(self):
        self.edits['cut']['offset'] = 15.0
        self.assertEqual(host.apply(self.app, self.original, self.edits)['status'], 'failed')
        self.assertEqual(self.doc.events, [])
        self.edits['cut']['offset'] = 5.0
        self.doc.api_errors = ['Native range restriction']
        self.assertEqual(host.apply(self.app, self.original, self.edits)['status'], 'failed')
        self.assertEqual(self.doc.events, [])

    def test_failed_regeneration_commit_or_postcommit_readback_rolls_back_everything(self):
        for flag, value in [('regen_failure', True), ('commit_status', 'RolledBack'),
                            ('corrupt_readback', True)]:
            with self.subTest(stage=flag):
                self.setUp()
                setattr(self.doc, flag, value)
                result = host.apply(self.app, self.original, self.edits)
                self.assertEqual((result['status'], result['rollback'], result['changed_ids']),
                                 ('failed', 'confirmed', []))
                self.assertEqual(self.doc.values, self.before)

    def test_pending_is_never_reported_as_committed(self):
        self.doc.commit_status = 'Pending'
        result = host.apply(self.app, self.original, self.edits)
        self.assertEqual(result['status'], 'failed')
        self.assertEqual(result['changed_ids'], [])
        self.assertEqual(result['rollback'], 'unconfirmed')

    def test_changed_uncontrolled_template_is_stale(self):
        template = types.SimpleNamespace(VersionGuid='v1', GetTemplateParameterIds=lambda: [Id(-100)],
                                         GetNonControlledTemplateParameterIds=lambda: [Id(-100)])
        self.doc.view.ViewTemplateId = Id(99)
        self.doc.GetElement = lambda value: template if value.Value == 99 else self.doc.view
        result = host.apply(self.app, self.original, self.edits)
        self.assertEqual(result['status'], 'failed')
        self.assertEqual(self.doc.events, [])

    def test_unchanged_settings_have_no_transaction(self):
        self.assertEqual(host.apply(self.app, self.original, self.original['planes'])['status'], 'unchanged')
        self.assertEqual(self.doc.events, [])

    def test_tampered_references_are_not_used(self):
        self.edits['cut'].update(level_id=2, finite_level_id=2, base_elevation=999)
        self.assertEqual(host.apply(self.app, self.original, self.edits)['status'], 'applied')
        self.assertEqual(self.doc.values['cut'], [1, 5.0])

    def test_relative_level_sentinels_use_project_origin_elevations(self):
        self.doc.values.update(top=[-3, 2.0], cut=[-2, 4.0], bottom=[-4, 1.0])
        planes = host.snapshot(self.app)['planes']
        self.assertEqual([planes[key]['elevation'] for key in ('top', 'cut', 'bottom')], [22.0, 14.0, 1.0])

    def test_unlimited_stays_unlimited_and_can_return_to_associated_level(self):
        self.doc.values['top'] = [-5, 10.0]
        self.original = host.snapshot(self.app)
        edits = copy.deepcopy(self.original['planes'])
        self.assertEqual(plane_elevation(edits['top'], 'top', 'FloorPlan'), float('inf'))
        edits['top']['unlimited'] = False
        self.assertEqual(host.apply(self.app, self.original, edits)['status'], 'applied')
        self.assertEqual(self.doc.values['top'], [1, 10.0])

    def test_nonfinite_offsets_rejected_and_ceiling_depth_direction_differs(self):
        self.edits['cut']['offset'] = float('nan')
        with self.assertRaises(ValueError):
            edited_planes(self.original, self.edits)
        planes = copy.deepcopy(self.original['planes'])
        self.assertTrue(validate_planes(planes, 'CeilingPlan'))
        planes['depth']['offset'] = 12.0
        self.assertEqual(validate_planes(planes, 'CeilingPlan'), [])

    def test_template_control_and_readonly_documents_fail_closed(self):
        template = types.SimpleNamespace(GetTemplateParameterIds=lambda: [Id(-100)],
                                         GetNonControlledTemplateParameterIds=lambda: [])
        self.doc.view.ViewTemplateId = Id(99)
        self.doc.GetElement = lambda _: template
        self.assertIn('template controls', host.lock_reason(self.doc, self.doc.view))
        self.doc.IsReadOnly = True
        self.assertIn('editable project', host.lock_reason(self.doc, self.doc.view))

    def test_dependent_view_scopes_are_rejected_before_any_edit(self):
        self.doc.view.GetDependentViewIds = lambda: [Id(43)]
        self.assertIn('dependent views', host.lock_reason(self.doc, self.doc.view))
        self.assertEqual(host.apply(self.app, self.original, self.edits)['status'], 'failed')
        self.assertEqual(self.doc.events, [])


if __name__ == '__main__':
    unittest.main()
