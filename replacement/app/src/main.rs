mod api;
mod contract;
mod native;
mod native_service;
mod state;
use api::{AppState, Contract};
use contract::PROTOCOL;
use std::sync::{Arc, Mutex};
use tokio::io::{AsyncBufReadExt, AsyncReadExt, BufReader};
use utoipa::OpenApi;

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    if std::env::args().nth(1).as_deref() == Some("--openapi") {
        println!("{}", Contract::openapi().to_pretty_json()?);
        return Ok(());
    }
    let mode = std::env::args().nth(1).unwrap_or_default();
    if mode != "--synthetic" && mode != "--native" {
        return Err("Choose explicit --synthetic or owned --native startup.".into());
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
    let latest_revision = Arc::new(std::sync::atomic::AtomicU32::new(0));
    let native = if mode == "--native" {
        let pipe = std::env::args().nth(3).ok_or("Missing pipe")?;
        let process_id: i32 = std::env::args().nth(4).ok_or("Missing PID")?.parse()?;
        let process_start_ticks = std::env::args().nth(5).ok_or("Missing start")?;
        let session_id = std::env::args().nth(6).ok_or("Missing native session")?;
        if process_id <= 0
            || !native::valid_id(&session)
            || !native::valid_id(&session_id)
            || process_start_ticks.len() > 20
            || !process_start_ticks.chars().all(|c| c.is_ascii_digit())
            || pipe != format!("RevitThyme-{process_id}-{session_id}")
        {
            return Err("Invalid native binding".into());
        }
        let mut secret = String::new();
        BufReader::new((&mut input).take(65))
            .read_line(&mut secret)
            .await?;
        if !secret.ends_with('\n') {
            return Err("Invalid native startup frame".into());
        }
        secret.pop();
        if secret.len() != 64 || !secret.chars().all(|c| c.is_ascii_hexdigit()) {
            return Err("Invalid native credential".into());
        }
        let client = native::Client::new(native::Binding {
            pipe,
            process_id,
            process_start_ticks,
            session_id,
            credential: secret,
            connection_id: session.clone(),
        });
        Some(Arc::new(tokio::sync::Mutex::new(
            native_service::Service::new(client, latest_revision.clone()),
        )))
    } else {
        None
    };
    let app = AppState {
        latest_revision,
        native,
        cache: Arc::new(Mutex::new(state::Cache::new(session.clone()))),
        credential: Arc::new(credential),
    };
    let routes = api::router(app);
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await?;
    println!(
        "{}",
        serde_json::json!({"protocol":PROTOCOL,"version":env!("CARGO_PKG_VERSION"),"port":listener.local_addr()?.port(),"session":session,"mode":if mode=="--native"{"native"}else{"synthetic"}})
    );
    axum::serve(listener, routes)
        .with_graceful_shutdown(async move {
            let _ = (&mut input).take(1).read_u8().await;
        })
        .await?;
    Ok(())
}
