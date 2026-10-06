# -*- coding: utf-8 -*-
"""Read-only operation boundary shared by both callers."""
from revitthyme import config, preissue, preissue_snapshot
from revitthyme.preissue_standards import validate


def _validate_view_types(standards):
    configured = standards.get('views', {}).get('types', [])
    if not configured:
        return None
    try:
        from pyrevit import DB
        from System import Enum
        names = set(str(name) for name in Enum.GetNames(DB.ViewType))
        if not names:
            raise ValueError('Host ViewType enum is empty')
    except Exception:
        return {'status': 'host_validation_unavailable',
                'diagnostics': ['Host ViewType enum unavailable; configured view standards were not checked.']}
    unknown = sorted(set(configured) - names)
    if unknown:
        return {'status': 'invalid_standards',
                'diagnostics': ['Unknown host ViewType names: ' + ', '.join(unknown)]}
    return None


def check(uiapp, parameters):
    doc = uiapp.ActiveUIDocument.Document
    if doc.IsFamilyDocument:
        return {'status': 'unsupported_document',
                'diagnostics': ['Pre-Issue Check requires a project document.']}
    supplied = 'standards' in parameters
    try:
        standards = validate(parameters['standards'] if supplied else config.preissue_standards())
    except Exception as error:
        return {'status': 'invalid_standards', 'diagnostics': [str(error)]}
    type_error = _validate_view_types(standards)
    if type_error is not None:
        return type_error
    before = bool(doc.IsModified)
    report = preissue.evaluate(preissue_snapshot.collect(doc), standards)
    after = bool(doc.IsModified)
    report['standards_source'] = 'request' if supplied else 'user_file_or_unconfigured'
    report['readback'] = {'is_modified_before': before, 'is_modified_after': after,
                          'modified_flag_unchanged': before == after,
                          'scope': 'document_modified_flag_only'}
    report['status'] = 'checked' if before == after else 'document_changed_during_check'
    return report
