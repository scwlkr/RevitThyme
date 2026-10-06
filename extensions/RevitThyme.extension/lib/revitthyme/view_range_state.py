# -*- coding: utf-8 -*-
"""View-range math in Revit internal feet; no Revit or UI dependencies."""
import math

KEYS = ('top', 'cut', 'bottom', 'depth')
UNIT_FACTORS = {'mm': 304.8, 'm': 0.3048, 'ft': 1.0}


def finite(value):
    try:
        number = float(value)
        return not math.isnan(number) and not math.isinf(number)
    except (TypeError, ValueError, OverflowError):
        return False


def plane_elevation(plane, key, view_type):
    if plane.get('unlimited'):
        positive = key == 'top' or (key == 'depth' and view_type == 'CeilingPlan')
        return float('inf') if positive else -float('inf')
    return float(plane['base_elevation']) + float(plane['offset'])


def validate_planes(planes, view_type):
    errors = []
    if not isinstance(planes, dict) or set(planes) != set(KEYS):
        return ['All four range planes are required.']
    for key in KEYS:
        plane = planes[key]
        if not isinstance(plane, dict) or not finite(plane.get('offset')):
            errors.append('{0}: enter a finite offset.'.format(key.title()))
        elif not finite(plane.get('base_elevation')):
            errors.append('{0}: its reference level cannot be resolved.'.format(key.title()))
        elif key == 'cut' and plane.get('unlimited'):
            errors.append('Cut Plane must use a finite level and offset.')
    if errors:
        return errors
    heights = dict((key, plane_elevation(planes[key], key, view_type)) for key in KEYS)
    if heights['top'] < heights['cut']:
        errors.append('Top must be at or above Cut Plane.')
    if heights['bottom'] > heights['cut']:
        errors.append('Bottom must be at or below Cut Plane.')
    if view_type == 'CeilingPlan':
        if heights['depth'] < heights['top']:
            errors.append('For a ceiling plan, View Depth must be at or above Top.')
    elif heights['depth'] > heights['bottom']:
        errors.append('View Depth must be at or below Bottom.')
    return errors


def edited_planes(snapshot, edits):
    """Accept only editable values; caller-provided references cannot retarget a plane."""
    if not isinstance(edits, dict) or set(edits) != set(KEYS):
        raise ValueError('All four range planes are required.')
    result = {}
    for key in KEYS:
        supplied = edits[key]
        if not isinstance(supplied, dict) or not isinstance(supplied.get('unlimited'), bool):
            raise ValueError('{0}: invalid plane settings.'.format(key))
        if not finite(supplied.get('offset')):
            raise ValueError('{0}: enter a finite offset.'.format(key))
        original = snapshot['planes'][key]
        if supplied['unlimited'] and not original.get('allow_unlimited'):
            raise ValueError('{0}: Unlimited is unsupported.'.format(key))
        result[key] = dict(original, offset=float(supplied['offset']),
                           unlimited=supplied['unlimited'])
    errors = validate_planes(result, snapshot['view_type'])
    if errors:
        raise ValueError('\n'.join(errors))
    return result
