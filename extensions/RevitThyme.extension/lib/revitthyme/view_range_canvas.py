# -*- coding: utf-8 -*-
"""WPF drawing over cached points only; no model access or document changes."""
from System.Windows import Thickness
from System.Windows.Controls import Canvas, TextBlock
from System.Windows.Media import BrushConverter, DoubleCollection
from System.Windows.Shapes import Line, Rectangle

COLORS = {'top': '#318D8D', 'cut': '#DC754B',
          'bottom': '#5C80B3', 'depth': '#8D73AA'}
NAMES = {'top': 'Top', 'cut': 'Cut Plane', 'bottom': 'Bottom', 'depth': 'View Depth'}


def brush(color):
    return BrushConverter().ConvertFromString(color)


def line(canvas, x1, y1, x2, y2, color, width=1, dashed=False):
    shape = Line(X1=x1, Y1=y1, X2=x2, Y2=y2,
                 Stroke=brush(color), StrokeThickness=width)
    if dashed:
        shape.StrokeDashArray = DoubleCollection([4.0, 3.0])
    canvas.Children.Add(shape)


def label(canvas, text, x, y, color='#687064', size=11):
    node = TextBlock(Text=text, Foreground=brush(color), FontSize=size,
                     Background=brush('#FDFEFB'), Padding=Thickness(3, 1, 3, 1))
    Canvas.SetLeft(node, x)
    Canvas.SetTop(node, y)
    canvas.Children.Add(node)


def band(canvas, x, y, width, height, color):
    shape = Rectangle(Width=max(0, width), Height=max(0, height),
                      Fill=brush(color), Opacity=0.13)
    Canvas.SetLeft(shape, x)
    Canvas.SetTop(shape, y)
    canvas.Children.Add(shape)


def draw_section(canvas, section, planes, levels, unit_factor, unit, view_type='FloorPlan'):
    canvas.Children.Clear()
    width, height = canvas.ActualWidth, canvas.ActualHeight
    if width < 20 or height < 20:
        return
    bounds = section.get('bounds')
    if bounds is None:
        label(canvas, 'No solid intersection at this slice position.', 20, 35)
        return
    elevations = [p['base_elevation'] + p['offset'] for p in planes.values()
                  if not p.get('unlimited')]
    xmin, zmin, xmax, zmax = bounds
    zmin, zmax = min([zmin] + elevations), max([zmax] + elevations)
    xspan, zspan = max(xmax - xmin, 1), max(zmax - zmin, 1)
    scale = min(max(1, width - 155) / xspan, max(1, height - 58) / zspan)
    left = 24 + max(0, (width - 155 - xspan * scale) / 2)
    bottom = height - 26

    def point(horizontal, elevation):
        return left + (horizontal - xmin) * scale, bottom - (elevation - zmin) * scale

    finite = {key: p['base_elevation'] + p['offset'] for key, p in planes.items()
              if not p.get('unlimited')}
    depth_band = ('top', 'depth') if view_type == 'CeilingPlan' else ('depth', 'bottom')
    for lower, upper, color in [('cut', 'top', COLORS['top']),
                               ('bottom', 'cut', COLORS['bottom']),
                               (depth_band[0], depth_band[1], COLORS['depth'])]:
        if lower in finite and upper in finite:
            y1, y2 = point(xmin, finite[lower])[1], point(xmin, finite[upper])[1]
            band(canvas, 16, min(y1, y2), width - 32, abs(y2 - y1), color)
    for level in levels:
        elevation = level['elevation']
        if zmin <= elevation <= zmax:
            y = point(xmin, elevation)[1]
            line(canvas, 16, y, width - 130, y, '#B5BDB0', dashed=True)
            label(canvas, level['name'], 18, y - 17, '#87917F', 10)
    for segment in section.get('segments', []):
        x1, y1 = point(*segment[0])
        x2, y2 = point(*segment[1])
        line(canvas, x1, y1, x2, y2, '#5E675C', 1.25)
    annotations = []
    for key in ('top', 'cut', 'bottom', 'depth'):
        if key not in finite:
            positive = key == 'top' or (key == 'depth' and view_type == 'CeilingPlan')
            annotations.append((6 if positive else height - 38, key, None))
            continue
        y = point(xmin, finite[key])[1]
        line(canvas, 16, y, width - 130, y, COLORS[key], 2.3, key == 'depth')
        annotations.append((max(0, min(height - 38, y - 12)), key, y))
    annotations.sort()
    positions = []
    for desired, key, y in annotations:
        positions.append(max(desired, positions[-1] + 34 if positions else 0))
    overflow = max(0, positions[-1] - (height - 38))
    for (_, key, y), label_y in zip(annotations, positions):
        label_y -= overflow
        text = (NAMES[key] + '\nUnlimited' if y is None else
                '{0}\n{1:g} {2}'.format(NAMES[key], finite[key] * unit_factor, unit))
        if y is not None:
            line(canvas, width - 130, y, width - 123, label_y + 13, COLORS[key])
        label(canvas, text, width - 126, label_y, COLORS[key])


def draw_locator(canvas, geometry, axis, fraction):
    canvas.Children.Clear()
    bounds = geometry.get('bounds')
    if not bounds or canvas.ActualWidth < 20:
        return
    xmin, ymin, unused, xmax, ymax, unused = bounds
    width, height = canvas.ActualWidth, canvas.ActualHeight
    scale = min((width - 16) / max(xmax - xmin, 1),
                (height - 14) / max(ymax - ymin, 1))

    def point(x, y):
        return 8 + (x - xmin) * scale, height - 7 - (y - ymin) * scale

    # Project a bounded sample of actual triangles, rather than inventing a floor plan.
    triangles = geometry.get('triangles', [])
    step = max(1, (len(triangles) + 399) // 400)
    for triangle in triangles[::step]:
        for first, second in ((0, 1), (1, 2), (2, 0)):
            x1, y1 = point(*triangle[first][:2])
            x2, y2 = point(*triangle[second][:2])
            line(canvas, x1, y1, x2, y2, '#C8CFC2', 0.5)
    if axis == 'x':
        cut = xmin + fraction * (xmax - xmin)
        x1, y1 = point(cut, ymin)
        x2, y2 = point(cut, ymax)
    else:
        cut = ymin + fraction * (ymax - ymin)
        x1, y1 = point(xmin, cut)
        x2, y2 = point(xmax, cut)
    line(canvas, x1, y1, x2, y2, '#DC754B', 2.5)
    label(canvas, axis.upper(), 2, 1, '#DC754B', 10)
