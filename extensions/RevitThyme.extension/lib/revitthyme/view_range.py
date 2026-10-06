# -*- coding: utf-8 -*-
"""Modal ribbon workflow: extract once, preview locally, apply explicitly."""
from pyrevit import HOST_APP, forms
from revitthyme import view_range_host, view_range_geometry


def show():
    try:
        snapshot = view_range_host.snapshot(HOST_APP.uiapp)
        snapshot['geometry'] = view_range_geometry.extract_context(HOST_APP.doc)
        from revitthyme.view_range_ui import show_editor
        edits = show_editor(snapshot)
        if edits is None:
            return
        result = view_range_host.apply(HOST_APP.uiapp, snapshot, edits)
        if result['status'] == 'applied':
            forms.alert('View Range applied to {0}.\n\nView ID: {1}\nUse Revit Undo to restore the previous range.'.format(
                snapshot['view_name'], snapshot['view_id']), title='Visual View Range')
        elif result['status'] == 'unchanged':
            forms.alert('The range is unchanged.', title='Visual View Range')
        else:
            message = '\n'.join(result['diagnostics'])
            if result['rollback'] == 'confirmed':
                message += '\n\nThe entire change was rolled back.'
            elif result['rollback'] == 'unconfirmed':
                message += '\n\nRollback could not be confirmed. Inspect the view before continuing.'
            forms.alert(message, title='Visual View Range')
    except Exception as error:
        forms.alert(str(error), title='Visual View Range')
