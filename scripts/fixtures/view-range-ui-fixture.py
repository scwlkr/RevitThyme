# -*- coding: utf-8 -*-
"""Exercise the real editor with a synthetic cache in standalone native WPF.

Only pyRevit's host-bound forms base is substituted. No Revit API is loaded.
"""
import sys
from System.IO import File, FileMode, Path
from System.Windows import Window, Size, Rect
from System.Windows.Controls import Border, Canvas, TextBlock
from System.Windows.Media import PixelFormats
from System.Windows.Media.Imaging import RenderTargetBitmap, PngBitmapEncoder, BitmapFrame
from System.Windows.Markup import XamlReader

# A standalone IronPython engine has no installed stdlib; the editor only needs paths.
os_module = type(sys)('os')
path_module = type(sys)('os.path')
path_module.join = lambda *values: Path.Combine(*values)
path_module.dirname = Path.GetDirectoryName
os_module.path = path_module
sys.modules['os'] = os_module
sys.modules['os.path'] = path_module
forms_module = type(sys)('pyrevit.forms')


class FixtureWindow(Window):
    def __init__(self, path):
        stream = File.OpenRead(path)
        try:
            source = XamlReader.Load(stream)
        finally:
            stream.Close()
        for name in ('Title', 'Width', 'Height', 'MinWidth', 'MinHeight',
                     'Background', 'FontFamily', 'FontSize', 'Resources'):
            setattr(self, name, getattr(source, name))
        for name in ('view_title', 'unit_picker', 'axis_picker', 'position_slider',
                     'section_canvas', 'locator_canvas', 'reset_button', 'apply_button',
                     'cancel_button', 'geometry_note', 'plane_rows', 'validation_border',
                     'validation_text', 'position_label', 'section_title'):
            setattr(self, name, source.FindName(name))
        content = source.Content
        source.Content = None
        self.Content = Border(Background=self.Background, Child=content)

    def show_dialog(self):
        raise AssertionError('Fixture must not open a real modal window.')


forms_module.WPFWindow = FixtureWindow
pyrevit_module = type(sys)('pyrevit')
pyrevit_module.forms = forms_module
sys.modules['pyrevit'] = pyrevit_module
sys.modules['pyrevit.forms'] = forms_module
sys.path.insert(0, library_path)
from revitthyme.view_range_ui import ViewRangeEditor


def box(triangles, xmin, ymin, zmin, xmax, ymax, zmax):
    points = [(xmin, ymin, zmin), (xmax, ymin, zmin), (xmax, ymax, zmin),
              (xmin, ymax, zmin), (xmin, ymin, zmax), (xmax, ymin, zmax),
              (xmax, ymax, zmax), (xmin, ymax, zmax)]
    for a, b, c, d in [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4),
                       (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]:
        triangles.extend([[points[a], points[b], points[c]],
                          [points[a], points[c], points[d]]])


triangles = []
box(triangles, 0, 0, -0.6, 30, 24, 0)
box(triangles, 0, 0, 0, 0.6, 24, 10)
box(triangles, 29.4, 0, 0, 30, 24, 10)
box(triangles, 0, 23.4, 0, 30, 24, 10)
box(triangles, 0, 0, 0, 30, 0.6, 3)
box(triangles, 0, 0, 8, 30, 0.6, 10)
box(triangles, 0, 0, 3, 8, 0.6, 8)
box(triangles, 22, 0, 3, 30, 0.6, 8)
box(triangles, 0, 0, 10, 30, 24, 10.5)
# Two sloping roof planes, rather than a decorative diagram of a house.
triangles.extend([[(0, -1, 10.5), (30, -1, 10.5), (30, 12, 17)],
                  [(0, -1, 10.5), (30, 12, 17), (0, 12, 17)],
                  [(0, 12, 17), (30, 12, 17), (30, 25, 10.5)],
                  [(0, 12, 17), (30, 25, 10.5), (0, 25, 10.5)]])
levels = [{'id': 11, 'name': 'Ground floor', 'elevation': 0},
          {'id': 12, 'name': 'First floor', 'elevation': 10.5},
          {'id': 13, 'name': 'Roof ridge', 'elevation': 17}]
planes = {}
for key, offset in [('top', 8), ('cut', 4), ('bottom', 0), ('depth', -2)]:
    planes[key] = {'level_id': 11, 'finite_level_id': 11, 'reference_name': 'Ground floor',
                   'base_elevation': 0, 'offset': offset, 'elevation': offset,
                   'unlimited': False, 'allow_unlimited': key != 'cut'}
snapshot = {'view_id': 101, 'view_name': 'Synthetic house fixture (source test)',
            'view_type': 'FloorPlan', 'locked_reason': None, 'levels': levels,
            'planes': planes, 'unit': 'mm',
            'geometry': {'triangles': triangles, 'bounds': [0, -1, -0.6, 30, 25, 17],
                         'diagnostics': ['Synthetic geometry fixture. No Revit model was opened.',
                                         'Native WPF render; Revit integration remains untested.']}}
