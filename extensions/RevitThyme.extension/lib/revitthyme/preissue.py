# -*- coding: utf-8 -*-
"""Deterministic rules on scalar snapshots, portable to CPython and IronPython."""
import hashlib
import json
import math
from revitthyme.preissue_standards import CHECK_IDS, TEXT_TYPES, validate

try:
    INTEGER_TYPES = (int, long)
except NameError:
    INTEGER_TYPES = (int,)


def _text(value):
    return value.strip() if isinstance(value, TEXT_TYPES) else ''


def _matches(value, pattern):
    """Full-value * / ? matching with bounded O(value * pattern) work, no regex."""
    state = [True] + [False] * len(pattern)
    for index, token in enumerate(pattern, 1):
        state[index] = state[index - 1] and token == '*'
    for character in value:
        next_state = [False] * (len(pattern) + 1)
        for index, token in enumerate(pattern, 1):
            next_state[index] = ((next_state[index - 1] or state[index]) if token == '*'
                                 else state[index - 1] and (token == '?' or token == character))
        state = next_state
    return state[-1]


def _finding(check, code, items, message, evidence, standards):
    items = sorted(items, key=lambda item: item['id'])
    keys = [item.get('unique_id') or 'id:{0}'.format(item['id']) for item in items]
    source = json.dumps([check, code, keys], ensure_ascii=True, separators=(',', ':'))
    return {'id': 'preissue:' + hashlib.sha256(source.encode('utf-8')).hexdigest()[:24],
            'check_id': check, 'code': code,
            'severity': standards.get('severity', {}).get(check, 'warning'),
            'element_ids': [item['id'] for item in items],
            'element_unique_ids': [item.get('unique_id') for item in items],
            'message': message, 'evidence': evidence}


class Check(object):
    def __init__(self, name, collection, findings):
        self.name, self.findings = name, findings
        self.value = {'check_id': name, 'status': 'checked', 'checked_count': 0,
                      'not_checked_ids': [], 'excluded_ids': [], 'diagnostics': []}
        if collection.get('status') != 'complete':
            self.incomplete('collection_' + collection.get('status', 'unavailable'))
        if collection.get('unreadable_count', 0):
            self.value['unreadable_count'] = collection['unreadable_count']

    def incomplete(self, reason, item=None):
        self.value['status'] = 'not_checked'
        self.value['diagnostics'].append(reason)
        if item is not None:
            self.value['not_checked_ids'].append(item['id'])

    def require(self, item, fields):
        missing = [field for field in fields if field not in item]
        if missing:
            self.incomplete('missing_fields:' + ','.join(missing), item)
        return not missing

    def add(self, code, items, message, evidence, standards):
        self.findings.append(_finding(self.name, code, items, message, evidence, standards))

    def finish(self):
        if self.value['status'] == 'checked' and not self.value['checked_count']:
            self.value['diagnostics'].append('empty_checked_scope')
        for field in ('not_checked_ids', 'excluded_ids', 'diagnostics'):
            self.value[field] = sorted(set(self.value[field]))
        self.value['finding_count'] = sum(x['check_id'] == self.name for x in self.findings)
        return self.value


def _placement(check, items, standards):
    for item in items:
        if not check.require(item, ['has_location']):
            continue
        if item['has_location'] is False:
            check.add('room.unplaced', [item], 'Room has no placement location.', {}, standards)
        elif item['has_location'] is True:
            if not check.require(item, ['area']):
                continue
            area = item['area']
            if (isinstance(area, bool) or not isinstance(area, INTEGER_TYPES + (float,)) or
                    math.isnan(area) or math.isinf(area) or area < 0):
                check.incomplete('invalid_area', item)
                continue
            if area == 0:
                check.add('room.zero_area', [item],
                          'Placed room has zero area; review enclosure or redundancy.',
                          {'area_internal': area}, standards)
        else:
            check.incomplete('invalid_placement', item)
            continue
        check.value['checked_count'] += 1


def _duplicates(check, items, fields, standards):
    groups = {}
    for item in items:
        if not check.require(item, fields):
            continue
        number = _text(item['number'])
        if not number:
            check.incomplete('number_unavailable_or_empty', item)
            continue
        if 'phase_id' in fields and (isinstance(item['phase_id'], bool) or
                                    not isinstance(item['phase_id'], INTEGER_TYPES) or
                                    item['phase_id'] <= 0):
            check.incomplete('room_phase_unavailable', item)
            continue
        if 'design_option_id' in fields and (isinstance(item['design_option_id'], bool) or
                not isinstance(item['design_option_id'], INTEGER_TYPES) or
                item['design_option_id'] < -1 or item['design_option_id'] == 0):
            check.incomplete('room_design_option_unavailable', item)
            continue
        key = tuple([number] + [item[field] for field in fields if field != 'number'])
        groups.setdefault(key, []).append(item)
        check.value['checked_count'] += 1
    for key in sorted(groups):
        if len(groups[key]) > 1:
            evidence = dict(zip(fields, key))
            check.add(check.name, groups[key], 'Repeated number in the checked scope.',
                      evidence, standards)


