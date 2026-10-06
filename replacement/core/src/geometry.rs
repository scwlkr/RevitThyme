use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use utoipa::ToSchema;

pub type Triangle = [[f64; 3]; 3];
pub type Segment = [[f64; 2]; 2];
const EPS: f64 = 1e-8;
#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema)]
#[serde(rename_all = "snake_case")]
pub enum Axis {
    X,
    Y,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Section {
    #[schema(schema_with = segment_schema)]
    pub segments: Vec<Segment>,
    #[schema(min_items = 4, max_items = 4)]
    pub bounds_feet: [f64; 4],
    pub position_feet: f64,
}
type Key = [[i64; 2]; 2];
fn segment_schema() -> utoipa::openapi::schema::Array {
    use utoipa::openapi::schema::{ArrayBuilder, ObjectBuilder, Type};
    let point = ArrayBuilder::new()
        .items(ObjectBuilder::new().schema_type(Type::Number))
        .min_items(Some(2))
        .max_items(Some(2));
    let segment = ArrayBuilder::new()
        .items(point)
        .min_items(Some(2))
        .max_items(Some(2));
    ArrayBuilder::new()
        .items(segment)
        .max_items(Some(150_000))
        .build()
}
fn key(segment: Segment) -> Key {
    let mut points = segment.map(|p| p.map(|v| (v / EPS).round() as i64));
    points.sort();
    points
}

/// Inputs are already in project coordinates; native instance transforms are applied exactly once by capture.
pub fn slice(
    triangles: &[Triangle],
    bounds: [f64; 6],
    axis: Axis,
    fraction: f64,
) -> Result<Section, String> {
    if triangles.len() > 50_000
        || !fraction.is_finite()
        || !(0.0..=1.0).contains(&fraction)
        || bounds
            .iter()
            .any(|v| !v.is_finite() || v.abs() > 1_000_000.0)
        || (0..3).any(|i| bounds[i] > bounds[i + 3])
    {
        return Err("Invalid slice or geometry bounds/limit.".into());
    }
    let dimension = match axis {
        Axis::X => 0,
        Axis::Y => 1,
    };
    let horizontal = 1 - dimension;
    let position = bounds[dimension] + fraction * (bounds[dimension + 3] - bounds[dimension]);
    let mut crossing: BTreeMap<Key, Segment> = BTreeMap::new();
    let mut coplanar: BTreeMap<Key, (usize, Segment)> = BTreeMap::new();
    for triangle in triangles {
        if triangle
            .iter()
            .flatten()
            .any(|v| !v.is_finite() || v.abs() > 1_000_000.0)
        {
            return Err("Invalid triangle coordinate.".into());
        }
        let distance = triangle.map(|p| p[dimension] - position);
        if distance.iter().all(|d| *d > EPS) || distance.iter().all(|d| *d < -EPS) {
            continue;
        }
        let project = |p: [f64; 3]| [p[horizontal], p[2]];
        if distance.iter().all(|d| d.abs() <= EPS) {
            for i in 0..3 {
                let segment = [project(triangle[i]), project(triangle[(i + 1) % 3])];
                let entry = coplanar.entry(key(segment)).or_insert((0, segment));
                entry.0 += 1;
            }
        } else {
            let mut points = BTreeMap::new();
            for i in 0..3 {
                let j = (i + 1) % 3;
                if distance[i].abs() <= EPS {
                    let p = project(triangle[i]);
                    points.insert(p.map(|v| (v / EPS).round() as i64), p);
                }
                if (distance[i] > EPS && distance[j] < -EPS)
                    || (distance[i] < -EPS && distance[j] > EPS)
                {
                    let ratio = distance[i] / (distance[i] - distance[j]);
                    let p = [
                        triangle[i][horizontal]
                            + ratio * (triangle[j][horizontal] - triangle[i][horizontal]),
                        triangle[i][2] + ratio * (triangle[j][2] - triangle[i][2]),
                    ];
                    points.insert(p.map(|v| (v / EPS).round() as i64), p);
                }
            }
            if points.len() == 2 {
                let p: Vec<_> = points.into_values().collect();
                crossing.insert(key([p[0], p[1]]), [p[0], p[1]]);
            }
        }
    }
    for (k, (count, segment)) in coplanar {
        if count % 2 == 1 {
            crossing.insert(k, segment);
        }
    }
    crossing.retain(|k, _| k[0] != k[1]);
    Ok(Section {
        segments: crossing.into_values().collect(),
        bounds_feet: [
            bounds[horizontal],
            bounds[2],
            bounds[horizontal + 3],
            bounds[5],
        ],
        position_feet: position,
    })
}
