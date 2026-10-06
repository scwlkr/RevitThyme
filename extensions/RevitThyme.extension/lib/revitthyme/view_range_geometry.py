# -*- coding: utf-8 -*-
"""Read-only model triangulation and portable section slicing, in internal feet.

Call extract_context once in a valid Revit API context. Its result contains only
scalars; slice_section can run from UI events without any Revit API calls.
"""
import math

CATEGORIES = (
    'OST_Walls', 'OST_Floors', 'OST_Roofs', 'OST_Ceilings', 'OST_Doors',
    'OST_Windows', 'OST_Stairs', 'OST_StairsRuns', 'OST_StairsLandings',
    'OST_StructuralFraming', 'OST_StructuralColumns', 'OST_Columns',
    'OST_StructuralFoundation', 'OST_CurtainWallPanels', 'OST_CurtainWallMullions',
    'OST_Railings', 'OST_Ramps', 'OST_GenericModel', 'OST_Furniture',
    'OST_Casework', 'OST_MechanicalEquipment', 'OST_PlumbingFixtures',
    'OST_DuctCurves', 'OST_PipeCurves')
EPSILON = 1.0e-7


def _finite(value):
    number = float(value)
    if math.isnan(number) or math.isinf(number):
        raise ValueError('Non-finite geometry coordinate')
    return number


def _id(value):
    return int(value.Value)


def extract_context(doc, db=None, max_elements=1500, max_triangles=50000):
    """Cache local model mesh with deterministic caps; no active-view collector.

    View-independent geometry deliberately includes other phases/design options
    and hidden categories. Linked models, annotation and symbolic curves are not
    included. It is section context, not Revit's final visibility renderer.
    """
    if db is None:
        from pyrevit import DB as db
    if (isinstance(max_elements, bool) or isinstance(max_triangles, bool) or
            int(max_elements) != max_elements or int(max_triangles) != max_triangles or
            max_elements < 1 or max_triangles < 1):
        raise ValueError('Geometry budgets must be positive integers')
    result = {'triangles': [], 'bounds': None, 'element_count': 0,
              'triangle_count': 0, 'partial': False, 'diagnostics': [
                  'Local model only; linked models, annotation and symbolic curves are excluded.',
                  'Context includes other phases, design options and hidden categories; '
                  'it does not reproduce plan visibility, crop regions or plan regions.']}
    ids, counters = {}, {'errors': 0, 'invalid': 0, 'depth': 0}
    for name in CATEGORIES:
        category = getattr(db.BuiltInCategory, name, None)
        if category is None:
            continue
        try:
            collector = db.FilteredElementCollector(doc).OfCategory(category)
            for value in collector.WhereElementIsNotElementType().ToElementIds():
                ids[_id(value)] = value
        except Exception:
            counters['errors'] += 1
    options = db.Options()
    options.ComputeReferences = False
    options.IncludeNonVisibleObjects = False
    options.DetailLevel = db.ViewDetailLevel.Medium
    ordered = sorted(ids)
    if len(ordered) > max_elements:
        result['partial'] = True
        result['diagnostics'].append('Element budget reached: {0} of {1} candidates.'.format(
            max_elements, len(ordered)))
    for number in ordered[:max_elements]:
        if len(result['triangles']) >= max_triangles:
            result['partial'] = True
            break
        try:
            element = doc.GetElement(ids[number])
            geometry = element.get_Geometry(options)
            result['element_count'] += 1
            _collect(geometry, db, result, counters, max_triangles, 0)
        except Exception:
            counters['errors'] += 1
    result['triangle_count'] = len(result['triangles'])
    if result['partial'] and result['triangle_count'] >= max_triangles:
        result['diagnostics'].append('Triangle budget reached: section context is partial.')
    for key, message in (('errors', 'Geometry could not be read for {0} items.'),
                         ('invalid', 'Skipped {0} triangles with invalid coordinates.'),
                         ('depth', 'Skipped {0} deeply nested geometry instances.')):
        if counters[key]:
            result['partial'] = True
            result['diagnostics'].append(message.format(counters[key]))
    if not result['triangles']:
        result['diagnostics'].append('No triangulated model geometry is available for a section.')
    return result


