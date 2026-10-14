use crate::contract::*;
use revitthyme_core::{
    fixture,
    geometry::{self, Triangle},
};
use std::time::{Duration, Instant};

pub struct Cache {
    session: String,
    serial: u32,
    snapshot: Option<(Snapshot, Instant, Vec<Triangle>)>,
}
impl Cache {
    pub fn adopt(&mut self, snapshot: Snapshot, triangles: Vec<Triangle>) -> Result<(), ApiError> {
        if !snapshot.view_kind.accepts(snapshot.plan_direction)
            || triangles.len() > 50_000
            || snapshot.triangle_count as usize != triangles.len()
            || snapshot
                .bounds_feet
                .iter()
                .any(|n| !n.is_finite() || n.abs() > 1_000_001.)
        {
            return Err(error(
                "invalid_capture",
                "Native capture exceeded bounds.",
                true,
            ));
        }
        geometry::slice(&triangles, snapshot.bounds_feet, geometry::Axis::X, 0.5)
            .map_err(|_| error("invalid_capture", "Native coordinates rejected.", true))?;
        snapshot
            .underlay
            .band(snapshot.bounds_feet[2], snapshot.bounds_feet[5])
            .map_err(|m| error("invalid_capture", &m, true))?;
        self.snapshot = Some((snapshot, Instant::now(), triangles));
        Ok(())
    }
    pub fn invalidate(&mut self) {
        self.snapshot = None;
    }
    pub fn new(session: String) -> Self {
        Self {
            session,
            serial: 0,
            snapshot: None,
        }
    }
    pub fn capture(&mut self, request: CaptureRequest) -> Result<Snapshot, ApiError> {
        check_protocol(request.protocol)?;
        if !request.view_kind.accepts(request.plan_direction) {
            return Err(error(
                "invalid_direction",
                "Floor plans look down; ceiling plans look up. Structural plans support either direction.",
                false,
            ));
        }
        self.serial = self
            .serial
            .checked_add(1)
            .ok_or_else(|| error("session_expired", "Restart the application.", true))?;
        let mut triangles = fixture::house();
        if request.partial_fixture {
            triangles.truncate(36);
        }
        use revitthyme_core::range::PlanDirection;
        let underlay = Underlay {
            enabled: !matches!(request.underlay_fixture, UnderlayFixture::None),
            direction: if matches!(
                request.underlay_fixture,
                UnderlayFixture::Up | UnderlayFixture::UnboundedUp
            ) {
                PlanDirection::Up
            } else {
                PlanDirection::Down
            },
            base_level_id: "4294967302".into(),
            base_level_name: "Underlay base".into(),
            base_elevation_feet: 2.,
            top_level_id: "4294967303".into(),
            top_level_name: "Underlay top".into(),
            top_elevation_feet: 6.,
            top_unbounded: matches!(
                request.underlay_fixture,
                UnderlayFixture::UnboundedUp | UnderlayFixture::UnboundedDown
            ),
        };
        let snapshot = Snapshot {
            protocol:PROTOCOL, mode:Mode::Synthetic,
            snapshot_id: format!("{}-{}",self.session,self.serial),
            target: Target { process_id:0,process_start_ticks:"0".into(),session_id:self.session.clone(), document_id:"synthetic-house".into(), view_id:format!("{:?}-plan", request.view_kind), revision:self.serial },
            document_name:"Synthetic courtyard house".into(), view_name:format!("{:?} Plan · Level 1",request.view_kind),
            view_kind:request.view_kind, plan_direction:request.plan_direction,
            underlay,
            original:fixture::original(request.view_kind,request.plan_direction),
            bounds_feet:[0.,0.,-0.5,20.,16.,10.5], triangle_count:triangles.len() as u32,
            partial:request.partial_fixture, diagnostics:vec![
                "Synthetic geometry only. No Revit document is connected.".into(),
                "Section includes model geometry, not final plan visibility. Links, annotations, crop, plan regions, phases and design-option filtering are excluded.".into(),
            ], expires_in_seconds:600, native_write_available:false,
        };
        let mut snapshot = snapshot;
        if snapshot.partial {
            snapshot.diagnostics.push(
                "Partial fixture: triangle limit deliberately omits slabs and furniture.".into(),
            );
        }
        self.snapshot = Some((snapshot.clone(), Instant::now(), triangles));
        Ok(snapshot)
    }
    fn resolve(&self, request: &PreviewRequest) -> Result<(&Snapshot, &[Triangle]), ApiError> {
        check_protocol(request.protocol)?;
        let (snapshot, created, triangles) = self
            .snapshot
            .as_ref()
            .ok_or_else(|| error("no_snapshot", "Capture a fixture first.", true))?;
        if created.elapsed() >= Duration::from_secs(600)
            || request.snapshot_id != snapshot.snapshot_id
            || request.target != snapshot.target
        {
            return Err(error(
                "stale_snapshot",
                "Target, revision or snapshot changed. Refresh before reviewing Apply.",
                true,
            ));
        }
        Ok((snapshot, triangles))
    }
    pub fn preview(&self, request: &PreviewRequest) -> Result<Preview, ApiError> {
        let started = Instant::now();
        let (snapshot, triangles) = self.resolve(request)?;
        let proposed = snapshot
            .original
            .edited(&request.edits, snapshot.plan_direction)
            .map_err(|m| error("invalid_range", &m, false))?;
        let section = geometry::slice(
            triangles,
            snapshot.bounds_feet,
            request.axis,
            request.fraction,
        )
        .map_err(|m| error("invalid_slice", &m, false))?;
        Ok(Preview {
            protocol: PROTOCOL,
            snapshot_id: snapshot.snapshot_id.clone(),
            input_revision: request.input_revision,
            section,
            underlay_bands: snapshot
                .underlay
                .band(snapshot.bounds_feet[2], snapshot.bounds_feet[5])
                .map_err(|m| error("invalid_capture", &m, true))?,
            display_offsets: Offsets {
                top: request.unit.display(proposed.top.offset_feet),
                cut: request.unit.display(proposed.cut.offset_feet),
                bottom: request.unit.display(proposed.bottom.offset_feet),
                depth: request.unit.display(proposed.depth.offset_feet),
            },
            display_limits: [request.unit.display(-5.), request.unit.display(15.)],
            elevations_feet: Offsets {
                top: proposed.top.base_feet + proposed.top.offset_feet,
                cut: proposed.cut.base_feet + proposed.cut.offset_feet,
                bottom: proposed.bottom.base_feet + proposed.bottom.offset_feet,
                depth: proposed.depth.base_feet + proposed.depth.offset_feet,
            },
            proposed,
            diagnostics: snapshot.diagnostics.clone(),
            elapsed_ms: started.elapsed().as_secs_f64() * 1000.,
        })
    }
    pub fn propose(&self, request: &PreviewRequest) -> Result<Proposal, ApiError> {
        let preview = self.preview(request)?;
        let (snapshot, _) = self.resolve(request)?;
        Ok(Proposal { proposal_id:format!("{}-{}",snapshot.snapshot_id,request.input_revision),protocol:PROTOCOL,mode:snapshot.mode,target:snapshot.target.clone(),snapshot_id:snapshot.snapshot_id.clone(),input_revision:request.input_revision,
            before:snapshot.original.clone(),identical:preview.proposed==snapshot.original,after:preview.proposed,
            changed_ids:vec![],side_effects:vec![],native_write_available:snapshot.native_write_available,
            message:"Apply preview only. Native validation and transactions require the M2 .NET adapter and approved M3 qualification. No model has changed.".into() })
    }
}
pub fn error(code: &str, message: &str, refresh: bool) -> ApiError {
    ApiError {
        protocol: PROTOCOL,
        code: code.into(),
        message: message.into(),
        refresh_required: refresh,
    }
}
pub fn check_protocol(protocol: u32) -> Result<(), ApiError> {
    if protocol == PROTOCOL {
        Ok(())
    } else {
        Err(error(
            "protocol_mismatch",
            "Incompatible component protocol. Restart a compatible bundle.",
            true,
        ))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use revitthyme_core::{
        geometry::Axis,
        range::{PlanDirection, Unit, ViewKind},
    };
    fn request(snapshot: &Snapshot) -> PreviewRequest {
        PreviewRequest {
            protocol: 2,
            snapshot_id: snapshot.snapshot_id.clone(),
            target: snapshot.target.clone(),
            input_revision: 1,
            axis: Axis::Y,
            fraction: 0.02,
            unit: Unit::Mm,
            edits: snapshot.original.edits(),
        }
    }
    #[test]
    fn lifetime_target_protocol_and_expiry() {
        let mut cache = Cache::new("owned".into());
        let first = cache
            .capture(CaptureRequest {
                protocol: 2,
                view_kind: ViewKind::Floor,
                plan_direction: PlanDirection::Down,
                underlay_fixture: UnderlayFixture::None,
                partial_fixture: false,
            })
            .unwrap();
        let mut r = request(&first);
        assert!(cache.preview(&r).is_ok());
        r.target.session_id = "other-process".into();
        assert!(cache.preview(&r).is_err());
        r = request(&first);
        r.protocol = 99;
        assert!(cache.preview(&r).is_err());
        r = request(&first);
        cache
            .capture(CaptureRequest {
                protocol: 2,
                view_kind: ViewKind::Ceiling,
                plan_direction: PlanDirection::Up,
                underlay_fixture: UnderlayFixture::None,
                partial_fixture: false,
            })
            .unwrap();
        assert!(cache.propose(&r).is_err());
        let (snapshot, time, _) = cache.snapshot.as_mut().unwrap();
        r = request(snapshot);
        *time = Instant::now() - Duration::from_secs(601);
        assert!(cache.preview(&r).is_err());
    }
    #[test]
    fn review_has_no_write_authority_and_partial_is_visible() {
        let mut cache = Cache::new("fixture".into());
        let s = cache
            .capture(CaptureRequest {
                protocol: 2,
                view_kind: ViewKind::Floor,
                plan_direction: PlanDirection::Down,
                underlay_fixture: UnderlayFixture::None,
                partial_fixture: true,
            })
            .unwrap();
        assert!(s.partial && s.diagnostics.iter().any(|d| d.starts_with("Partial")));
        let before = cache.propose(&request(&s)).unwrap();
        assert!(
            before.identical && before.changed_ids.is_empty() && !before.native_write_available
        );
        let mut r = request(&s);
        r.edits.cut.value = 5.;
        let after = cache.propose(&r).unwrap();
        assert!(!after.identical && after.side_effects.is_empty());
        assert_eq!(cache.propose(&request(&s)).unwrap().before, s.original);
    }
}
