# -*- coding: utf-8 -*-
"""Read the host document in the caller's valid Revit API context only."""
from pyrevit import DB

try:
    text = unicode
except NameError:
    text = str


def _id(value):
    return int(value.Value)


def _parameter(element, parameter):
    value = element.get_Parameter(parameter)
    if value is None:
        raise ValueError('Parameter unavailable')
    return value.AsString()  # None is a known empty value, unlike a missing field.


def _option(element):
    option = element.DesignOption
    return _id(option.Id) if option is not None else -1


def _template(view, doc):
    template_id = view.ViewTemplateId
    if _id(template_id) == -1:
        return None
    template = doc.GetElement(template_id)
    if template is None:
        raise ValueError('Template unavailable')
    return text(template.Name)


def _record(element, fields):
    item = {'id': _id(element.Id)}
    missing = []
    for name, read in [('unique_id', lambda: text(element.UniqueId))] + fields:
        try:
            item[name] = read()
        except Exception:
            missing.append(name)
    if missing:
        item['unreadable_fields'] = sorted(missing)
    return item


def _collect(doc, select, read):
    result = {'status': 'complete', 'items': [], 'unreadable_count': 0,
              'excluded_count': 0, 'diagnostics': []}
    collector = None
    try:
        collector = DB.FilteredElementCollector(doc)
        elements = list(select(collector))
        for element in elements:
            try:
                item = read(element)
                if item is None:
                    result['excluded_count'] += 1
                else:
                    result['items'].append(item)
            except Exception:
                result['unreadable_count'] += 1
                result['status'] = 'partial'
        result['items'].sort(key=lambda item: item['id'])
    except Exception:
        result.update(status='unavailable', diagnostics=['collection_unavailable'])
    finally:
        if collector is not None:
            collector.Dispose()
    return result


def collect(doc):
    """Serialize scalar facts. No transactions, regeneration or linked-model reads."""
    def room(element):
        return _record(element, [
            ('number', lambda: _parameter(element, DB.BuiltInParameter.ROOM_NUMBER)),
            ('phase_id', lambda: _id(element.CreatedPhaseId)),
            ('design_option_id', lambda: _option(element)),
            ('has_location', lambda: element.Location is not None),
            ('area', lambda: float(element.Area))])

    def door(element):
        return _record(element, [
            ('mark', lambda: _parameter(element, DB.BuiltInParameter.ALL_MODEL_MARK))])

    def view(element):
        if element.IsTemplate or isinstance(element, DB.ViewSheet):
            return None
        return _record(element, [
            ('name', lambda: text(element.Name)),
            ('view_type', lambda: text(element.ViewType)),
            ('template_name', lambda: _template(element, doc))])

    def sheet(element):
        return _record(element, [
            ('name', lambda: text(element.Name)),
            ('number', lambda: text(element.SheetNumber)),
            ('is_placeholder', lambda: bool(element.IsPlaceholder))])

    def category(value):
        return lambda collector: collector.OfCategory(value).WhereElementIsNotElementType()

    return {
        'schema_version': 1,
        'scope': 'active_host_document_all_phases_and_design_options_no_links',
        'rooms': _collect(doc, category(DB.BuiltInCategory.OST_Rooms), room),
        'doors': _collect(doc, category(DB.BuiltInCategory.OST_Doors), door),
        'views': _collect(doc, lambda c: c.OfClass(DB.View), view),
        'sheets': _collect(doc, lambda c: c.OfClass(DB.ViewSheet), sheet),
    }
