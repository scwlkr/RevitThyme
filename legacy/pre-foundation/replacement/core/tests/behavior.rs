use revitthyme_core::{
    fixture,
    geometry::{Axis, slice},
    range::{PlanDirection, Unit, ViewKind},
};

#[test]
fn independent_tetrahedron_and_coplanar_edges() {
    let a = [0., 0., 0.];
    let b = [2., 0., 0.];
    let c = [0., 2., 0.];
    let d = [0., 0., 2.];
    let section = slice(
        &[[a, b, c], [a, b, d], [a, c, d], [b, c, d]],
        [0., 0., 0., 2., 2., 2.],
        Axis::X,
        0.5,
    )
    .unwrap();
    let mut vertices: Vec<_> = section.segments.into_iter().flatten().collect();
    vertices.sort_by(|a, b| a.partial_cmp(b).unwrap());
    vertices.dedup();
    assert_eq!(vertices, vec![[0., 0.], [0., 1.], [1., 0.]]);
    let face = slice(
        &fixture::box_mesh([0., 0., 0.], [2., 3., 4.]),
        [0., 0., 0., 2., 3., 4.],
        Axis::X,
        0.,
    )
    .unwrap();
    assert_eq!(face.segments.len(), 4);
    assert!(
        face.segments
            .iter()
            .all(|[a, b]| a[0] == b[0] || a[1] == b[1])
    );
}
#[test]
fn opening_and_transformed_furniture_coordinates() {
    let section = slice(
        &fixture::house(),
        [0., 0., -0.5, 20., 16., 10.5],
        Axis::Y,
        0.015625,
    )
    .unwrap();
    for jamb in [8.0, 12.0] {
        let mut intervals: Vec<_> = section
            .segments
            .iter()
            .filter(|[a, b]| a[0] == jamb && b[0] == jamb)
            .map(|[a, b]| [a[1].min(b[1]), a[1].max(b[1])])
            .collect();
        intervals.sort_by(|a, b| a.partial_cmp(b).unwrap());
        let mut covered = 0.0_f64;
        for [start, end] in intervals {
            assert!(start <= covered);
            covered = covered.max(end);
        }
        assert!(covered >= 7.0);
    }
    for [a, b] in section.segments {
        if a[1].min(b[1]) <= 1. && a[1].max(b[1]) >= 1. {
            assert!(a[0].max(b[0]) <= 8. || a[0].min(b[0]) >= 12.);
        }
    }
    let furniture = fixture::box_mesh([0., 0., 0.], [2., 3., 2.5])
        .into_iter()
        .map(|t| t.map(|[x, y, z]| [14. - y, 6. + x, z]))
        .collect::<Vec<_>>();
    let s = slice(&furniture, [11., 6., 0., 14., 8., 2.5], Axis::Y, 0.5).unwrap();
    let points: Vec<_> = s.segments.into_iter().flatten().collect();
    for corner in [[11., 0.], [14., 0.], [11., 2.5], [14., 2.5]] {
        assert!(points.contains(&corner));
    }
    for p in points {
        assert!((11.0..=14.).contains(&p[0]));
        assert!((0.0..=2.5).contains(&p[1]));
    }
}
#[test]
fn units_precision_level_relative_and_native_rule_prechecks() {
    for unit in [Unit::Mm, Unit::M, Unit::Ft] {
        for feet in [-12.5, 0., 4.000000000123] {
            assert!((unit.feet(unit.display(feet)) - feet).abs() < 1e-13);
        }
    }
    for kind in [ViewKind::Floor, ViewKind::Engineering, ViewKind::Ceiling] {
        let direction = if kind == ViewKind::Ceiling {
            PlanDirection::Up
        } else {
            PlanDirection::Down
        };
        let r = fixture::original(kind, direction);
        assert_eq!(r.edited(&r.edits(), direction).unwrap(), r);
        let mut edits = r.edits();
        edits.cut.unlimited = true;
        assert!(r.edited(&edits, direction).is_err());
        edits = r.edits();
        edits.cut.value = f64::NAN;
        assert!(r.edited(&edits, direction).is_err());
        edits = r.edits();
        edits.depth.value = 2.;
        assert!(r.edited(&edits, direction).is_err());
    }
    let mut r = fixture::original(ViewKind::Floor, PlanDirection::Down);
    r.top.base_feet = 10.;
    r.top.offset_feet = -2.;
    assert!(r.edited(&r.edits(), PlanDirection::Down).is_ok());
    let mut edits = r.edits();
    edits.top.unlimited = true;
    edits.depth.unlimited = true;
    assert!(r.edited(&edits, PlanDirection::Down).is_ok());
}
#[test]
fn structural_look_up_accepts_depth_above_the_primary_range() {
    let mut r = fixture::original(ViewKind::Engineering, PlanDirection::Up);
    r.depth.offset_feet = 10.;
    assert_eq!(r.edited(&r.edits(), PlanDirection::Up).unwrap(), r);
    assert!(r.edited(&r.edits(), PlanDirection::Down).is_err());
    let mut edits = r.edits();
    edits.depth.value = 7.;
    assert!(r.edited(&edits, PlanDirection::Up).is_err());
    edits.depth.unlimited = true;
    assert!(r.edited(&edits, PlanDirection::Up).unwrap().depth.unlimited);
    // A native ceiling range may reference the upper level for Top and Depth.
    r.top.base_feet = 10.;
    r.top.offset_feet = 0.;
    r.depth.base_feet = 10.;
    r.depth.offset_feet = 0.;
    r.cut.offset_feet = 7.5;
    r.bottom.offset_feet = 7.5;
    assert_eq!(r.edited(&r.edits(), PlanDirection::Up).unwrap(), r);
}
#[test]
fn underlay_band_clips_level_bounds_without_changing_view_direction() {
    use revitthyme_core::underlay::Underlay;
    let mut u = Underlay {
        enabled: true,
        direction: PlanDirection::Up,
        base_level_id: "12".into(),
        base_level_name: "Lower".into(),
        base_elevation_feet: 2.,
        top_level_id: "13".into(),
        top_level_name: "Upper".into(),
        top_elevation_feet: 6.,
        top_unbounded: false,
    };
    for direction in [PlanDirection::Up, PlanDirection::Down] {
        u.direction = direction;
        let bands = u.band(-0.5, 10.5).unwrap();
        assert_eq!(
            (bands[0].bottom_feet, bands[0].top_feet, bands[0].direction),
            (2., 6., direction)
        );
        let bands = u.band(3., 5.).unwrap();
        assert_eq!((bands[0].bottom_feet, bands[0].top_feet), (3., 5.));
    }
    u.top_unbounded = true;
    assert_eq!(u.band(-0.5, 10.5).unwrap()[0].top_feet, 10.5);
    u.enabled = false;
    assert!(u.band(-0.5, 10.5).unwrap().is_empty());
    u.enabled = true;
    u.top_unbounded = false;
    u.top_elevation_feet = 1.;
    assert!(u.band(-0.5, 10.5).is_err());
}
#[test]
fn malformed_geometry_and_budgets_are_rejected() {
    assert!(slice(&[], [0.; 6], Axis::X, f64::NAN).is_err());
    assert!(slice(&vec![[[0.; 3]; 3]; 50_001], [0.; 6], Axis::X, 0.5).is_err());
    assert!(slice(&[[[f64::INFINITY; 3]; 3]], [0.; 6], Axis::X, 0.5).is_err());
}
