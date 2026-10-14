pub use revitthyme_core::underlay::{Underlay, UnderlayBand};
use revitthyme_core::{
    geometry::{Axis, Section},
    range::{Edits, PlanDirection, Range, Unit, ViewKind},
};
use serde::{Deserialize, Serialize};
use utoipa::ToSchema;

pub const PROTOCOL: u32 = 2;
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct Target {
    pub process_id: i32,
    pub process_start_ticks: String,
    pub session_id: String,
    pub document_id: String,
    pub view_id: String,
    #[schema(maximum = 4294967295.0)]
    pub revision: u32,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct CaptureRequest {
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub view_kind: ViewKind,
    /// Used only by the offline fixture. Native capture always reads Revit's type.
    pub plan_direction: PlanDirection,
    pub underlay_fixture: UnderlayFixture,
    pub partial_fixture: bool,
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema)]
#[serde(rename_all = "snake_case")]
pub enum UnderlayFixture {
    None,
    Up,
    Down,
    UnboundedUp,
    UnboundedDown,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Snapshot {
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub mode: Mode,
    pub snapshot_id: String,
    pub target: Target,
    pub document_name: String,
    pub view_name: String,
    pub view_kind: ViewKind,
    pub plan_direction: PlanDirection,
    /// Independent from the main range; underlay visibility is not simulated.
    pub underlay: Underlay,
    pub original: Range,
    #[schema(min_items = 6, max_items = 6)]
    pub bounds_feet: [f64; 6],
    #[schema(maximum = 4294967295.0)]
    pub triangle_count: u32,
    pub partial: bool,
    pub diagnostics: Vec<String>,
    #[schema(maximum = 4294967295.0)]
    pub expires_in_seconds: u32,
    pub native_write_available: bool,
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema)]
#[serde(rename_all = "snake_case")]
pub enum Mode {
    Synthetic,
    Native,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct PreviewRequest {
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub snapshot_id: String,
    pub target: Target,
    #[schema(maximum = 4294967295.0)]
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
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub snapshot_id: String,
    #[schema(maximum = 4294967295.0)]
    pub input_revision: u32,
    pub section: Section,
    #[schema(max_items = 1)]
    pub underlay_bands: Vec<UnderlayBand>,
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
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct Proposal {
    pub proposal_id: String,
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub mode: Mode,
    pub target: Target,
    pub snapshot_id: String,
    #[schema(maximum = 4294967295.0)]
    pub input_revision: u32,
    pub before: Range,
    pub after: Range,
    pub identical: bool,
    pub changed_ids: Vec<String>,
    pub side_effects: Vec<String>,
    pub native_write_available: bool,
    pub message: String,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct RawPlane {
    pub level_id: String,
    pub offset_feet: f64,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(deny_unknown_fields)]
pub struct RawRange {
    pub top: RawPlane,
    pub cut: RawPlane,
    pub bottom: RawPlane,
    pub depth: RawPlane,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct ApplyRequest {
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub request_id: String,
    pub proposal_id: String,
    pub target: Target,
    pub confirmed: bool,
}
#[derive(Clone, Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct OutcomeRequest {
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub request_id: String,
    pub target: Target,
}
#[derive(Clone, Copy, Debug, Serialize, Deserialize, ToSchema, PartialEq)]
#[serde(rename_all = "snake_case")]
pub enum MutationStatus {
    Queued,
    Executing,
    AppliedVerified,
    UnchangedVerified,
    RollbackConfirmed,
    OutcomeUnconfirmed,
    Rejected,
    Cancelled,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct MutationResult {
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub request_id: String,
    pub target: Target,
    pub status: MutationStatus,
    pub code: String,
    pub message: String,
    pub refresh_required: bool,
    #[schema(max_items = 1)]
    pub native_values: Vec<RawRange>,
    pub changed_ids: Vec<String>,
    pub skipped_ids: Vec<String>,
}
#[derive(Debug, Serialize, Deserialize, ToSchema)]
#[serde(deny_unknown_fields)]
pub struct ApiError {
    #[schema(maximum = 4294967295.0)]
    pub protocol: u32,
    pub code: String,
    pub message: String,
    pub refresh_required: bool,
}
