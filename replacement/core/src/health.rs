use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use utoipa::ToSchema;

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize, ToSchema)]
#[serde(rename_all = "snake_case")]
pub enum Category {
    Doors,
    Rooms,
    Views,
    Sheets,
    Levels,
    Walls,
    Floors,
    Ceilings,
    LightingFixtures,
    LightingDevices,
    ElectricalEquipment,
    Warnings,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct ElementRow {
    pub element_id: String,
    pub unique_id: String,
    pub name: String,
    pub category: String,
    pub type_name: String,
    pub family_name: String,
    pub level_id: String,
    pub level_name: String,
    pub mark: Option<String>,
    pub sheet_number: Option<String>,
    pub is_template: bool,
    pub template_eligible: bool,
    pub view_template_id: Option<String>,
    pub view_kind: Option<String>,
    pub placed: Option<bool>,
    pub area_square_feet: Option<f64>,
    pub related_ids: Vec<String>,
}
#[derive(Debug, Serialize)]
pub struct Finding {
    pub element_id: String,
    pub name: String,
    pub level_id: String,
    pub level_name: String,
    pub related_ids: Vec<String>,
}
#[derive(Debug, Serialize)]
pub struct Check {
    pub code: &'static str,
    pub count: usize,
    pub complete: bool,
    pub truncated: bool,
    pub findings: Vec<Finding>,
}
fn check(code: &'static str, rows: Vec<&ElementRow>, complete: bool, limit: usize) -> Check {
    Check {
        code,
        count: rows.len(),
        complete,
        truncated: rows.len() > limit,
        findings: rows
            .into_iter()
            .take(limit)
            .map(|r| Finding {
                element_id: r.element_id.clone(),
                name: r.name.clone(),
                level_id: r.level_id.clone(),
                level_name: r.level_name.clone(),
                related_ids: r.related_ids.clone(),
            })
            .collect(),
    }
}
fn duplicates<'a>(
    rows: &'a [ElementRow],
    key: impl Fn(&'a ElementRow) -> Option<&'a str>,
) -> Vec<&'a ElementRow> {
    let mut groups: BTreeMap<String, Vec<&ElementRow>> = BTreeMap::new();
    for row in rows {
        if let Some(value) = key(row).filter(|s| !s.trim().is_empty()) {
            groups
                .entry(value.trim().to_lowercase())
                .or_default()
                .push(row);
        }
    }
    groups
        .into_values()
        .filter(|g| g.len() > 1)
        .flatten()
        .collect()
}
// Rules live outside Revit. Counts describe affected rows, not duplicate groups.
pub fn evaluate(
    category: Category,
    rows: &[ElementRow],
    complete: bool,
    limit: usize,
) -> Vec<Check> {
    match category {
        Category::Views => vec![
            check(
                "views_missing_templates",
                rows.iter()
                    .filter(|r| {
                        r.template_eligible
                            && !r.is_template
                            && r.view_template_id.as_deref() == Some("-1")
                    })
                    .collect(),
                complete,
                limit,
            ),
            check(
                "duplicate_view_names",
                duplicates(rows, |r| {
                    (!r.is_template && r.view_kind.as_deref() != Some("DrawingSheet"))
                        .then_some(r.name.as_str())
                }),
                complete,
                limit,
            ),
        ],
        Category::Sheets => vec![check(
            "duplicate_sheet_numbers",
            duplicates(rows, |r| r.sheet_number.as_deref()),
            complete,
            limit,
        )],
        Category::Doors => vec![check(
            "doors_missing_marks",
            rows.iter()
                .filter(|r| r.mark.as_deref().is_none_or(|m| m.trim().is_empty()))
                .collect(),
            complete,
            limit,
        )],
        Category::Rooms => vec![check(
            "unplaced_rooms",
            rows.iter().filter(|r| r.placed == Some(false)).collect(),
            complete,
            limit,
        )],
        Category::Warnings => vec![check(
            "revit_warnings",
            rows.iter().collect(),
            complete,
            limit,
        )],
        _ => vec![],
    }
}
