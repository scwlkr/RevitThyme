use crate::{
    contract::*,
    native::{Client, Request},
    state::{check_protocol, error},
};
use revitthyme_core::geometry::Triangle;
use std::collections::HashMap;
pub struct Service {
    client: Client,
    proposal: Option<Proposal>,
    accepted: HashMap<String, ApplyRequest>,
    serial: u64,
    latest_revision: std::sync::Arc<std::sync::atomic::AtomicU32>,
}
impl Service {
    pub fn new(
        client: Client,
        latest_revision: std::sync::Arc<std::sync::atomic::AtomicU32>,
    ) -> Self {
        Self {
            client,
            proposal: None,
            accepted: HashMap::new(),
            serial: 0,
            latest_revision,
        }
    }
    pub async fn capture(&mut self) -> Result<(Snapshot, Vec<Triangle>), ApiError> {
        self.proposal = None;
        self.latest_revision
            .store(0, std::sync::atomic::Ordering::SeqCst);
        let request = self.client.request("capture", None, String::new(), None);
        let reply = self.client.complete_read(request).await?;
        let snapshot = reply
            .capture
            .ok_or_else(|| error(&reply.code, &reply.message, true))?
            .snapshot();
        if snapshot.triangle_count > 50_000 || snapshot.expires_in_seconds != 600 {
            return Err(error(
                "invalid_capture",
                "Native capture limits mismatch.",
                true,
            ));
        }
        let mut triangles = Vec::with_capacity(snapshot.triangle_count as usize);
        for chunk in 0..snapshot.triangle_count.div_ceil(128) {
            let mut request = self.client.request(
                "geometry",
                Some(snapshot.target.clone()),
                snapshot.snapshot_id.clone(),
                None,
            );
            request.chunk_index = chunk;
            let reply = self.client.call(&request).await?;
            let values = reply
                .triangles
                .ok_or_else(|| error(&reply.code, &reply.message, true))?;
            if values.len() > 128 {
                return Err(error("invalid_capture", "Native chunk limit.", true));
            }
            triangles.extend(values);
        }
        Ok((snapshot, triangles))
    }
    pub async fn propose(&mut self, mut proposal: Proposal) -> Result<Proposal, ApiError> {
        self.proposal = None;
        let request = self.client.request(
            "validate",
            Some(proposal.target.clone()),
            proposal.snapshot_id.clone(),
            Some(proposal.after.clone()),
        );
        let reply = self.client.complete_read(request).await?;
        if reply.status != "validated" {
            return Err(error(&reply.code, &reply.message, true));
        }
        if self
            .latest_revision
            .load(std::sync::atomic::Ordering::SeqCst)
            != proposal.input_revision
        {
            return Err(error(
                "stale_proposal",
                "Inputs changed during native validation. Review again.",
                true,
            ));
        }
        self.serial += 1;
        proposal.proposal_id = format!("{}-native-{}", proposal.snapshot_id, self.serial);
        proposal.message="Native validity checked. Confirm only this displayed target and proposal. Apply revalidates immediately before writing; no save or sync.".into();
        proposal.side_effects = if proposal.identical {
            vec![]
        } else {
            vec!["One plan-view range transaction and undo item; source geometry unchanged.".into()]
        };
        self.proposal = Some(proposal.clone());
        Ok(proposal)
    }
    pub async fn apply(&mut self, request: ApplyRequest) -> Result<MutationResult, ApiError> {
        check_protocol(request.protocol)?;
        if !crate::native::valid_id(&request.request_id) || !request.confirmed {
            return Err(error(
                "confirmation_required",
                "Unique request ID and explicit target confirmation required.",
                false,
            ));
        }
        if let Some(prior) = self.accepted.get(&request.request_id) {
            if prior.proposal_id != request.proposal_id || prior.target != request.target {
                return Err(error(
                    "request_id_conflict",
                    "Request ID has another payload.",
                    false,
                ));
            }
            return self
                .outcome(
                    OutcomeRequest {
                        protocol: 2,
                        request_id: request.request_id,
                        target: request.target,
                    },
                    false,
                )
                .await;
        }
        let p = self
            .proposal
            .as_ref()
            .ok_or_else(|| error("stale_proposal", "Review a fresh native proposal.", true))?;
        if p.proposal_id != request.proposal_id
            || p.target != request.target
            || self
                .latest_revision
                .load(std::sync::atomic::Ordering::SeqCst)
                != p.input_revision
        {
            return Err(error(
                "stale_proposal",
                "Displayed proposal or target changed. Review again.",
                true,
            ));
        }
        if self.accepted.len() >= 128 {
            return Err(error(
                "outcomes_full",
                "Restart after resolving retained outcomes; no mutation submitted.",
                false,
            ));
        }
        let native = Request {
            protocol: 2,
            request_id: request.request_id.clone(),
            operation: "apply",
            target: Some(p.target.clone()),
            snapshot_id: p.snapshot_id.clone(),
            range: Some(p.after.clone()),
            chunk_index: 0,
            outcome_id: String::new(),
            confirmed: true,
        };
        self.accepted
            .insert(request.request_id.clone(), request.clone());
        self.proposal = None;
        match self.client.call(&native).await {
            Ok(reply)=>reply.mutation(request.target),
            Err(_)=>Ok(MutationResult {protocol: 2,request_id:request.request_id,target:request.target,status:MutationStatus::OutcomeUnconfirmed,
                code:"lost_response".into(),message:"Apply response unavailable. Inspect this request's outcome and refresh. No automatic retry.".into(),
                refresh_required:true,native_values:vec![],changed_ids:vec![],skipped_ids:vec![]})
        }
    }
    pub async fn outcome(
        &mut self,
        request: OutcomeRequest,
        cancel: bool,
    ) -> Result<MutationResult, ApiError> {
        check_protocol(request.protocol)?;
        if !crate::native::valid_id(&request.request_id) {
            return Err(error(
                "invalid_request",
                "Original request ID required.",
                false,
            ));
        }
        let mut native = self.client.request(
            if cancel { "cancel" } else { "outcome" },
            Some(request.target.clone()),
            String::new(),
            None,
        );
        native.outcome_id = request.request_id.clone();
        let reply = self.client.call(&native).await?;
        // Unknown outcomes echo the query ID; present the original ID to the caller.
        let mut result = reply.mutation(request.target)?;
        result.request_id = request.request_id;
        Ok(result)
    }
}
