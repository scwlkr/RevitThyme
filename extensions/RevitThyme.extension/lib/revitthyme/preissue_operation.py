# -*- coding: utf-8 -*-
"""Read-only operation boundary shared by both callers."""
from revitthyme import config, preissue, preissue_snapshot
from revitthyme.preissue_standards import validate


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
    before = bool(doc.IsModified)
    report = preissue.evaluate(preissue_snapshot.collect(doc), standards)
    after = bool(doc.IsModified)
    report['standards_source'] = 'request' if supplied else 'user_file_or_unconfigured'
    report['readback'] = {'is_modified_before': before, 'is_modified_after': after,
                          'modified_flag_unchanged': before == after,
                          'scope': 'document_modified_flag_only'}
    report['status'] = 'checked' if before == after else 'document_changed_during_check'
    return report
