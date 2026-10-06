"""Offline geometry contracts; no Revit runtime or model changes."""
import importlib.util
import math
import unittest
from pathlib import Path
from types import SimpleNamespace as NS

ROOT = Path(__file__).resolve().parents[1]
MODULE = ROOT / 'extensions/RevitThyme.extension/lib/revitthyme/view_range_geometry.py'
SPEC = importlib.util.spec_from_file_location('view_range_geometry', MODULE)
geometry = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(geometry)


def box(x0, y0, z0, x1, y1, z1):
    points = [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0],
              [x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]]
    faces = [(0, 1, 2, 3), (4, 5, 6, 7), (0, 1, 5, 4),
             (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return [[points[a], points[b], points[c]] for a, b, c, d in faces] + [
        [points[a], points[c], points[d]] for a, b, c, d in faces]


def context(triangles):
    points = [point for triangle in triangles for point in triangle]
    bounds = ([min(p[i] for p in points) for i in range(3)] +
              [max(p[i] for p in points) for i in range(3)]) if points else None
    return {'triangles': triangles, 'bounds': bounds, 'diagnostics': []}


class Triangle:
    def __init__(self, points):
        self.points = points

    def get_Vertex(self, index):
        point = self.points[index]
        return NS(X=point[0], Y=point[1], Z=point[2])


class Mesh:
    def __init__(self, triangles):
        self.triangles = triangles
        self.NumTriangles = len(triangles)

    def get_Triangle(self, index):
        return Triangle(self.triangles[index])


class Face:
    def __init__(self, triangles):
        self.triangles = triangles

    def Triangulate(self, detail):
        return Mesh(self.triangles)


class Solid:
    def __init__(self, triangles):
        self.Faces = [Face(triangles)]


class Instance:
    def __init__(self, items):
        self.items = items
        self.calls = 0

    @property
    def Transform(self):
        raise AssertionError('GetInstanceGeometry must not be transformed twice')

    def GetInstanceGeometry(self):
        self.calls += 1
        return self.items


class Element:
    def __init__(self, number, items, category='OST_Walls'):
        self.Id = NS(Value=number)
        self.category = category
        self.items = items
        self.calls = 0

    def get_Geometry(self, options):
        assert not hasattr(options, 'View'), 'Existing plan range must not limit context'
        assert options.ComputeReferences is False
        assert options.IncludeNonVisibleObjects is False
        self.calls += 1
        return self.items


class Document:
    def __init__(self, elements):
        self.elements = {element.Id.Value: element for element in elements}

    def GetElement(self, number):
        return self.elements[number.Value]


class Collector:
    def __init__(self, document):
        self.document = document

    def OfCategory(self, category):
        self.category = category
        return self

    def WhereElementIsNotElementType(self):
        return self

    def ToElementIds(self):
        return [element.Id for element in reversed(list(self.document.elements.values()))
                if element.category == self.category]


DB = NS(BuiltInCategory=NS(**{name: name for name in geometry.CATEGORIES}),
        FilteredElementCollector=Collector, Options=NS, ViewDetailLevel=NS(Medium=2),
        Solid=Solid, Mesh=Mesh, GeometryInstance=Instance)


class SliceTests(unittest.TestCase):
    def test_tetrahedron_true_plane_intersection(self):
        a, b, c, d = [0, 0, 0], [2, 0, 0], [0, 2, 0], [0, 0, 2]
        result = geometry.slice_section(context([[a, b, c], [a, b, d], [a, c, d], [b, c, d]]))
        self.assertEqual(result['cut_position'], 1)
        self.assertEqual(len(result['segments']), 3)
        vertices = {tuple(point) for segment in result['segments'] for point in segment}
        self.assertEqual(vertices, {(0, 0), (1, 0), (0, 1)})

    def test_box_extents_and_stable_canvas_bounds(self):
        data = context(box(10, 20, 30, 12, 24, 36))
        result = geometry.slice_section(data, axis='y', fraction=.25)
        self.assertEqual(result['cut_position'], 21)
        self.assertEqual(result['bounds'], [10, 30, 12, 36])
        for segment in result['segments']:
            for horizontal, elevation in segment:
                self.assertTrue(horizontal in (10, 12) or elevation in (30, 36))

    def test_coplanar_box_face_has_no_triangle_diagonal(self):
        result = geometry.slice_section(context(box(0, 0, 0, 2, 3, 4)), fraction=0)
        self.assertTrue(result['segments'])
        for (x0, z0), (x1, z1) in result['segments']:
            self.assertTrue(x0 == x1 or z0 == z1)

    def test_wall_opening_stays_empty(self):
        # Two jambs and a lintel: a doorway is absent, not a bounding rectangle.
        triangles = box(0, 0, 0, 1, 1, 4) + box(3, 0, 0, 4, 1, 4) + box(1, 0, 3, 3, 1, 4)
        result = geometry.slice_section(context(triangles), axis='y')
        for (x0, z0), (x1, z1) in result['segments']:
            if min(z0, z1) <= 1 <= max(z0, z1):
                self.assertTrue(max(x0, x1) <= 1 or min(x0, x1) >= 3)

    def test_invalid_settings_and_empty_context(self):
        for axis, fraction in [('z', .5), ('x', -1), ('y', 2), ('x', math.nan)]:
            with self.assertRaises(ValueError):
                geometry.slice_section(context([]), axis, fraction)
        self.assertIsNone(geometry.slice_section(context([]))['bounds'])
        with self.assertRaises(ValueError):
            geometry.slice_section({'bounds': [1, 0, 0, 0, 1, 1]})


class ExtractionTests(unittest.TestCase):
    def test_instance_model_coordinates_are_not_transformed_twice(self):
        instance = Instance([Mesh(box(10, 20, 30, 12, 24, 36))])
        element = Element(8, [instance])
        result = geometry.extract_context(Document([element]), DB)
        self.assertEqual(result['bounds'], [10, 20, 30, 12, 24, 36])
        self.assertEqual(result['triangle_count'], 12)
        self.assertEqual(element.calls, 1)
        self.assertEqual(instance.calls, 1)
        self.assertFalse(result['partial'])
        self.assertTrue(geometry.slice_section(result)['segments'])

    def test_category_selection_deterministic_element_budget(self):
        elements = [Element(30, [Solid(box(30, 0, 0, 31, 1, 1))], 'OST_Floors'),
                    Element(10, [Mesh(box(10, 0, 0, 11, 1, 1))]),
                    Element(20, [Mesh(box(20, 0, 0, 21, 1, 1))])]
        result = geometry.extract_context(Document(elements), DB, max_elements=2)
        self.assertEqual(result['bounds'], [10, 0, 0, 21, 1, 1])
        self.assertEqual(result['element_count'], 2)
        self.assertEqual(elements[0].calls, 0)
        self.assertTrue(result['partial'])
        self.assertIn('2 of 3 candidates', '\n'.join(result['diagnostics']))

    def test_triangle_budget_is_bounded_inside_large_mesh(self):
        result = geometry.extract_context(Document([Element(1, [Solid(box(0, 0, 0, 1, 1, 1))])]),
                                          DB, max_triangles=5)
        self.assertEqual(result['triangle_count'], 5)
        self.assertTrue(result['partial'])
        self.assertIn('Triangle budget', '\n'.join(result['diagnostics']))

    def test_invalid_coordinates_are_skipped_and_reported(self):
        good = [[0, 0, 0], [1, 1, 0], [1, 0, 1]]
        bad = [[math.inf, 0, 0], [0, 1, 0], [0, 0, 1]]
        result = geometry.extract_context(Document([Element(1, [Mesh([good, bad])])]), DB)
        self.assertEqual(result['triangle_count'], 1)
        self.assertEqual(result['bounds'], [0, 0, 0, 1, 1, 1])
        self.assertTrue(result['partial'])
        self.assertIn('invalid coordinates', '\n'.join(result['diagnostics']))

    def test_nested_items_have_honest_diagnostics(self):
        item = [Mesh(box(0, 0, 0, 1, 1, 1))]
        for unused in range(10):
            item = [Instance(item)]
        result = geometry.extract_context(Document([Element(1, item)]), DB)
        self.assertEqual(result['triangle_count'], 0)
        self.assertTrue(result['partial'])
        self.assertIn('deeply nested', '\n'.join(result['diagnostics']))

    def test_element_failure_preserves_other_readable_geometry(self):
        class BrokenElement(Element):
            def get_Geometry(self, options):
                raise RuntimeError('Unavailable geometry')
        result = geometry.extract_context(Document([
            BrokenElement(1, []), Element(2, [Mesh(box(0, 0, 0, 1, 1, 1))])]), DB)
        self.assertEqual(result['triangle_count'], 12)
        self.assertTrue(result['partial'])
        self.assertIn('could not be read for 1 items', '\n'.join(result['diagnostics']))

    def test_read_only_scalar_cache_and_budget_validation(self):
        import json
        element = Element(1, [Mesh(box(0, 0, 0, 1, 1, 1))])
        result = geometry.extract_context(Document([element]), DB)
        frozen = json.loads(json.dumps(result))
        self.assertEqual(geometry.slice_section(frozen), geometry.slice_section(result))
        self.assertEqual(element.calls, 1)
        for cap in [0, -1, True, 1.5]:
            with self.assertRaises(ValueError):
                geometry.extract_context(Document([]), DB, max_triangles=cap)


if __name__ == '__main__':
    unittest.main()
