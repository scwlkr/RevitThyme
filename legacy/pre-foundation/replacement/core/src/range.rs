use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum ViewKind {
    Floor,
    Engineering,
    Ceiling,
}

#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum PlanDirection {
    Down,
    Up,
}
impl ViewKind {
    pub fn accepts(self, direction: PlanDirection) -> bool {
        self == Self::Engineering
            || direction
                == if self == Self::Ceiling {
                    PlanDirection::Up
                } else {
                    PlanDirection::Down
                }
    }
}

#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum Unit {
    Mm,
    M,
    Ft,
}
impl Unit {
    pub fn factor(self) -> f64 {
        match self {
            Self::Mm => 304.8,
            Self::M => 0.3048,
            Self::Ft => 1.0,
        }
    }
    pub fn display(self, feet: f64) -> f64 {
        feet * self.factor()
    }
    pub fn feet(self, display: f64) -> f64 {
        display / self.factor()
    }
}

#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Plane {
    /// Decimal string: native IDs may exceed 32 bits.
    pub level_id: String,
    pub level_name: String,
    pub base_feet: f64,
    pub offset_feet: f64,
    pub unlimited: bool,
    pub allow_unlimited: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Range {
    pub top: Plane,
    pub cut: Plane,
    pub bottom: Plane,
    pub depth: Plane,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Edit {
    pub value: f64,
    pub unit: Unit,
    pub unlimited: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Edits {
    pub top: Edit,
    pub cut: Edit,
    pub bottom: Edit,
    pub depth: Edit,
}

impl Range {
    pub fn edits(&self) -> Edits {
        let edit = |p: &Plane| Edit {
            value: p.offset_feet,
            unit: Unit::Ft,
            unlimited: p.unlimited,
        };
        Edits {
            top: edit(&self.top),
            cut: edit(&self.cut),
            bottom: edit(&self.bottom),
            depth: edit(&self.depth),
        }
    }
    pub fn edited(&self, edits: &Edits, direction: PlanDirection) -> Result<Self, String> {
        let mut result = self.clone();
        for (name, plane, edit) in [
            ("Top", &mut result.top, &edits.top),
            ("Cut", &mut result.cut, &edits.cut),
            ("Bottom", &mut result.bottom, &edits.bottom),
            ("View Depth", &mut result.depth, &edits.depth),
        ] {
            let feet = edit.unit.feet(edit.value);
            if !feet.is_finite() || !plane.base_feet.is_finite() || feet.abs() > 1_000_000.0 {
                return Err(format!(
                    "{name}: enter a finite offset within 1,000,000 ft."
                ));
            }
            if edit.unlimited && (!plane.allow_unlimited || name == "Cut") {
                return Err(format!("{name}: Unlimited is unsupported."));
            }
            plane.offset_feet = feet;
            plane.unlimited = edit.unlimited;
        }
        let elevation = |p: &Plane, positive: bool| {
            if p.unlimited {
                if positive {
                    f64::INFINITY
                } else {
                    f64::NEG_INFINITY
                }
            } else {
                p.base_feet + p.offset_feet
            }
        };
        let top = elevation(&result.top, true);
        let cut = elevation(&result.cut, true);
        let bottom = elevation(&result.bottom, false);
        let depth = elevation(&result.depth, direction == PlanDirection::Up);
        if top < cut || bottom > cut {
            return Err("Top must be above Cut, and Bottom below Cut.".into());
        }
        if direction == PlanDirection::Up && depth < top {
            return Err("Looking up: View Depth must be at or above Top.".into());
        }
        if direction == PlanDirection::Down && depth > bottom {
            return Err("Looking down: View Depth must be at or below Bottom.".into());
        }
        Ok(result)
    }
}
