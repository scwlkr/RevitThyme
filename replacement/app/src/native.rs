use crate::{contract::*, state::error};
use revitthyme_core::{
    geometry::Triangle,
    range::{Range, ViewKind},
};
use serde::{Deserialize, Serialize};
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::windows::named_pipe::{ClientOptions, NamedPipeClient};

#[derive(Clone)]
pub struct Binding {
    pub pipe: String,
    pub process_id: i32,
    pub process_start_ticks: String,
    pub session_id: String,
    pub credential: String,
    pub connection_id: String,
}
#[derive(Serialize)]
pub struct Request {
    pub protocol: u32,
    pub request_id: String,
    pub operation: &'static str,
    pub target: Option<Target>,
    pub snapshot_id: String,
    pub range: Option<Range>,
    pub chunk_index: u32,
    pub outcome_id: String,
    pub confirmed: bool,
}
#[derive(Clone, Debug, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Capture {
    snapshot_id: String,
    target: Target,
    document_name: String,
    view_name: String,
    view_kind: ViewKind,
    original: Range,
    bounds_feet: [f64; 6],
    triangle_count: u32,
    partial: bool,
    diagnostics: Vec<String>,
    expires_in_seconds: u32,
}
impl Capture {
    pub fn snapshot(self) -> Snapshot {
        Snapshot {
            protocol: 1,
            mode: Mode::Native,
            snapshot_id: self.snapshot_id,
            target: self.target,
            document_name: self.document_name,
            view_name: self.view_name,
            view_kind: self.view_kind,
            original: self.original,
            bounds_feet: self.bounds_feet,
            triangle_count: self.triangle_count,
            partial: self.partial,
            diagnostics: self.diagnostics,
            expires_in_seconds: self.expires_in_seconds,
            native_write_available: true,
        }
    }
}
#[derive(Clone, Debug, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Reply {
    pub protocol: u32,
    pub request_id: String,
    pub status: String,
    pub code: String,
    pub message: String,
    pub refresh_required: bool,
    pub capture: Option<Capture>,
    pub triangles: Option<Vec<Triangle>>,
    pub native_values: Option<RawRange>,
    pub changed_ids: Vec<String>,
    pub skipped_ids: Vec<String>,
}
impl Reply {
    pub fn mutation(self, target: Target) -> Result<MutationResult, ApiError> {
        let status =
            serde_json::from_value(serde_json::Value::String(self.status)).map_err(|_| {
                error(
                    "native_protocol",
                    "Unexpected native mutation status.",
                    true,
                )
            })?;
        Ok(MutationResult {
            protocol: 1,
            request_id: self.request_id,
            target,
            status,
            code: self.code,
            message: self.message,
            refresh_required: self.refresh_required,
            native_values: self.native_values.into_iter().collect(),
            changed_ids: self.changed_ids,
            skipped_ids: self.skipped_ids,
        })
    }
}
pub struct Client {
    binding: Binding,
    pipe: Option<NamedPipeClient>,
    serial: u64,
}
impl Client {
    pub fn new(binding: Binding) -> Self {
        Self {
            binding,
            pipe: None,
            serial: 0,
        }
    }
    pub fn request(
        &mut self,
        operation: &'static str,
        target: Option<Target>,
        snapshot_id: String,
        range: Option<Range>,
    ) -> Request {
        self.serial += 1;
        Request {
            protocol: 1,
            request_id: format!("{}{:012x}", &self.binding.connection_id[..24], self.serial),
            operation,
            target,
            snapshot_id,
            range,
            chunk_index: 0,
            outcome_id: String::new(),
            confirmed: false,
        }
    }
    async fn connect(&mut self) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
        if self.pipe.is_some() {
            return Ok(());
        }
        let pipe = ClientOptions::new().open(format!(r"\\.\pipe\{}", self.binding.pipe))?;
        use std::os::windows::io::AsRawHandle;
        let mut server_pid = 0;
        if unsafe { GetNamedPipeServerProcessId(pipe.as_raw_handle(), &mut server_pid) } == 0
            || server_pid != self.binding.process_id as u32
        {
            return Err("Wrong native server process".into());
        }
        self.pipe = Some(pipe);
        let hello = serde_json::json!({"protocol":1,"process_id":self.binding.process_id,"process_start_ticks":self.binding.process_start_ticks,
            "session_id":self.binding.session_id,"credential":self.binding.credential,"connection_id":self.binding.connection_id});
        let ack = self.exchange(&hello).await?;
        if ack
            != serde_json::json!({"protocol":1,"process_id":self.binding.process_id,"process_start_ticks":self.binding.process_start_ticks,"session_id":self.binding.session_id})
        {
            return Err("Native handshake mismatch".into());
        }
        Ok(())
    }
    async fn exchange<T: Serialize>(
        &mut self,
        request: &T,
    ) -> Result<serde_json::Value, Box<dyn std::error::Error + Send + Sync>> {
        let bytes = serde_json::to_vec(request)?;
        if bytes.is_empty() || bytes.len() > 65536 {
            return Err("Frame limit".into());
        }
        let pipe = self.pipe.as_mut().ok_or("Native disconnected")?;
        pipe.write_u32_le(bytes.len() as u32).await?;
        pipe.write_all(&bytes).await?;
        let length = pipe.read_u32_le().await?;
        if length == 0 || length > 65536 {
            return Err("Native frame limit".into());
        }
        let mut bytes = vec![0; length as usize];
        pipe.read_exact(&mut bytes).await?;
        Ok(serde_json::from_slice(&bytes)?)
    }
    pub async fn call(&mut self, request: &Request) -> Result<Reply, ApiError> {
        let result = tokio::time::timeout(Duration::from_secs(8), async {
            self.connect().await?;
            let reply: Reply = serde_json::from_value(self.exchange(request).await?)?;
            if reply.protocol != 1
                || reply.request_id != request.request_id
                    && request.operation != "outcome"
                    && request.operation != "cancel"
            {
                return Err::<Reply, Box<dyn std::error::Error + Send + Sync>>(
                    "Native reply identity mismatch".into(),
                );
            }
            Ok(reply)
        })
        .await;
        match result {
            Ok(Ok(reply)) => Ok(reply),
            _ => {
                self.pipe = None;
                Err(error(
                    "native_disconnected",
                    "Native response unavailable. Query the original Apply outcome and refresh; never automatically retry a mutation.",
                    true,
                ))
            }
        }
    }
    pub async fn complete_read(&mut self, request: Request) -> Result<Reply, ApiError> {
        let mut reply = self.call(&request).await?;
        for _ in 0..150 {
            if !["queued", "executing"].contains(&reply.status.as_str()) {
                return Ok(reply);
            }
            tokio::time::sleep(Duration::from_millis(100)).await;
            let mut query = self.request("outcome", request.target.clone(), String::new(), None);
            query.outcome_id = request.request_id.clone();
            reply = self.call(&query).await?;
        }
        Err(error(
            "native_busy",
            "Revit is busy. Waiting capture/validation expired; refresh when Revit is ready.",
            true,
        ))
    }
}
#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetNamedPipeServerProcessId(pipe: *mut std::ffi::c_void, pid: *mut u32) -> i32;
}
pub fn valid_id(id: &str) -> bool {
    id.len() == 36
        && id.chars().enumerate().all(|(i, c)| {
            if [8, 13, 18, 23].contains(&i) {
                c == '-'
            } else {
                c.is_ascii_hexdigit()
            }
        })
}
