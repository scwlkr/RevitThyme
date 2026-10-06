use crate::range::PlanDirection;
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Underlay {
    pub enabled: bool,
    pub direction: PlanDirection,
    pub base_level_id: String,
    pub base_level_name: String,
    pub base_elevation_feet: f64,
    pub top_level_id: String,
    pub top_level_name: String,
    pub top_elevation_feet: f64,
    pub top_unbounded: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct UnderlayBand {
    pub direction: PlanDirection,
    pub bottom_feet: f64,
    pub top_feet: f64,
    pub top_unbounded: bool,
}
impl Underlay {
    // A section annotates the level band and sight direction, not Revit's plan projection.
    pub fn band(&self, bottom: f64, top: f64) -> Result<Vec<UnderlayBand>, String> {
        if [self.base_elevation_feet, self.top_elevation_feet]
            .iter()
            .any(|v| !v.is_finite() || v.abs() > 1_000_000.)
            || self.enabled
                && !self.top_unbounded
                && self.top_elevation_feet <= self.base_elevation_feet
        {
            return Err("Invalid captured underlay level elevations.".into());
        }
        let low = bottom.max(self.base_elevation_feet);
        let high = if self.top_unbounded {
            top
        } else {
            top.min(self.top_elevation_feet)
        };
        Ok(if self.enabled && low < high {
            vec![UnderlayBand {
                direction: self.direction,
                bottom_feet: low,
                top_feet: high,
                top_unbounded: self.top_unbounded,
            }]
        } else {
            vec![]
        })
    }
}
