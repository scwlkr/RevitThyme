# -*- coding: utf-8 -*-
import json
from pyrevit import HOST_APP, forms, script
from revitthyme import config
from revitthyme.operations import execute, identity


def show(operation):
    result = execute(operation, HOST_APP.uiapp, {'target': identity(HOST_APP.uiapp)})
    output = script.get_output()
    output.set_title('RevitThyme - ' + operation)
    print(json.dumps(result, indent=2))


def settings():
    root = forms.pick_folder(title='Select the TimberFold project folder')
    if root:
        try:
            path = config.configure(root)
            forms.alert('TimberFold inspection configured.\n\n' + path, title='RevitThyme')
        except Exception as error:
            forms.alert(str(error), title='RevitThyme settings')
