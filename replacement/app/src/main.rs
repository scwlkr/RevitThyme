mod contract;
mod state;
use axum::{
    Json, Router,
    extract::{DefaultBodyLimit, State},
    http::{Request, StatusCode},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::post,
};
use contract::*;
use revitthyme_core::{geometry::*, range::*};
use std::sync::{Arc, Mutex};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, BufReader};
use utoipa::{Modify, OpenApi};

#[derive(Clone)]
struct AppState {
    cache: Arc<Mutex<state::Cache>>,
    credential: Arc<String>,
}
type ApiResult<T> = Result<Json<T>, (StatusCode, Json<ApiError>)>;
fn failure(e: ApiError) -> (StatusCode, Json<ApiError>) {
    let status = match e.code.as_str() {
        "stale_snapshot" | "protocol_mismatch" => StatusCode::CONFLICT,
        _ => StatusCode::UNPROCESSABLE_ENTITY,
    };
    (status, Json(e))
}
fn input<T>(
    request: Result<Json<T>, axum::extract::rejection::JsonRejection>,
) -> Result<T, (StatusCode, Json<ApiError>)> {
    request.map(|Json(value)| value).map_err(|_| {
        (
            StatusCode::BAD_REQUEST,
            Json(state::error(
                "malformed_request",
                "Malformed or oversized JSON request.",
                false,
            )),
        )
    })
}
async fn auth(State(app): State<AppState>, req: Request<axum::body::Body>, next: Next) -> Response {
    if req
        .headers()
        .get("authorization")
        .and_then(|v| v.to_str().ok())
        != Some(&format!("Bearer {}", app.credential))
    {
        return (
            StatusCode::UNAUTHORIZED,
            Json(state::error(
                "unauthorized",
                "Application credential required.",
                false,
            )),
        )
            .into_response();
    }
    next.run(req).await
}
#[utoipa::path(post,path="/v1/capture",request_body=CaptureRequest,responses((status=200,body=Snapshot),(status=400,body=ApiError),(status=401,body=ApiError),(status=409,body=ApiError),(status=422,body=ApiError)))]
async fn capture(
    State(app): State<AppState>,
    request: Result<Json<CaptureRequest>, axum::extract::rejection::JsonRejection>,
) -> ApiResult<Snapshot> {
    let request = input(request)?;
    app.cache
        .lock()
        .map_err(|_| {
            failure(state::error(
                "unavailable",
                "Restart the application.",
                true,
            ))
        })?
        .capture(request)
        .map(Json)
        .map_err(failure)
}
#[utoipa::path(post,path="/v1/preview",request_body=PreviewRequest,responses((status=200,body=Preview),(status=400,body=ApiError),(status=401,body=ApiError),(status=409,body=ApiError),(status=422,body=ApiError)))]
async fn preview(
    State(app): State<AppState>,
    request: Result<Json<PreviewRequest>, axum::extract::rejection::JsonRejection>,
) -> ApiResult<Preview> {
    let request = input(request)?;
    app.cache
        .lock()
        .map_err(|_| {
            failure(state::error(
                "unavailable",
                "Restart the application.",
                true,
            ))
        })?
        .preview(&request)
        .map(Json)
        .map_err(failure)
}
#[utoipa::path(post,path="/v1/propose",request_body=PreviewRequest,responses((status=200,body=Proposal),(status=400,body=ApiError),(status=401,body=ApiError),(status=409,body=ApiError),(status=422,body=ApiError)))]
async fn propose(
    State(app): State<AppState>,
    request: Result<Json<PreviewRequest>, axum::extract::rejection::JsonRejection>,
) -> ApiResult<Proposal> {
    let request = input(request)?;
    app.cache
        .lock()
        .map_err(|_| {
            failure(state::error(
                "unavailable",
                "Restart the application.",
                true,
            ))
        })?
        .propose(&request)
        .map(Json)
        .map_err(failure)
}
#[derive(OpenApi)]
#[openapi(
    paths(capture, preview, propose),
    modifiers(&ApplicationAuth),
    security(("applicationCredential" = [])),
    components(schemas(
        Target,
        CaptureRequest,
        Snapshot,
        Mode,
        PreviewRequest,
        Preview,
        Offsets,
        Proposal,
        ApiError,
        Axis,
        Section,
        ViewKind,
        Unit,
        Plane,
        Range,
        Edit,
        Edits
    ))
)]
struct Contract;

struct ApplicationAuth;
impl Modify for ApplicationAuth {
    fn modify(&self, openapi: &mut utoipa::openapi::OpenApi) {
        use utoipa::openapi::security::{HttpAuthScheme, HttpBuilder, SecurityScheme};
        if let Some(components) = openapi.components.as_mut() {
            components.add_security_scheme(
                "applicationCredential",
                SecurityScheme::Http(HttpBuilder::new().scheme(HttpAuthScheme::Bearer).build()),
            );
        }
    }
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    if std::env::args().nth(1).as_deref() == Some("--openapi") {
        println!("{}", Contract::openapi().to_pretty_json()?);
        return Ok(());
    }
    if std::env::args().nth(1).as_deref() != Some("--synthetic") {
        return Err(
            "M1 requires explicit --synthetic; no native adapter exists in this milestone.".into(),
        );
    }
    // Credential arrives on an owned stdin pipe, never argv, environment, logs or renderer.
    let mut input = BufReader::new(tokio::io::stdin());
    let mut credential = String::new();
    BufReader::new((&mut input).take(65))
        .read_line(&mut credential)
        .await?;
    if !credential.ends_with('\n') {
        return Err("Invalid startup frame".into());
    }
    credential.pop();
    if credential.len() != 64 || !credential.chars().all(|c| c.is_ascii_hexdigit()) {
        return Err("Invalid startup channel".into());
    }
    let session = std::env::args().nth(2).ok_or("Missing session identity")?;
    if session.len() > 80
        || !session
            .chars()
            .all(|c| c.is_ascii_alphanumeric() || c == '-')
    {
        return Err("Invalid session identity".into());
    }
    let app = AppState {
        cache: Arc::new(Mutex::new(state::Cache::new(session.clone()))),
        credential: Arc::new(credential),
    };
    let routes = Router::new()
        .route("/v1/capture", post(capture))
        .route("/v1/preview", post(preview))
        .route("/v1/propose", post(propose))
        .layer(DefaultBodyLimit::max(16 * 1024))
        .layer(middleware::from_fn_with_state(app.clone(), auth))
        .with_state(app);
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await?;
    println!(
        "{}",
        serde_json::json!({"protocol":PROTOCOL,"version":env!("CARGO_PKG_VERSION"),"port":listener.local_addr()?.port(),"session":session,"mode":"synthetic"})
    );
    axum::serve(listener, routes)
        .with_graceful_shutdown(async move {
            let _ = (&mut input).take(1).read_u8().await;
        })
        .await?;
    Ok(())
}
