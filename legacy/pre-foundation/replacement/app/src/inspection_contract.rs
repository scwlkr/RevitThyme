use crate::{
    contract::{ApiError, Target},
    state::error,
};
use revitthyme_core::health::{Category, ElementRow};
use serde::{Deserialize, Serialize};
use utoipa::{OpenApi, ToSchema};

#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Query {
    pub category: Category,
    /// Last returned numeric ID as a string. Start with "0".
    pub after_id: String,
    #[schema(minimum = 1, maximum = 100)]
    pub limit: u32,
    #[schema(max_length = 128)]
    pub name_contains: String,
    /// Supported for doors only.
    pub missing_mark: bool,
    /// Optional exact IDs; empty means category search.
    #[schema(max_items = 20)]
    pub ids: Vec<String>,
}
impl Query {
    pub fn validate(&self) -> Result<(), ApiError> {
        let canonical = |s: &str, zero: bool| {
            s.parse::<i64>()
                .is_ok_and(|v| (v > 0 || zero && v == 0) && v.to_string() == s)
        };
        if !(1..=100).contains(&self.limit)
            || self.name_contains.chars().count() > 128
            || !canonical(&self.after_id, true)
            || self.ids.len() > 20
            || self.ids.iter().any(|id| !canonical(id, false))
            || self
                .ids
                .iter()
                .collect::<std::collections::HashSet<_>>()
                .len()
                != self.ids.len()
            || self.missing_mark && self.category != Category::Doors
            || !self.ids.is_empty()
                && (!self.name_contains.is_empty()
                    || self.missing_mark
                    || self.category == Category::Warnings)
        {
            return Err(error(
                "invalid_arguments",
                "Use the exact schema, supported filters and canonical positive element IDs.",
                false,
            ));
        }
        Ok(())
    }
}
#[derive(Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct ElementsRequest {
    pub target: Target,
    pub query: Query,
}
#[derive(Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct HealthRequest {
    pub target: Target,
    /// Maximum sample findings per check; counts are independent of this limit.
    #[schema(minimum = 1, maximum = 50)]
    pub limit: u32,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Context {
    pub target: Target,
    pub document_name: String,
    pub view_name: String,
    pub view_kind: String,
    pub is_modified: bool,
    pub is_read_only: bool,
    pub is_workshared: bool,
    pub revit_version: String,
    pub internal_length_unit: String,
}
#[derive(Clone, Debug, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Page {
    pub target: Target,
    pub category: Category,
    pub items: Vec<ElementRow>,
    pub next_after_id: String,
    pub has_more: bool,
    pub scan_complete: bool,
    pub scanned: u32,
    pub missing_ids: Vec<String>,
    pub diagnostics: Vec<String>,
}
#[derive(Clone, Debug, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct ResultData {
    pub context: Option<Context>,
    pub page: Option<Page>,
}
#[derive(OpenApi)]
#[openapi(components(schemas(Query, Category, ElementsRequest, HealthRequest)))]
pub struct Contract;