def _names(check, items, field, patterns, standards):
    if not patterns:
        check.incomplete('firm_standard_absent')
        return
    for item in items:
        if not check.require(item, [field]):
            continue
        name = _text(item[field])
        if not name or not any(_matches(name, x) for x in patterns):
            check.add(check.name, [item], 'Value does not match the configured naming standard.',
                      {'value': item[field], 'patterns': patterns}, standards)
        check.value['checked_count'] += 1


def _templates(check, items, rules, standards):
    required, names = rules.get('require_template', False), rules.get('template_names')
    if not required and not names:
        check.incomplete('firm_standard_absent_or_disabled')
        return
    for item in items:
        if not check.require(item, ['template_name']):
            continue
        template = item['template_name']
        if (required and template is None) or (template is not None and names and template not in names):
            check.add(check.name, [item], 'View template does not meet the configured standard.',
                      {'template_name': template, 'required': required, 'allowed_names': names}, standards)
        check.value['checked_count'] += 1


def evaluate(snapshot, standards=None):
    standards = validate(standards)
    if snapshot.get('schema_version') != 1:
        raise ValueError('Unsupported Pre-Issue snapshot schema')
    findings, checks = [], []
    empty = {'status': 'unavailable', 'items': []}
    collections = dict((name, snapshot.get(name, empty)) for name in ('rooms', 'doors', 'views', 'sheets'))
    for name in CHECK_IDS:
        collection_name = name.split('.')[0] + 's'
        collection = collections[collection_name]
        check = Check(name, collection, findings)
        items = sorted(collection.get('items', []), key=lambda item: item['id'])
        if name.startswith('view.'):
            rules = standards.get('views', {})
            scoped = []
            for item in items:
                if not check.require(item, ['view_type']):
                    continue
                if item['view_type'] in rules.get('types', []):
                    scoped.append(item)
                else:
                    check.value['excluded_ids'].append(item['id'])
            items = scoped
        elif name.startswith('sheet.'):
            scoped = []
            for item in items:
                if not check.require(item, ['is_placeholder']):
                    continue
                if not item['is_placeholder'] or standards.get('sheets', {}).get('include_placeholders', False):
                    scoped.append(item)
                else:
                    check.value['excluded_ids'].append(item['id'])
            items = scoped
        if name == 'room.placement':
            _placement(check, items, standards)
        elif name == 'room.duplicate_number':
            _duplicates(check, items, ['number', 'phase_id', 'design_option_id'], standards)
        elif name == 'door.mark':
            for item in items:
                if check.require(item, ['mark']):
                    if not _text(item['mark']):
                        check.add(name, [item], 'Door instance Mark is empty.', {}, standards)
                    check.value['checked_count'] += 1
        elif name == 'view.name':
            _names(check, items, 'name', rules.get('name_patterns'), standards)
        elif name == 'view.template':
            _templates(check, items, rules, standards)
        elif name in ('sheet.name', 'sheet.number'):
            field = name.split('.')[1]
            _names(check, items, field, standards.get('sheets', {}).get(field + '_patterns'), standards)
        elif name == 'sheet.duplicate_number':
            _duplicates(check, items, ['number'], standards)
        checks.append(check.finish())
    findings.sort(key=lambda value: (value['check_id'], value['element_ids'], value['code']))
    incomplete = any(check['status'] == 'not_checked' for check in checks)
    return {'report_schema_version': 1, 'scope': snapshot.get('scope'),
            'snapshot_scope': dict((name, {'status': value.get('status', 'unavailable'),
                                         'item_count': len(value.get('items', [])),
                                         'excluded_count': value.get('excluded_count', 0),
                                         'unreadable_count': value.get('unreadable_count', 0)})
                                   for name, value in collections.items()),
            'standards': standards, 'checks': checks, 'findings': findings,
            'summary': {'finding_count': len(findings), 'incomplete': incomplete,
                        'conclusion': 'findings_present' if findings else
                                      ('incomplete' if incomplete else 'no_findings')},
            'limitations': ['Rule-based QA does not certify code compliance or issue readiness.',
                            'Zero area does not distinguish unenclosed from redundant rooms.',
                            'Duplicate room numbers are grouped by created phase and design option; geometry is not compared.',
                            'Links, annotations, dimensions and electrical/code compliance are not checked.']}
