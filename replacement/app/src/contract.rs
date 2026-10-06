use revitthyme_core::{
    geometry::{Axis, Section},
    range::{Edits, Range, Unit, ViewKind},
};
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

pub const PROTOCOL: u32 = 1;
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Target {
    pub session_id: String,
    pub document_id: String,
    pub view_id: String,
    pub revision: u32,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct CaptureRequest {
    pub protocol: u32,
    pub view_kind: ViewKind,
    pub partial_fixture: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Snapshot {
    pub protocol: u32,
    pub mode: Mode,
    pub snapshot_id: String,
    pub target: Target,
    pub document_name: String,
    pub view_name: String,
    pub view_kind: ViewKind,
    pub original: Range,
    #[schema(min_items = 6, max_items = 6)]
    pub bounds_feet: [f64; 6],
    pub triangle_count: u32,
    pub partial: bool,
    pub diagnostics: Vec<String>,
    pub expires_in_seconds: u32,
    pub native_write_available: bool,
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema)]
#[serde(rename_all = "snake_case")]
pub enum Mode {
    Synthetic,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct PreviewRequest {
    pub protocol: u32,
    pub snapshot_id: String,
    pub target: Target,
    pub input_revision: u32,
    pub axis: Axis,
    #[schema(minimum = 0, maximum = 1)]
    pub fraction: f64,
    pub unit: Unit,
    pub edits: Edits,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Preview {
    pub protocol: u32,
    pub snapshot_id: String,
    pub input_revision: u32,
    pub section: Section,
    pub proposed: Range,
    pub display_offsets: Offsets,
    #[schema(min_items = 2, max_items = 2)]
    pub display_limits: [f64; 2],
    pub elevations_feet: Offsets,
    pub diagnostics: Vec<String>,
    pub elapsed_ms: f64,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Offsets {
    pub top: f64,
    pub cut: f64,
    pub bottom: f64,
    pub depth: f64,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Proposal {
    pub protocol: u32,
    pub mode: Mode,
    pub target: Target,
    pub snapshot_id: String,
    pub input_revision: u32,
    pub before: Range,
    pub after: Range,
    pub identical: bool,
    pub changed_ids: Vec<String>,
    pub side_effects: Vec<String>,
    pub native_write_available: bool,
    pub message: String,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct ApiError {
    pub protocol: u32,
    pub code: String,
    pub message: String,
    pub refresh_required: bool,
}
