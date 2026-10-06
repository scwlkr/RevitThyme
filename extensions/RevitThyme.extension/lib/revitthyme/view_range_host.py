# -*- coding: utf-8 -*-
"""Revit API boundary for a modal Visual View Range editor."""
from pyrevit import DB
from System import Int64
from revitthyme.operations import identity
from revitthyme.view_range_state import KEYS, edited_planes

PLANES = (DB.PlanViewPlane.TopClipPlane, DB.PlanViewPlane.CutPlane,
          DB.PlanViewPlane.BottomClipPlane, DB.PlanViewPlane.ViewDepthPlane)


def number(value):
    return int(value.Value)


def eid(value):
    return DB.ElementId(Int64(value))


def levels(doc):
    return sorted([{'id': number(level.Id), 'name': level.Name,
                    'elevation': float(level.ProjectElevation)}
                   for level in DB.FilteredElementCollector(doc).OfClass(DB.Level)],
                  key=lambda row: (row['elevation'], row['id']))


def lock_reason(doc, view):
    if doc.IsFamilyDocument or doc.IsReadOnly or doc.IsModifiable:
        return 'Open an editable project outside another transaction.'
    if not isinstance(view, DB.ViewPlan) or str(view.ViewType) not in (
            'FloorPlan', 'CeilingPlan', 'EngineeringPlan') or view.IsTemplate:
        return 'Activate a floor, structural or reflected ceiling plan.'
    if number(view.GetPrimaryViewId()) != number(DB.ElementId.InvalidElementId):
        return 'Dependent plans are excluded from this source preview.'
    if list(view.GetDependentViewIds()):
        return 'This plan has dependent views. Their propagated ranges are not yet qualified; use the standard View Range dialog.'
    if number(view.ViewTemplateId) != number(DB.ElementId.InvalidElementId):
        template = doc.GetElement(view.ViewTemplateId)
        if template is None:
            return 'The assigned view template could not be resolved.'
        controlled = set(number(value) for value in template.GetTemplateParameterIds())
        excluded = set(number(value) for value in template.GetNonControlledTemplateParameterIds())
        if int(DB.BuiltInParameter.PLAN_VIEW_RANGE) in controlled - excluded:
            return 'The assigned view template controls View Range. Edit its settings first.'
    return None


def _planes(doc, view, level_rows):
    associated = next((row for row in level_rows if row['id'] == number(view.GenLevel.Id)), None)
    if associated is None:
        raise ValueError('The plan associated level could not be resolved.')
    values = {}
    view_range = view.GetViewRange()
    try:
        for key, plane in zip(KEYS, PLANES):
            reference = number(view_range.GetLevelId(plane))
            unlimited = reference == number(DB.PlanViewRange.Unlimited)
            row = next((item for item in level_rows if item['id'] == reference), None)
            if unlimited or reference == number(DB.PlanViewRange.Current):
                row = associated
            elif reference == number(DB.PlanViewRange.LevelAbove):
                row = next((item for item in level_rows
                            if item['elevation'] > associated['elevation'] + 1e-8), None)
            elif reference == number(DB.PlanViewRange.LevelBelow):
                row = next((item for item in reversed(level_rows)
                            if item['elevation'] < associated['elevation'] - 1e-8), None)
            if row is None:
                raise ValueError('{0}: reference level is unavailable.'.format(key.title()))
            offset = float(view_range.GetOffset(plane))
            values[key] = {'level_id': reference, 'finite_level_id':
                           associated['id'] if unlimited else reference,
                           'reference_name': row['name'], 'base_elevation': row['elevation'],
                           'offset': offset, 'unlimited': unlimited,
                           'allow_unlimited': key != 'cut',
                           'elevation': None if unlimited else row['elevation'] + offset}
    finally:
        view_range.Dispose()
    return values


def snapshot(uiapp):
    uidoc = uiapp.ActiveUIDocument
    if uidoc is None:
        raise ValueError('Open a project and activate a plan view.')
    if str(uiapp.Application.VersionNumber) != '2027':
        raise ValueError('This source preview targets Revit 2027; other versions are unverified.')
    doc, view = uidoc.Document, uidoc.ActiveView
    reason = lock_reason(doc, view)
    if reason:
        raise ValueError(reason)
    rows = levels(doc)
    unit = 'mm'
    try:
        unit_id = doc.GetUnits().GetFormatOptions(DB.SpecTypeId.Length).GetUnitTypeId().TypeId
        if 'feet' in unit_id or 'inches' in unit_id:
            unit = 'ft'
        elif 'meters-' in unit_id and 'millimeters' not in unit_id:
            unit = 'm'
    except Exception:
        pass
    template_id = number(view.ViewTemplateId)
    template_state = {'id': template_id}
    if template_id != number(DB.ElementId.InvalidElementId):
        template = doc.GetElement(view.ViewTemplateId)
        template_state.update(version=str(template.VersionGuid),
                              parameters=sorted(number(value) for value in template.GetTemplateParameterIds()),
                              excluded=sorted(number(value) for value in template.GetNonControlledTemplateParameterIds()))
    return {'target': identity(uiapp), 'view_id': number(view.Id), 'view_name': view.Name,
            'view_type': str(view.ViewType), 'levels': rows, 'planes': _planes(doc, view, rows),
            'unit': unit, 'locked_reason': None, 'template': template_state}


