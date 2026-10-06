# -*- coding: utf-8 -*-
"""Read-only operations shared by ribbon and Routes; never start transactions."""
import os
from pyrevit import DB, versionmgr
from pyrevit.loader import sessioninfo
from System.Diagnostics import Process
from revitthyme import config
from revitthyme.preissue_operation import check as preissue_check


def element_id(value):
    return int(value.Value)


def identity(uiapp):
    session = '{0}:{1}'.format(Process.GetCurrentProcess().Id,
                              sessioninfo.get_session_uuid())
    uidoc = uiapp.ActiveUIDocument
    doc = uidoc.Document if uidoc else None
    token = None
    if doc:
        token = '{0}:{1}:{2}'.format(session, doc.CreationGUID, doc.GetHashCode())
    return {'session': session, 'document': token}


def status(uiapp):
    uidoc = uiapp.ActiveUIDocument
    doc = uidoc.Document if uidoc else None
    return {
        'host': {'adapter': 'pyrevit',
                 'pyrevit_version': versionmgr.get_pyrevit_version().get_formatted(),
                 'revit_version': uiapp.Application.VersionNumber,
                 'revit_build': uiapp.Application.VersionBuild},
        'target': identity(uiapp),
        'document': None if doc is None else {
            'title': doc.Title, 'is_family': doc.IsFamilyDocument,
            'is_modified': doc.IsModified, 'is_read_only': doc.IsReadOnly,
            'is_modifiable': doc.IsModifiable,
            'active_view_id': element_id(uidoc.ActiveView.Id)},
        'readiness': 'no_document' if doc is None else 'read_only_operations_available',
        'generation_available': False,
    }


def inspect(uiapp):
    doc = uiapp.ActiveUIDocument.Document
    if doc.IsFamilyDocument:
        return {'status': 'unsupported_document',
                'diagnostics': ['TimberFold inspection requires a project document.']}
    root = config.tool_root()
    manifest = config.read_json(os.path.join(config.extension_root(), 'tool.json'))
    missing = config.missing_entries(root) if root else []
    walls = list(DB.FilteredElementCollector(doc).OfClass(DB.Wall))
    exterior, skipped, unsupported = [], [], []
    for wall in walls:
        number = element_id(wall.Id)
        if wall.WallType.Function != DB.WallFunction.Exterior:
            skipped.append(number)
            continue
        exterior.append(number)
        location = wall.Location
        if (wall.WallType.Kind != DB.WallKind.Basic or
                not isinstance(location, DB.LocationCurve) or
                not isinstance(location.Curve, DB.Line)):
            unsupported.append(number)
    roofs = [element_id(x.Id) for x in
             DB.FilteredElementCollector(doc).OfClass(DB.RoofBase)]
    return {
        'status': 'inspected',
        'tool': {'id': 'timberfold', 'registered_version': manifest['source']['declared_version'],
                 'reference_commit': manifest['source']['reference_commit'],
                 'source_revision_verified': False,
                 'configuration': 'unconfigured' if not root else
                                  ('missing_files' if missing else 'files_available'),
                 'missing_files': missing, 'generation_available': False},
        'scope': {'wall_count': len(walls), 'exterior_wall_ids': sorted(exterior),
                  'roof_ids': sorted(roofs), 'unsupported_wall_ids': sorted(unsupported),
                  'skipped_interior_wall_ids': sorted(skipped)},
        'diagnostics': [
            'Counts are candidates only; geometry, phases, openings and fabrication settings are not validated.',
            'Curved, curtain and stacked exterior walls need extraction diagnostics.',
            'TimberFold generation is not integrated in this release.'],
    }


def execute(operation, uiapp, parameters=None):
    parameters = parameters if parameters is not None else {}
    suite = config.read_json(os.path.join(config.extension_root(), 'suite.json'))
    result = {'schema_version': 1, 'suite_version': suite['version'],
              'operation': operation, 'effects': ['read_model'],
              'changed_ids': [], 'skipped_ids': [], 'target': identity(uiapp)}
    if operation not in ('revitthyme_status', 'timberfold_inspect', 'preissue_check'):
        result.update(status='unknown_operation', diagnostics=['Operation is not implemented.'])
        return result
    allowed = ['target', 'standards'] if operation == 'preissue_check' else ['target']
    if not isinstance(parameters, dict) or set(parameters) - set(allowed):
        result.update(status='invalid_request', diagnostics=['Accepted fields: ' + ', '.join(allowed)])
        return result
    target = parameters.get('target')
    if operation != 'revitthyme_status' and target is None:
        result.update(status='target_required', diagnostics=['Discover target using revitthyme_status.'])
        return result
    if target is not None and target != result['target']:
        result.update(status='target_mismatch', diagnostics=['Active session/document changed; rediscover target.'])
        return result
    if operation != 'revitthyme_status' and uiapp.ActiveUIDocument is None:
        result.update(status='no_document', diagnostics=['Open a project to inspect.'])
        return result
    try:
        if operation == 'revitthyme_status':
            data = status(uiapp)
        elif operation == 'preissue_check':
            data = preissue_check(uiapp, parameters)
        else:
            data = inspect(uiapp)
        result.update(status=data.pop('status', 'succeeded'), data=data)
        if operation == 'timberfold_inspect':
            result['skipped_ids'] = data.get('scope', {}).get('skipped_interior_wall_ids', [])
    except Exception as error:
        result.update(status='failed', diagnostics=[str(error)])
    return result
