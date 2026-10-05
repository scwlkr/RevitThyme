# -*- coding: utf-8 -*-
from pyrevit import script
from revitthyme.http import register

try:
    register()
except Exception as error:
    script.get_logger().warning('RevitThyme Routes unavailable: %s', error)