def _same_range(actual, expected):
    for key in KEYS:
        a, b = actual[key], expected[key]
        if a['level_id'] != b['level_id'] or a['unlimited'] != b['unlimited']:
            return False
        if abs(a['offset'] - b['offset']) > 1e-7:
            return False
    return True


class RejectFailures(DB.IFailuresPreprocessor):
    """Never silently resolve failures or finish a pending transaction as success."""
    def __init__(self):
        self.messages = []

    def PreprocessFailures(self, accessor):
        messages = list(accessor.GetFailureMessages())
        for message in messages:
            self.messages.append(message.GetDescriptionText())
        return (DB.FailureProcessingResult.ProceedWithRollBack if messages
                else DB.FailureProcessingResult.Continue)


def apply(uiapp, original, edits):
    """Called after modal in-Revit Apply. No Routes mutation endpoint is exposed."""
    result = {'status': 'failed', 'changed_ids': [], 'rollback': 'not_needed', 'diagnostics': []}
    group, transaction, candidate = None, None, None
    failures = RejectFailures()
    try:
        current = snapshot(uiapp)
        if any(current[key] != original[key] for key in ('target', 'view_id', 'view_type', 'levels', 'template')):
            raise ValueError('The active view, document, template or reference levels changed. Reopen the editor.')
        if not _same_range(current['planes'], original['planes']):
            raise ValueError('View Range changed since the preview. Reopen the editor.')
        proposed = edited_planes(current, edits)
        doc = uiapp.ActiveUIDocument.Document
        view = doc.GetElement(eid(current['view_id']))
        candidate = view.GetViewRange()
        expected = {}
        for key, plane in zip(KEYS, PLANES):
            settings = proposed[key]
            reference = number(DB.PlanViewRange.Unlimited) if settings['unlimited'] else settings['finite_level_id']
            candidate.SetLevelId(plane, eid(reference))
            candidate.SetOffset(plane, settings['offset'])
            expected[key] = dict(settings, level_id=reference)
        errors = list(view.CheckPlanViewRangeValidity(candidate))
        if errors:
            raise ValueError('Revit rejected the staged range: ' + ', '.join(str(error) for error in errors))
        if _same_range(current['planes'], expected):
            result['status'] = 'unchanged'
            return result
        group = DB.TransactionGroup(doc, 'RevitThyme Visual View Range')
        if group.Start() != DB.TransactionStatus.Started:
            raise RuntimeError('Could not start the undo group.')
        transaction = DB.Transaction(doc, 'Apply View Range')
        if transaction.Start() != DB.TransactionStatus.Started:
            raise RuntimeError('Could not start the view range transaction.')
        options = transaction.GetFailureHandlingOptions()
        options.SetFailuresPreprocessor(failures)
        options.SetClearAfterRollback(True)
        options.SetForcedModalHandling(True)
        transaction.SetFailureHandlingOptions(options)
        view.SetViewRange(candidate)
        doc.Regenerate()
        if not _same_range(_planes(doc, view, current['levels']), expected):
            raise RuntimeError('View Range read-back did not match the requested settings.')
        if transaction.Commit() != DB.TransactionStatus.Committed:
            raise RuntimeError('Revit did not commit the range; ' + '; '.join(failures.messages))
        readback = _planes(doc, view, current['levels'])
        if not _same_range(readback, expected):
            raise RuntimeError('Committed View Range read-back did not match.')
        if group.Assimilate() != DB.TransactionStatus.Committed:
            raise RuntimeError('Revit did not complete the undo group.')
        result.update(status='applied', changed_ids=[current['view_id']], planes=readback)
    except Exception as error:
        result['diagnostics'].append(str(error))
        statuses = []
        for scope in (transaction, group):
            if scope is None:
                continue
            try:
                status = scope.GetStatus()
                if status == DB.TransactionStatus.Started:
                    status = scope.RollBack()
                statuses.append(status)
            except Exception as rollback_error:
                result['diagnostics'].append('Rollback could not be confirmed: ' + str(rollback_error))
                statuses.append(None)
        if group is not None:
            result['rollback'] = ('confirmed' if statuses and
                                  statuses[-1] == DB.TransactionStatus.RolledBack else 'unconfirmed')
    finally:
        for resource in (candidate, transaction, group):
            if resource is not None:
                try:
                    resource.Dispose()
                except Exception:
                    pass
    return result