def _collect(geometry, db, result, counters, cap, depth):
    if geometry is None:
        return
    if depth > 8:
        counters['depth'] += 1
        return
    for item in geometry:
        if len(result['triangles']) >= cap:
            result['partial'] = True
            return
        try:
            if isinstance(item, db.GeometryInstance):
                # Already in model coordinates. Applying Transform again is wrong.
                _collect(item.GetInstanceGeometry(), db, result, counters, cap, depth + 1)
            elif isinstance(item, db.Solid):
                for face in item.Faces:
                    if len(result['triangles']) >= cap:
                        result['partial'] = True
                        break
                    _mesh(face.Triangulate(0.25), result, counters, cap)
            elif isinstance(item, db.Mesh):
                _mesh(item, result, counters, cap)
        except Exception:
            counters['errors'] += 1


def _mesh(mesh, result, counters, cap):
    for index in range(mesh.NumTriangles):
        if len(result['triangles']) >= cap:
            result['partial'] = True
            return
        triangle = mesh.get_Triangle(index)
        try:
            points = []
            for vertex in range(3):
                point = triangle.get_Vertex(vertex)
                points.append([_finite(point.X), _finite(point.Y), _finite(point.Z)])
        except (ValueError, TypeError, OverflowError):
            counters['invalid'] += 1
            continue
        result['triangles'].append(points)
        bounds = result['bounds']
        for point in points:
            if bounds is None:
                bounds = point[:] + point[:]
            else:
                for dimension in range(3):
                    bounds[dimension] = min(bounds[dimension], point[dimension])
                    bounds[dimension + 3] = max(bounds[dimension + 3], point[dimension])
        result['bounds'] = bounds


def _key(points):
    return tuple(sorted(tuple(round(value, 7) for value in point)
                        for point in points))


def _intersection(triangle, dimension, position):
    horizontal = 1 - dimension
    distances = [point[dimension] - position for point in triangle]
    if all(value > EPSILON for value in distances) or all(value < -EPSILON for value in distances):
        return False, []
    project = lambda point: [point[horizontal], point[2]]
    if all(abs(value) <= EPSILON for value in distances):
        return True, [[project(triangle[i]), project(triangle[(i + 1) % 3])]
                      for i in range(3)]
    points = {}
    for i in range(3):
        a, b = triangle[i], triangle[(i + 1) % 3]
        da, db = distances[i], distances[(i + 1) % 3]
        if abs(da) <= EPSILON:
            point = project(a)
            points[_key([point])] = point
        if (da > EPSILON and db < -EPSILON) or (da < -EPSILON and db > EPSILON):
            ratio = da / (da - db)
            point = [a[horizontal] + ratio * (b[horizontal] - a[horizontal]),
                     a[2] + ratio * (b[2] - a[2])]
            points[_key([point])] = point
    values = list(points.values())
    if len(values) == 2:
        return False, [values]
    return False, []


def slice_section(context, axis='x', fraction=0.5):
    """Intersect cached triangles with constant model X or Y, without API access.

    Bounds project the entire cache, keeping the canvas stable while slicing.
    Coplanar triangulation edges cancel in pairs, avoiding diagonal cartoons.
    """
    if axis not in ('x', 'y'):
        raise ValueError('Section axis must be x or y')
    fraction = _finite(fraction)
    if fraction < 0 or fraction > 1:
        raise ValueError('Section fraction must be between 0 and 1')
    diagnostics = list(context.get('diagnostics', []))
    bounds = context.get('bounds')
    if bounds is None:
        return {'segments': [], 'bounds': None, 'cut_position': None,
                'diagnostics': diagnostics}
    bounds = [_finite(value) for value in bounds]
    if len(bounds) != 6 or any(bounds[i] > bounds[i + 3] for i in range(3)):
        raise ValueError('Invalid geometry bounds')
    dimension = 0 if axis == 'x' else 1
    horizontal = 1 - dimension
    position = bounds[dimension] + fraction * (bounds[dimension + 3] - bounds[dimension])
    crossing, coplanar = {}, {}
    for triangle in context.get('triangles', []):
        flat, segments = _intersection(triangle, dimension, position)
        for segment in segments:
            key = _key(segment)
            if key[0] == key[1]:
                continue
            if flat:
                count, previous = coplanar.get(key, (0, segment))
                coplanar[key] = (count + 1, previous)
            else:
                crossing[key] = segment
    for key, (count, segment) in coplanar.items():
        if count % 2:
            crossing[key] = segment
    segments = [crossing[key] for key in sorted(crossing)]
    if not segments:
        diagnostics.append('This section position does not intersect cached model geometry.')
    return {'segments': segments, 'cut_position': position,
            'bounds': [bounds[horizontal], bounds[2], bounds[horizontal + 3], bounds[5]],
            'diagnostics': diagnostics}
