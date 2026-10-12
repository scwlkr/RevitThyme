use crate::{api::Contract, lab::Lab};
use serde::Deserialize;
use serde_json::{Value, json};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use utoipa::OpenApi;

const MAX_INPUT: u64 = 65_537;
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct Call {
    name: String,
    arguments: Value,
}
pub struct Protocol {
    initialized: bool,
    ready: bool,
    writes: bool,
}
pub enum Action {
    Reply(Value),
    Call(String, Value),
    Silent,
}
impl Protocol {
    pub fn new(writes: bool) -> Self {
        Self {
            initialized: false,
            ready: false,
            writes,
        }
    }
    pub fn dispatch(&mut self, request: &Value) -> Action {
        let id = request.get("id").cloned().unwrap_or(Value::Null);
        let has_id = request.get("id").is_some();
        let error = |code, message| {
            Action::Reply(json!({"jsonrpc":"2.0","id":id,"error":{"code":code,"message":message}}))
        };
        if request.get("jsonrpc") != Some(&json!("2.0"))
            || !request.is_object()
            || has_id && !(id.is_string() || id.is_i64() || id.is_u64())
            || request.as_object().is_some_and(|o| {
                o.keys()
                    .any(|k| !["jsonrpc", "id", "method", "params"].contains(&k.as_str()))
            })
        {
            return error(-32600, "Invalid JSON-RPC request");
        }
        let method = request.get("method").and_then(Value::as_str).unwrap_or("");
        if !has_id {
            if method == "notifications/initialized" && self.initialized {
                self.ready = true;
            }
            return Action::Silent;
        }
        let params = request.get("params").cloned().unwrap_or(json!({}));
        let reply = |result| Action::Reply(json!({"jsonrpc":"2.0","id":id,"result":result}));
        match method {
            "initialize" if !self.initialized => {
                let version = params
                    .get("protocolVersion")
                    .and_then(Value::as_str)
                    .unwrap_or("");
                if !params.is_object()
                    || !params.get("capabilities").is_some_and(Value::is_object)
                    || !params.get("clientInfo").is_some_and(Value::is_object)
                {
                    return error(-32602, "Initialization parameters required");
                }
                self.initialized = true;
                reply(
                    json!({"protocolVersion":if ["2025-11-25","2025-06-18","2025-03-26","2024-11-05"].contains(&version){version}else{"2025-11-25"},
                    "capabilities":{"tools":{"listChanged":false}},"serverInfo":{"name":"revitthyme-test","version":"0.1.0"},
                    "instructions":"Dedicated native test session only. Capture its approved fixture before preview/propose. No arbitrary code, save, sync or working-instance fallback."}),
                )
            }
            "ping" => reply(json!({})),
            _ if !self.ready => error(
                -32000,
                "Initialize and send notifications/initialized first",
            ),
            "tools/list" if params == json!({}) => reply(json!({"tools":tools(self.writes)})),
            "tools/call" => match serde_json::from_value::<Call>(params) {
                Ok(call)
                    if call.arguments.is_object()
                        && tools(self.writes).iter().any(|t| t["name"] == call.name) =>
                {
                    Action::Call(call.name, call.arguments)
                }
                _ => error(-32602, "Unknown tool or invalid call parameters"),
            },
            "tools/list" => error(-32602, "Pagination is not supported"),
            _ => error(-32601, "Method not found"),
        }
    }
}
fn tools(writes: bool) -> Vec<Value> {
    let document = serde_json::to_value(Contract::openapi()).expect("Owned OpenAPI schema");
    let mut definitions = document["components"]["schemas"].clone();
    local_refs(&mut definitions);
    let schema = |name: &str| {
        let mut value = definitions[name].clone();
        value["$defs"] = definitions.clone();
        value
    };
    let mut values = vec![
        json!({"name":"lab_capture","description":"Read the exact approved test document and active supported plan through the native Revit API.","inputSchema":{"type":"object","properties":{},"additionalProperties":false},"annotations":{"readOnlyHint":true,"openWorldHint":false}}),
        json!({"name":"view_range_preview","description":"Compute a section and range preview from the captured test model using shared Rust logic.","inputSchema":schema("PreviewRequest"),"annotations":{"readOnlyHint":true,"openWorldHint":false}}),
        json!({"name":"view_range_propose","description":"Validate a proposed range in the approved native test document. Opens no transaction.","inputSchema":schema("PreviewRequest"),"annotations":{"readOnlyHint":true,"openWorldHint":false}}),
        json!({"name":"view_range_outcome","description":"Read the retained native result for an original request and target. Never retry an uncertain mutation.","inputSchema":schema("OutcomeRequest"),"annotations":{"readOnlyHint":true,"openWorldHint":false}}),
        json!({"name":"view_range_cancel","description":"Cancel a queued request before API execution. An executing operation may already have completed.","inputSchema":schema("OutcomeRequest"),"annotations":{"destructiveHint":false,"openWorldHint":false}}),
    ];
    if writes {
        values.push(json!({"name":"view_range_apply","description":"Apply one explicitly confirmed proposal to the approved disposable test model. Native transaction and readback; no save or sync.","inputSchema":schema("ApplyRequest"),"annotations":{"destructiveHint":true,"openWorldHint":false}}));
    }
    values
}
fn local_refs(value: &mut Value) {
    match value {
        Value::Object(map) => {
            for (key, v) in map {
                if key == "$ref" {
                    if let Some(s) = v.as_str() {
                        *v = json!(s.replace("#/components/schemas/", "#/$defs/"));
                    }
                } else {
                    local_refs(v);
                }
            }
        }
        Value::Array(items) => {
            for v in items {
                local_refs(v);
            }
        }
        _ => (),
    }
}
pub async fn serve(lab: &mut Lab) -> Result<(), Box<dyn std::error::Error>> {
    let mut protocol = Protocol::new(lab.binding.allow_writes);
    let mut input = BufReader::new(tokio::io::stdin());
    let mut output = tokio::io::stdout();
    loop {
        let mut bytes = Vec::new();
        let length = (&mut input)
            .take(MAX_INPUT)
            .read_until(b'\n', &mut bytes)
            .await?;
        if length == 0 {
            break;
        }
        if length as u64 >= MAX_INPUT || bytes.last() != Some(&b'\n') {
            return Err("MCP input frame exceeds limit or is incomplete".into());
        }
        let request: Value = match serde_json::from_slice(&bytes) {
            Ok(value) => value,
            Err(_) => {
                output.write_all(b"{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32700,\"message\":\"Parse error\"}}\n").await?;
                output.flush().await?;
                continue;
            }
        };
        let response = match protocol.dispatch(&request) {
            Action::Silent => continue,
            Action::Reply(reply) => reply,
            Action::Call(name, args) => {
                let (value, failed) = match lab.call(&name, args).await {
                    Ok(value) => (value, false),
                    Err(error) => (json!(error), true),
                };
                json!({"jsonrpc":"2.0","id":request["id"],"result":{"content":[{"type":"text","text":value.to_string()}],"structuredContent":value,"isError":failed}})
            }
        };
        let mut bytes = serde_json::to_vec(&response)?;
        bytes.push(b'\n');
        output.write_all(&bytes).await?;
        output.flush().await?;
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    fn ready(writes: bool) -> Protocol {
        let mut p = Protocol::new(writes);
        assert!(matches!(p.dispatch(&json!({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1"}}})),Action::Reply(_)));
        p.dispatch(&json!({"jsonrpc":"2.0","method":"notifications/initialized"}));
        p
    }
    #[test]
    fn lifecycle_and_write_tool_permission() {
        let mut cold = Protocol::new(false);
        match cold.dispatch(&json!({"jsonrpc":"2.0","id":1,"method":"tools/list"})) {
            Action::Reply(v) => assert_eq!(v["error"]["code"], -32000),
            _ => panic!(),
        }
        let mut p = ready(false);
        assert!(matches!(p.dispatch(&json!({"jsonrpc":"2.0","id":"capture","method":"tools/call","params":{"name":"lab_capture","arguments":{}}})),Action::Call(_, _)));
        assert!(matches!(p.dispatch(&json!({"jsonrpc":"2.0","method":"tools/call","params":{"name":"lab_capture","arguments":{}}})),Action::Silent));
        for args in [
            json!({"name":"view_range_apply","arguments":{}}),
            json!({"name":"execute_code","arguments":{}}),
            json!({"name":"lab_capture","arguments":{},"extra":true}),
        ] {
            match p.dispatch(&json!({"jsonrpc":"2.0","id":2,"method":"tools/call","params":args})) {
                Action::Reply(v) => assert_eq!(v["error"]["code"], -32602),
                _ => panic!(),
            }
        }
        assert_eq!(tools(false).len(), 5);
        assert_eq!(tools(true).len(), 6);
        for name in ["PreviewRequest", "ApplyRequest", "OutcomeRequest"] {
            assert!(
                tools(true)
                    .iter()
                    .any(|v| v["inputSchema"]["$defs"][name].is_object())
            );
        }
    }
}