checks = []


def require(description, condition):
    if not condition:
        raise AssertionError(description)
    checks.append(description)


def layout(window):
    window.Content.Background = window.Background
    window.Content.Measure(Size(1190, 790))
    window.Content.Arrange(Rect(0, 0, 1190, 790))
    window.Content.UpdateLayout()
    window._redraw(None, None)
    window.Content.UpdateLayout()


editor = ViewRangeEditor(snapshot)
layout(editor)
require('Native XAML controls and cached section render',
        editor.section_canvas.Children.Count > 10 and len(editor.rows) == 4)
editor.rows['cut']['slider'].Value = 1524
require('Slider updates offset in internal feet and preserves reference',
        abs(editor.planes['cut']['offset'] - 5) < 1e-9 and editor.planes['cut']['level_id'] == 11)
editor.rows['top']['text'].Text = '1000'
require('Invalid ordering disables Apply', not editor.apply_button.IsEnabled)
editor.rows['top']['text'].Text = 'NaN'
require('Nonfinite typed offset disables Apply', not editor.apply_button.IsEnabled)
editor._reset(None, None)
require('Reset recovers current range and valid Apply', editor.apply_button.IsEnabled)
editor.unit_picker.SelectedIndex = 2
require('Unit conversion changes display without changing feet',
        editor.rows['cut']['text'].Text == '4' and editor.planes['cut']['offset'] == 4)
editor.rows['top']['unlimited'].IsChecked = True
require('Unlimited disables its offset controls',
        editor.planes['top']['unlimited'] and not editor.rows['top']['slider'].IsEnabled)
editor.rows['top']['unlimited'].IsChecked = False
editor.axis_picker.SelectedIndex = 1
editor.position_slider.Value = 30
require('Section controls reslice only cached geometry',
        editor.axis == 'y' and len(editor.section['segments']) > 0)
editor._reset(None, None)
editor.unit_picker.SelectedIndex = 0
editor.axis_picker.SelectedIndex = 0
editor.position_slider.Value = 50
layout(editor)
bitmap = RenderTargetBitmap(1190, 790, 96, 96, PixelFormats.Pbgra32)
bitmap.Render(editor.Content)
encoder = PngBitmapEncoder()
encoder.Frames.Add(BitmapFrame.Create(bitmap))
stream = File.Open(fixture_output, FileMode.Create)
try:
    encoder.Save(stream)
finally:
    stream.Close()
editor.rows['cut']['slider'].Value = 1524
editor._apply(None, None)
require('Explicit Apply returns staged edits without changing input snapshot',
        editor.result['cut']['offset'] == 5 and snapshot['planes']['cut']['offset'] == 4)
cancelled = ViewRangeEditor(snapshot)
cancelled.rows['cut']['slider'].Value = 1828.8
cancelled._cancel(None, None)
require('Cancel discards staged values', cancelled.result is None and
        snapshot['planes']['cut']['offset'] == 4)
locked_snapshot = dict(snapshot, locked_reason='View template controls the range.')
locked = ViewRangeEditor(locked_snapshot)
require('Template-controlled range cannot Apply', not locked.apply_button.IsEnabled)
locked.Close()
precision_planes = dict((key, dict(plane)) for key, plane in planes.items())
precision_planes['cut']['offset'] = 4.123456789
precision = ViewRangeEditor(dict(snapshot, planes=precision_planes))
precision.unit_picker.SelectedIndex = 1
precision.unit_picker.SelectedIndex = 2
require('Changing units preserves offsets beyond displayed precision',
        precision.planes['cut']['offset'] == 4.123456789)
precision.Close()
ceiling_planes = dict((key, dict(plane)) for key, plane in planes.items())
ceiling_planes['depth']['offset'] = 12
ceiling = ViewRangeEditor(dict(snapshot, planes=ceiling_planes, view_type='CeilingPlan'))
layout(ceiling)
require('Ceiling range validates depth above Top and renders correct band',
        ceiling.apply_button.IsEnabled and ceiling.section_canvas.Children.Count > 10)
ceiling.rows['depth']['text'].Text = '1000'
require('Ceiling depth below Top disables Apply', not ceiling.apply_button.IsEnabled)
ceiling.Close()
coincident_planes = dict((key, dict(plane)) for key, plane in planes.items())
coincident_planes['depth']['offset'] = 0
coincident = ViewRangeEditor(dict(snapshot, planes=coincident_planes))
layout(coincident)
positions = [Canvas.GetTop(node) for node in coincident.section_canvas.Children
             if isinstance(node, TextBlock) and
             (node.Text.startswith('Bottom\n') or node.Text.startswith('View Depth\n'))]
require('Coincident Bottom and View Depth labels remain readable',
        len(positions) == 2 and abs(positions[0] - positions[1]) >= 34)
coincident.Close()
print('PASS: {0} native WPF editor checks'.format(len(checks)))
