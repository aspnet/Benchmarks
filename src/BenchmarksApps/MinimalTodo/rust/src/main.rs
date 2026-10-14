use std::{
    env,
    error::Error,
    future::IntoFuture,
    io::{self, Write},
    process::ExitCode,
    sync::Arc,
    time::Duration,
};

use tokio::{net::TcpListener, sync::Notify};
use tracing::{error, info};

const SHUTDOWN_TIMEOUT: Duration = Duration::from_secs(8);

#[tokio::main]
async fn main() -> ExitCode {
    tracing_subscriber::fmt()
        .with_max_level(tracing::Level::INFO)
        .with_writer(io::stderr)
        .init();

    match run().await {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            error!(%error, "Application failed.");
            ExitCode::FAILURE
        }
    }
}

async fn run() -> Result<(), Box<dyn Error>> {
    let port_value = match env::var("PORT") {
        Ok(value) => Some(value),
        Err(env::VarError::NotPresent) => None,
        Err(error) => return Err(format!("Cannot read PORT: {error}").into()),
    };
    let port = minimal_todo::parse_port(port_value.as_deref())?;
    let signal = shutdown_signal()?;
    let app = minimal_todo::router();
    let listener = TcpListener::bind(("0.0.0.0", port))
        .await
        .map_err(|error| format!("Cannot bind 0.0.0.0:{port}: {error}"))?;
    let shutdown = Arc::new(Notify::new());
    let server = axum::serve(listener, app)
        .with_graceful_shutdown(shutdown.clone().notified_owned())
        .into_future();
    tokio::pin!(server);

    println!("Application started.");
    io::stdout().flush()?;

    tokio::select! {
        result = &mut server => {
            result?;
            return Err("HTTP server stopped unexpectedly".into());
        }
        result = signal => result?,
    }

    info!("Shutdown requested; allowing up to 8 seconds to drain.");
    shutdown.notify_one();
    tokio::time::timeout(SHUTDOWN_TIMEOUT, &mut server)
        .await
        .map_err(|_| "Graceful shutdown timed out after 8 seconds; terminating remaining work")??;
    info!("Application stopped.");
    Ok(())
}

#[cfg(unix)]
fn shutdown_signal() -> io::Result<impl Future<Output = io::Result<()>>> {
    use tokio::signal::unix::{SignalKind, signal};

    let mut terminate = signal(SignalKind::terminate())?;
    let mut interrupt = signal(SignalKind::interrupt())?;
    Ok(async move {
        let received = tokio::select! {
            received = terminate.recv() => received,
            received = interrupt.recv() => received,
        };
        received.ok_or_else(|| io::Error::other("Termination signal stream closed unexpectedly"))
    })
}

#[cfg(windows)]
fn shutdown_signal() -> io::Result<impl Future<Output = io::Result<()>>> {
    use tokio::signal::windows;

    let mut interrupt = windows::ctrl_c()?;
    let mut terminate = windows::ctrl_break()?;
    let mut close = windows::ctrl_close()?;
    Ok(async move {
        let received = tokio::select! {
            received = interrupt.recv() => received,
            received = terminate.recv() => received,
            received = close.recv() => received,
        };
        received.ok_or_else(|| io::Error::other("Termination signal stream closed unexpectedly"))
    })
}
