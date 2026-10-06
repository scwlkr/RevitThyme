"""Fake Revit boundary verifies scalar extraction and honest unavailable facts."""
import importlib.util
import json
import sys
import types
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'extensions/RevitThyme.extension/lib'))
from revitthyme.preissue import evaluate


def eid(value):
    return types.SimpleNamespace(Value=value)


class Parameter:
    def __init__(self, value):
        self.value = value

    def AsString(self):
        return self.value


class Element:
    def __init__(self, number, category=None, **values):
        self.Id, self.UniqueId = eid(number), 'uid-' + str(number)
        self.category = category
        self.parameters = {}
        self.__dict__.update(values)

    def get_Parameter(self, parameter):
        return self.parameters.get(parameter)


class View(Element):
    pass


class Sheet(View):
    pass


class Collector:
    disposed = 0

    def __init__(self, doc):
        self.doc, self.elements = doc, doc.elements

    def OfCategory(self, category):
        if category == self.doc.unavailable:
            raise RuntimeError('Fixture collection failure')
        self.elements = [x for x in self.elements if x.category == category]
        return self

    def OfClass(self, cls):
        self.elements = [x for x in self.elements if isinstance(x, cls)]
        return self

    def WhereElementIsNotElementType(self):
        self.elements = [x for x in self.elements if not getattr(x, 'IsElementType', False)]
        return self

    def __iter__(self):
        return iter(self.elements)

    def Dispose(self):
        Collector.disposed += 1


class Document:
    def __init__(self, elements, unavailable=None):
        self.elements, self.unavailable = elements, unavailable

    def GetElement(self, value):
        return next((x for x in self.elements if x.Id.Value == value.Value), None)


def load_adapter():
    db = types.SimpleNamespace(
        FilteredElementCollector=Collector, View=View, ViewSheet=Sheet,
        BuiltInCategory=types.SimpleNamespace(OST_Rooms='rooms', OST_Doors='doors'),
        BuiltInParameter=types.SimpleNamespace(ROOM_NUMBER='number', ALL_MODEL_MARK='mark'))
    spec = importlib.util.spec_from_file_location('fixture_preissue_snapshot',
        ROOT / 'extensions/RevitThyme.extension/lib/revitthyme/preissue_snapshot.py')
    adapter = importlib.util.module_from_spec(spec)
    with patch.dict(sys.modules, {'pyrevit': types.SimpleNamespace(DB=db)}):
        spec.loader.exec_module(adapter)
    return adapter


class SnapshotBoundaryTests(unittest.TestCase):
    def setUp(self):
        Collector.disposed = 0
        self.adapter = load_adapter()

    def test_host_snapshot_is_scalar_sorted_scoped_and_has_no_write_boundary(self):
        room = Element(2, 'rooms', Location=None, Area=0, CreatedPhaseId=eid(10), DesignOption=None)
        room.parameters['number'] = Parameter('101')
        option_room = Element(3, 'rooms', Location=object(), Area=20, CreatedPhaseId=eid(11),
                              DesignOption=Element(50))
        option_room.parameters['number'] = Parameter('101')
        door = Element(4, 'doors')
        door.parameters['mark'] = Parameter(None)
        door_type = Element(5, 'doors', IsElementType=True)
        template = View(6, IsTemplate=True, Name='Office')
        view = View(7, IsTemplate=False, Name='A-Plan', ViewType='FloorPlan', ViewTemplateId=eid(6))
        no_template = View(8, IsTemplate=False, Name='A-Plan 2', ViewType='FloorPlan', ViewTemplateId=eid(-1))
        sheet = Sheet(9, IsTemplate=False, Name='A - Plans', SheetNumber='A-101', IsPlaceholder=False)
        doc = Document([sheet, view, door_type, template, room, door, no_template, option_room])
        result = self.adapter.collect(doc)
        json.dumps(result)  # No live Revit objects escape.
        self.assertEqual([2, 3], [x['id'] for x in result['rooms']['items']])
        self.assertEqual(-1, result['rooms']['items'][0]['design_option_id'])
        self.assertEqual(50, result['rooms']['items'][1]['design_option_id'])
        self.assertFalse(result['rooms']['items'][0]['has_location'])
        self.assertEqual([4], [x['id'] for x in result['doors']['items']])
        self.assertIsNone(result['doors']['items'][0]['mark'])
        self.assertEqual(2, result['views']['excluded_count'])
        self.assertEqual('Office', result['views']['items'][0]['template_name'])
        self.assertIsNone(result['views']['items'][1]['template_name'])
        self.assertEqual(4, Collector.disposed)
        # Fake DB intentionally exposes no Transaction and Document has no mutation API.
        self.assertEqual('findings_present', evaluate(result)['summary']['conclusion'])

    def test_missing_parameters_and_failed_properties_are_not_empty_values(self):
        door = Element(1, 'doors')
        room = Element(2, 'rooms', Area=1, DesignOption=None)
        room.parameters['number'] = Parameter('101')
        view = View(3, IsTemplate=False, Name='A-Plan', ViewType='FloorPlan', ViewTemplateId=eid(999))
        result = self.adapter.collect(Document([door, room, view]))
        self.assertNotIn('mark', result['doors']['items'][0])
        self.assertNotIn('has_location', result['rooms']['items'][0])
        self.assertNotIn('phase_id', result['rooms']['items'][0])
        self.assertNotIn('template_name', result['views']['items'][0])
        report = evaluate(result, {'schema_version': 1,
                                  'views': {'types': ['FloorPlan'], 'require_template': True}})
        checks = {x['check_id']: x for x in report['checks']}
        for name in ('door.mark', 'room.placement', 'room.duplicate_number', 'view.template'):
            self.assertEqual('not_checked', checks[name]['status'])
        self.assertEqual([], report['findings'])

    def test_collection_failure_and_unidentified_element_are_not_a_clean_scope(self):
        broken = types.SimpleNamespace(category='rooms')
        result = self.adapter.collect(Document([broken], unavailable='doors'))
        self.assertEqual('unavailable', result['doors']['status'])
        self.assertEqual('partial', result['rooms']['status'])
        self.assertEqual(1, result['rooms']['unreadable_count'])
        self.assertEqual(4, Collector.disposed)
        values = {x['check_id']: x for x in evaluate(result)['checks']}
        self.assertEqual('not_checked', values['door.mark']['status'])
        self.assertEqual('not_checked', values['room.placement']['status'])


if __name__ == '__main__':
    unittest.main()
