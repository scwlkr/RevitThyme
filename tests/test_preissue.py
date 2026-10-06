"""Acceptance fixtures for deterministic, read-only model housekeeping."""
import copy
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'extensions/RevitThyme.extension/lib'))
from revitthyme.preissue import evaluate
from revitthyme.preissue_standards import validate


def collection(items, status='complete'):
    return {'status': status, 'items': items}


def snapshot(**values):
    result = {'schema_version': 1, 'scope': 'fixture_no_links'}
    result.update((name, collection([])) for name in ('rooms', 'doors', 'views', 'sheets'))
    result.update((name, collection(items)) for name, items in values.items())
    return result


def room(number, element_id, phase=11, option=-1, placed=True, area=100):
    return {'id': element_id, 'unique_id': 'room-' + str(element_id),
            'number': number, 'phase_id': phase, 'design_option_id': option,
            'has_location': placed, 'area': area}


def checks(report):
    return {value['check_id']: value for value in report['checks']}


class PreIssueTests(unittest.TestCase):
    def test_unconfigured_intrinsic_findings_and_truthful_standards_coverage(self):
        model = snapshot(rooms=[room('101', 1, placed=False), room('102', 2, area=0)],
                         doors=[{'id': 3, 'mark': '  '}, {'id': 4, 'mark': 'D-1'},
                                {'id': 5, 'mark': None}, {'id': 6}])
        report = evaluate(model)
        self.assertEqual(['door.mark', 'door.mark', 'room.unplaced', 'room.zero_area'],
                         [value['code'] for value in report['findings']])
        values = checks(report)
        self.assertEqual('not_checked', values['door.mark']['status'])
        self.assertEqual([6], values['door.mark']['not_checked_ids'])
        self.assertEqual(3, values['door.mark']['checked_count'])
        for name in ('view.name', 'view.template', 'sheet.name', 'sheet.number'):
            self.assertEqual('not_checked', values[name]['status'])
        self.assertTrue(report['summary']['incomplete'])
        self.assertEqual('findings_present', report['summary']['conclusion'])

    def test_duplicate_numbers_are_exact_trimmed_phase_and_option_scoped(self):
        model = snapshot(rooms=[room(' 101 ', 1), room('101', 2), room('101', 3, phase=12),
                                room('101', 4, option=20), room('102a', 5), room('102A', 6)])
        findings = evaluate(model)['findings']
        self.assertEqual(1, len(findings))
        self.assertEqual([1, 2], findings[0]['element_ids'])
        self.assertEqual({'number': '101', 'phase_id': 11, 'design_option_id': -1},
                         findings[0]['evidence'])

    def test_findings_are_stable_across_order_and_input_is_not_mutated(self):
        model = snapshot(rooms=[room('101', 2), room('101', 1)],
                         doors=[{'id': 4, 'unique_id': 'door-4', 'mark': ''}])
        original = copy.deepcopy(model)
        report = evaluate(model)
        model['rooms']['items'].reverse()
        self.assertEqual(report, evaluate(model))
        model['rooms']['items'].reverse()
        self.assertEqual(original, model)
        self.assertEqual(len(report['findings']), len({x['id'] for x in report['findings']}))
        self.assertEqual(report['findings'][0]['id'], evaluate(model)['findings'][0]['id'])

    def test_configured_view_and_sheet_rules_scope_and_severity(self):
        standards = json.loads((ROOT / 'config/preissue-standards.example.json').read_text())
        standards['severity']['view.template'] = 'error'
        model = snapshot(views=[
            {'id': 1, 'view_type': 'FloorPlan', 'name': 'A-Level 1', 'template_name': None},
            {'id': 2, 'view_type': 'CeilingPlan', 'name': 'a-Level 1', 'template_name': 'Wrong'},
            {'id': 3, 'view_type': 'Section', 'name': 'Other', 'template_name': None},
            {'id': 4, 'view_type': 'FloorPlan', 'name': 'A-Level 2', 'template_name': 'A - Floor Plan'}],
            sheets=[{'id': 5, 'name': 'A - Plans', 'number': 'A-101', 'is_placeholder': False},
                    {'id': 6, 'name': '', 'number': 'A-10', 'is_placeholder': False},
                    {'id': 7, 'name': 'Other', 'number': '', 'is_placeholder': True}])
        report = evaluate(model, standards)
        values = checks(report)
        self.assertEqual([3], values['view.name']['excluded_ids'])
        self.assertEqual([7], values['sheet.number']['excluded_ids'])
        self.assertTrue(all(x['status'] == 'checked' for x in values.values()))
        self.assertEqual(5, len(report['findings']))
        self.assertEqual(['error', 'error'], [x['severity'] for x in report['findings']
                                            if x['check_id'] == 'view.template'])
        self.assertFalse(report['summary']['incomplete'])

    def test_missing_or_invalid_facts_never_invent_a_pass_or_duplicate(self):
        model = snapshot(rooms=[room('101', 1, phase=-1), room('101', 2, option=None),
                                room('', 3), room('103', 4, area=float('nan'))])
        del model['rooms']['items'][2]['has_location']
        model['doors'] = collection([], 'unavailable')
        model['sheets'] = collection([{'id': 10, 'is_placeholder': False, 'number': 'S1'},
                                      {'id': 11, 'is_placeholder': False, 'number': 'S1'}], 'partial')
        report = evaluate(model)
        values = checks(report)
        self.assertEqual([1, 2, 3], values['room.duplicate_number']['not_checked_ids'])
        self.assertEqual([3, 4], values['room.placement']['not_checked_ids'])
        self.assertEqual('not_checked', values['door.mark']['status'])
        self.assertEqual('not_checked', values['sheet.duplicate_number']['status'])
        self.assertEqual([10, 11], report['findings'][0]['element_ids'])

    def test_optional_template_allowlist_and_placeholder_opt_in(self):
        standards = {'schema_version': 1,
                     'views': {'types': ['FloorPlan'], 'template_names': ['Allowed']},
                     'sheets': {'include_placeholders': True, 'number_patterns': ['S?']}}
        model = snapshot(views=[{'id': 1, 'view_type': 'FloorPlan', 'template_name': None},
                                 {'id': 2, 'view_type': 'FloorPlan', 'template_name': 'Other'}],
                         sheets=[{'id': 3, 'number': 'S1', 'is_placeholder': True},
                                 {'id': 4, 'number': 'S1', 'is_placeholder': False}])
        findings = evaluate(model, standards)['findings']
        self.assertEqual(['sheet.duplicate_number', 'view.template'], [x['code'] for x in findings])
        self.assertEqual([2], findings[1]['element_ids'])

    def test_empty_complete_scope_is_explicit_and_no_findings_requires_coverage(self):
        report = evaluate(snapshot(), {'schema_version': 1,
                          'views': {'types': ['FloorPlan'], 'name_patterns': ['*'], 'require_template': True},
                          'sheets': {'name_patterns': ['*'], 'number_patterns': ['*']}})
        self.assertEqual('no_findings', report['summary']['conclusion'])
        self.assertTrue(all('empty_checked_scope' in x['diagnostics'] for x in report['checks']))
        self.assertEqual('incomplete', evaluate(snapshot())['summary']['conclusion'])

    def test_strict_bounded_standards_and_supported_snapshot_schema(self):
        invalid = [{}, {'schema_version': True}, {'schema_version': 2},
                   {'schema_version': 1, 'script': 'anything'},
                   {'schema_version': 1, 'views': {'name_patterns': ['A-*']}},
                   {'schema_version': 1, 'views': {'types': ['FloorPlan'], 'require_template': 'yes'}},
                   {'schema_version': 1, 'sheets': {'number_patterns': []}},
                   {'schema_version': 1, 'sheets': {'name_patterns': ['x' * 201]}},
                   {'schema_version': 1, 'sheets': {'name_patterns': ['x'] * 51}},
                   {'schema_version': 1, 'severity': {'room.placement': 'critical'}},
                   {'schema_version': 1, 'severity': {'unknown': 'error'}}]
        for value in invalid:
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate(value)
        with self.assertRaises(ValueError):
            evaluate({'schema_version': 2})

    def test_wildcard_patterns_are_full_value_literal_case_sensitive_and_bounded(self):
        model = snapshot(sheets=[{'id': 1, 'name': 'A - Plans', 'number': 'A-101', 'is_placeholder': False}])
        for pattern, expected in [('A-???', 0), ('A-*', 0), ('*-101', 0), ('A-10', 1),
                                  ('a-*', 1), ('A-[0-9][0-9][0-9]', 1), ('*a' * 90 + 'b', 1)]:
            with self.subTest(pattern=pattern):
                report = evaluate(model, {'schema_version': 1, 'sheets': {'number_patterns': [pattern]}})
                self.assertEqual(expected, checks(report)['sheet.number']['finding_count'])


if __name__ == '__main__':
    unittest.main()
