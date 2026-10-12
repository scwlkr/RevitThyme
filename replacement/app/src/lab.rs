use crate::{contract::*, native, native_service, state};
use serde::Deserialize;
use serde_json::{Value, json};
use std::sync::{Arc, atomic::AtomicU32};

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Binding {
    pub protocol: u32,
    pub process_id: i32,
    pub process_start_ticks: String,
    pub session_id: String,
    pub pipe: String,
    pub credential_target: String,
    pub fixture_path: String,
    pub allow_writes: bool,
    pub revit_version: String,
}
impl Binding {
    pub fn validate(&self) -> Result<(), Box<dyn std::error::Error>> {
        if self.protocol != 2
            || self.process_id <= 0
            || !native::valid_id(&self.session_id)
            || self.process_start_ticks.is_empty()
            || self.process_start_ticks.len() > 20
            || !self.process_start_ticks.chars().all(|c| c.is_ascii_digit())
            || self.pipe != format!("RevitThymeLab-{}-{}", self.process_id, self.session_id)
            || self.credential_target != self.pipe
            || self.revit_version != "27.3.0.28"
            || !std::path::Path::new(&self.fixture_path).is_absolute()
            || std::path::Path::new(&self.fixture_path)
                .file_name()
                .and_then(|s| s.to_str())
                != Some("RevitThyme-Test.rvt")
        {
            return Err("Invalid native lab binding; no fallback connection is allowed.".into());
        }
        Ok(())
    }
}
pub struct Lab {
    pub binding: Binding,
    native: native_service::Service,
    cache: state::Cache,
    revision: Arc<AtomicU32>,
}
impl Lab {
    pub async fn call(&mut self, name: &str, args: Value) -> Result<Value, ApiError> {
        let bad = || {
            state::error(
                "invalid_arguments",
                "Use the tool's exact input schema.",
                false,
            )
        };
        match name {
            "lab_capture" => {
                if args != json!({}) {
                    return Err(bad());
                }
                self.cache.invalidate();
                let (mut snapshot, triangles) = self.native.capture().await?;
                snapshot.native_write_available = self.binding.allow_writes;
                self.cache.adopt(snapshot.clone(), triangles)?;
                Ok(
                    json!({"snapshot":snapshot,"fixture_path":self.binding.fixture_path,"revit_version":self.binding.revit_version}),
                )
            }
            "view_range_preview" | "view_range_propose" => {
                let request: PreviewRequest = serde_json::from_value(args).map_err(|_| bad())?;
                self.revision
                    .fetch_max(request.input_revision, std::sync::atomic::Ordering::SeqCst);
                if name == "view_range_preview" {
                    return Ok(json!(self.cache.preview(&request)?));
                }
                let proposal = self.cache.propose(&request)?;
                Ok(json!(self.native.propose(proposal).await?))
            }
            "view_range_apply" if self.binding.allow_writes => {
                let request: ApplyRequest = serde_json::from_value(args).map_err(|_| bad())?;
                let result = self.native.apply(request).await?;
                self.cache.invalidate();
                Ok(json!(result))
            }
            "view_range_outcome" | "view_range_cancel" => {
                let request: OutcomeRequest = serde_json::from_value(args).map_err(|_| bad())?;
                Ok(json!(
                    self.native
                        .outcome(request, name == "view_range_cancel")
                        .await?
                ))
            }
            _ => Err(state::error(
                "unknown_tool",
                "Tool is unavailable in this approved lab run.",
                false,
            )),
        }
    }
}
pub async fn run() -> Result<(), Box<dyn std::error::Error>> {
    let path = std::env::args()
        .nth(2)
        .ok_or("Explicit lab binding path required")?;
    if std::env::args().len() != 3 || std::fs::metadata(&path)?.len() > 4096 {
        return Err("Invalid lab startup".into());
    }
    let binding: Binding = serde_json::from_slice(&std::fs::read(path)?)?;
    binding.validate()?;
    let secret = crate::lab_credential::read(&binding.credential_target)?;
    let revision = Arc::new(AtomicU32::new(0));
    // Connection identity is distinct from the native session identity and captures.
    let connection = connection_id();
    let client = native::Client::new(native::Binding {
        pipe: binding.pipe.clone(),
        process_id: binding.process_id,
        process_start_ticks: binding.process_start_ticks.clone(),
        session_id: binding.session_id.clone(),
        credential: secret,
        connection_id: connection.clone(),
    });
    let mut lab = Lab {
        binding,
        native: native_service::Service::new(client, revision.clone()),
        cache: state::Cache::new(connection),
        revision,
    };
    crate::mcp::serve(&mut lab).await
}
fn connection_id() -> String {
    // Windows' system UUID generator; no new dependency or time-based collision risk.
    #[repr(C, align(4))]
    struct GuidBytes([u8; 16]);
    let mut bytes = GuidBytes([0u8; 16]);
    assert_eq!(
        unsafe { CoCreateGuid(bytes.0.as_mut_ptr()) },
        0,
        "Cannot create MCP connection identity"
    );
    let h: String = bytes.0.iter().map(|b| format!("{b:02x}")).collect();
    format!(
        "{}-{}-{}-{}-{}",
        &h[..8],
        &h[8..12],
        &h[12..16],
        &h[16..20],
        &h[20..]
    )
}
#[link(name = "ole32")]
unsafe extern "system" {
    fn CoCreateGuid(value: *mut u8) -> i32;
}
