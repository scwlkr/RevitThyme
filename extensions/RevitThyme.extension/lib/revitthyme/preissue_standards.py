# -*- coding: utf-8 -*-
"""Small, declarative firm standards; no executable expressions or model writes."""
try:
    TEXT_TYPES = (basestring,)
except NameError:
    TEXT_TYPES = (str,)

CHECK_IDS = ('room.placement', 'room.duplicate_number', 'door.mark',
             'view.name', 'view.template', 'sheet.name', 'sheet.number',
             'sheet.duplicate_number')


def _object(value, allowed, label):
    if not isinstance(value, dict) or set(value) - set(allowed):
        raise ValueError('Invalid ' + label + ' fields')


def _strings(value, label):
    if (not isinstance(value, list) or not value or len(value) > 50 or
            any(not isinstance(x, TEXT_TYPES) or not x.strip() or
                len(x) > 200 for x in value)):
        raise ValueError(label + ' must be 1-50 nonempty strings (max 200 characters)')


def validate(value):
    """Validate before collecting the model; absence means no firm standards."""
    if value is None:
        return {'schema_version': 1}
    _object(value, ('schema_version', 'name', 'severity', 'views', 'sheets'), 'standards')
    if type(value.get('schema_version')) is not int or value['schema_version'] != 1:
        raise ValueError('Unsupported Pre-Issue standards schema')
    if 'name' in value and (not isinstance(value['name'], TEXT_TYPES) or
                            not value['name'].strip() or len(value['name']) > 200):
        raise ValueError('Standards name must be a nonempty string (max 200 characters)')
    severity = value.get('severity', {})
    _object(severity, CHECK_IDS, 'severity')
    if any(x not in ('info', 'warning', 'error') for x in severity.values()):
        raise ValueError('Severity must be info, warning or error')
    views = value.get('views', {})
    _object(views, ('types', 'name_patterns', 'require_template', 'template_names'), 'views')
    if views:
        _strings(views.get('types'), 'View types')
    for field in ('name_patterns', 'template_names'):
        if field in views:
            _strings(views[field], 'View ' + field)
    if 'require_template' in views and type(views['require_template']) is not bool:
        raise ValueError('require_template must be a boolean')
    sheets = value.get('sheets', {})
    _object(sheets, ('name_patterns', 'number_patterns', 'include_placeholders'), 'sheets')
    for field in ('name_patterns', 'number_patterns'):
        if field in sheets:
            _strings(sheets[field], 'Sheet ' + field)
    if 'include_placeholders' in sheets and type(sheets['include_placeholders']) is not bool:
        raise ValueError('include_placeholders must be a boolean')
    return value
