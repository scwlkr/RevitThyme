use crate::{contract::*, native_service, state};
use axum::{
    Json, Router,
    extract::{DefaultBodyLimit, State},
    http::{Request, StatusCode},
    middleware::{self, Next},
    response::{IntoResponse, Response},
    routing::post,
};
use revitthyme_core::{geometry::*, range::*};
use std::sync::{Arc, Mutex};
use utoipa::{Modify, OpenApi};

#[derive(Clone)]
pub struct AppState {
    pub cache: Arc<Mutex<state::Cache>>,
    pub credential: Arc<String>,
    pub native: Option<Arc<tokio::sync::Mutex<native_service::Service>>>,
    pub latest_revision: Arc<std::sync::atomic::AtomicU32>,
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
    state::check_protocol(request.protocol).map_err(failure)?;
    if let Some(native) = &app.native {
        let (snapshot, triangles) = native.lock().await.capture().await.map_err(failure)?;
        app.cache
            .lock()
            .map_err(|_| failure(state::error("unavailable", "Restart.", true)))?
            .adopt(snapshot.clone(), triangles)
            .map_err(failure)?;
        return Ok(Json(snapshot));
    }
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
    if app.native.is_some() {
        app.latest_revision
            .fetch_max(request.input_revision, std::sync::atomic::Ordering::SeqCst);
    }
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
    if app.native.is_some() {
        app.latest_revision
            .fetch_max(request.input_revision, std::sync::atomic::Ordering::SeqCst);
    }
    let proposal = app
        .cache
        .lock()
        .map_err(|_| {
            failure(state::error(
                "unavailable",
                "Restart the application.",
                true,
            ))
        })?
        .propose(&request)
        .map_err(failure)?;
    if let Some(native) = &app.native {
        return native
            .lock()
            .await
            .propose(proposal)
            .await
            .map(Json)
            .map_err(failure);
    }
    Ok(Json(proposal))
}
#[utoipa::path(post,path="/v1/apply",request_body=ApplyRequest,responses((status=200,body=MutationResult),(status=400,body=ApiError),(status=401,body=ApiError),(status=409,body=ApiError),(status=422,body=ApiError)))]
async fn apply(
    State(app): State<AppState>,
    request: Result<Json<ApplyRequest>, axum::extract::rejection::JsonRejection>,
) -> ApiResult<MutationResult> {
    let request = input(request)?;
    let native = app.native.as_ref().ok_or_else(|| {
        failure(state::error(
            "native_unavailable",
            "Synthetic preview cannot Apply.",
            false,
        ))
    })?;
    let result = native.lock().await.apply(request).await.map_err(failure)?;
    app.cache
        .lock()
        .map_err(|_| failure(state::error("unavailable", "Restart.", true)))?
        .invalidate();
    Ok(Json(result))
}
#[utoipa::path(post,path="/v1/outcome",request_body=OutcomeRequest,responses((status=200,body=MutationResult),(status=400,body=ApiError),(status=401,body=ApiError),(status=409,body=ApiError),(status=422,body=ApiError)))]
async fn outcome(
    State(app): State<AppState>,
    request: Result<Json<OutcomeRequest>, axum::extract::rejection::JsonRejection>,
) -> ApiResult<MutationResult> {
    native_outcome(app, input(request)?, false).await
}
#[utoipa::path(post,path="/v1/cancel",request_body=OutcomeRequest,responses((status=200,body=MutationResult),(status=400,body=ApiError),(status=401,body=ApiError),(status=409,body=ApiError),(status=422,body=ApiError)))]
async fn cancel(
    State(app): State<AppState>,
    request: Result<Json<OutcomeRequest>, axum::extract::rejection::JsonRejection>,
) -> ApiResult<MutationResult> {
    native_outcome(app, input(request)?, true).await
}
async fn native_outcome(
    app: AppState,
    request: OutcomeRequest,
    cancel: bool,
) -> ApiResult<MutationResult> {
    let native = app.native.as_ref().ok_or_else(|| {
        failure(state::error(
            "native_unavailable",
            "Synthetic preview has no mutation outcomes.",
            false,
        ))
    })?;
    native
        .lock()
        .await
        .outcome(request, cancel)
        .await
        .map(Json)
        .map_err(failure)
}
#[derive(OpenApi)]
#[openapi(
    paths(capture, preview, propose, apply, outcome, cancel),
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
        PlanDirection,
        Underlay,
        UnderlayBand,
        UnderlayFixture,
        Unit,
        Plane,
        Range,
        Edit,
        Edits
        ,ApplyRequest,OutcomeRequest,MutationStatus,MutationResult,RawRange,RawPlane
    ))
)]
pub struct Contract;

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

pub fn router(app: AppState) -> Router {
    Router::new()
        .route("/v1/capture", post(capture))
        .route("/v1/preview", post(preview))
        .route("/v1/propose", post(propose))
        .route("/v1/apply", post(apply))
        .route("/v1/outcome", post(outcome))
        .route("/v1/cancel", post(cancel))
        .layer(DefaultBodyLimit::max(16 * 1024))
        .layer(middleware::from_fn_with_state(app.clone(), auth))
        .with_state(app)
}
