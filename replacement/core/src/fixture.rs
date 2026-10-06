use crate::{
    geometry::Triangle,
    range::{Plane, Range, ViewKind},
};

pub fn box_mesh(min: [f64; 3], max: [f64; 3]) -> Vec<Triangle> {
    let [a, b, c] = min;
    let [x, y, z] = max;
    let p = [
        [a, b, c],
        [x, b, c],
        [x, y, c],
        [a, y, c],
        [a, b, z],
        [x, b, z],
        [x, y, z],
        [a, y, z],
    ];
    let mut triangles = Vec::new();
    for [i, j, k, l] in [
        [0, 1, 2, 3],
        [4, 5, 6, 7],
        [0, 1, 5, 4],
        [1, 2, 6, 5],
        [2, 3, 7, 6],
        [3, 0, 4, 7],
    ] {
        triangles.push([p[i], p[j], p[k]]);
        triangles.push([p[i], p[k], p[l]]);
    }
    triangles
}

/// A wall built around a real void, slabs and a rotated/translated furniture instance.
pub fn house() -> Vec<Triangle> {
    let mut triangles = Vec::new();
    for (min, max) in [
        ([0., 0., 0.], [8., 0.5, 10.]),
        ([12., 0., 0.], [20., 0.5, 10.]),
        ([8., 0., 7.], [12., 0.5, 10.]),
        ([0., 0., -0.5], [20., 16., 0.]),
        ([0., 0., 10.], [20., 16., 10.5]),
        ([0., 15.5, 0.], [20., 16., 10.]),
        ([0., 0., 0.], [0.5, 16., 10.]),
        ([19.5, 0., 0.], [20., 16., 10.]),
    ] {
        triangles.extend(box_mesh(min, max));
    }
    let instance = box_mesh([0., 0., 0.], [2., 3., 2.5]);
    triangles.extend(
        instance
            .into_iter()
            .map(|t| t.map(|[x, y, z]| [14. - y, 6. + x, z])),
    );
    triangles
}
pub fn original(kind: ViewKind) -> Range {
    let plane = |offset, unlimited_allowed| Plane {
        level_id: "4294967301".into(),
        level_name: "Level 1".into(),
        base_feet: 0.,
        offset_feet: offset,
        unlimited: false,
        allow_unlimited: unlimited_allowed,
    };
    Range {
        top: plane(8., true),
        cut: plane(4.000000000123, false),
        bottom: plane(0., true),
        depth: plane(if kind == ViewKind::Ceiling { 10. } else { -1. }, true),
    }
}
