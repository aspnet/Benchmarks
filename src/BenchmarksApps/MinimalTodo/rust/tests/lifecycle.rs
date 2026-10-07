use std::{process::Stdio, time::Duration};

use axum::http::{Request, StatusCode, Version, header};
use http_body_util::{BodyExt, Empty};
use hyper::{body::Bytes, client::conn::http1};
use hyper_util::rt::TokioIo;
use tokio::{
    io::{AsyncBufReadExt, BufReader, Lines},
    net::{TcpListener, TcpStream},
    process::{Child, ChildStdout, Command},
    time::timeout,
};

const EXPECTED: &[u8] = include_bytes!("fixtures/minimal-todo-v1.expected.json");
const TEST_TIMEOUT: Duration = Duration::from_secs(10);

fn command() -> Command {
    let mut command = Command::new(env!("CARGO_BIN_EXE_minimal-todo"));
    command
        .kill_on_drop(true)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped());
    command
}

async fn start() -> (Child, Lines<BufReader<ChildStdout>>, u16) {
    let reservation = TcpListener::bind(("127.0.0.1", 0)).await.unwrap();
    let port = reservation.local_addr().unwrap().port();
    drop(reservation);
    let mut child = command().env("PORT", port.to_string()).spawn().unwrap();
    let mut stdout = BufReader::new(child.stdout.take().unwrap()).lines();
    let line = timeout(TEST_TIMEOUT, stdout.next_line())
        .await
        .expect("startup timed out")
        .unwrap();
    if line.as_deref() != Some("Application started.") {
        let output = timeout(TEST_TIMEOUT, child.wait_with_output())
            .await
            .unwrap()
            .unwrap();
        panic!(
            "Startup failed: {}",
            String::from_utf8_lossy(&output.stderr)
        );
    }
    (child, stdout, port)
}

async fn assert_live_http(port: u16) {
    timeout(TEST_TIMEOUT, async {
        let stream = TcpStream::connect(("127.0.0.1", port)).await.unwrap();
        let (mut sender, connection) = http1::handshake(TokioIo::new(stream)).await.unwrap();
        let connection = tokio::spawn(connection);
        for (path, expected, content_type) in [
            ("/healthz", b"ready".as_slice(), "text/plain; charset=utf-8"),
            ("/todos", EXPECTED, "application/json"),
            ("/todos", EXPECTED, "application/json"),
        ] {
            let response = sender
                .send_request(
                    Request::builder()
                        .uri(path)
                        .header(header::HOST, format!("127.0.0.1:{port}"))
                        .header(header::ACCEPT_ENCODING, "gzip, br")
                        .body(Empty::<Bytes>::new())
                        .unwrap(),
                )
                .await
                .unwrap();
            assert_eq!(response.status(), StatusCode::OK);
            assert_eq!(response.version(), Version::HTTP_11);
            assert_eq!(response.headers()[header::CONTENT_TYPE], content_type);
            assert!(!response.headers().contains_key(header::CONTENT_ENCODING));
            assert!(!response.headers().contains_key(header::LOCATION));
            assert_eq!(
                response
                    .into_body()
                    .collect()
                    .await
                    .unwrap()
                    .to_bytes()
                    .as_ref(),
                expected
            );
        }
        drop(sender);
        connection.await.unwrap().unwrap();
    })
    .await
    .expect("HTTP/1.1 persistent-connection check timed out");
}

#[tokio::test]
async fn invalid_configuration_exits_unsuccessfully_without_readiness() {
    for port in [
        "", "0", "65536", "-1", "+8080", " 8080", "8080 ", "abc", "8.0",
    ] {
        let output = timeout(TEST_TIMEOUT, command().env("PORT", port).output())
            .await
            .expect("invalid configuration did not exit")
            .unwrap();
        assert!(!output.status.success(), "{port:?}");
        assert!(output.stdout.is_empty(), "{port:?}");
        assert!(
            String::from_utf8_lossy(&output.stderr)
                .contains("PORT must contain only decimal digits"),
            "{port:?}: {}",
            String::from_utf8_lossy(&output.stderr)
        );
    }
}

#[tokio::test]
async fn occupied_port_exits_unsuccessfully_without_readiness() {
    let listener = TcpListener::bind(("0.0.0.0", 0)).await.unwrap();
    let port = listener.local_addr().unwrap().port();
    let output = timeout(
        TEST_TIMEOUT,
        command().env("PORT", port.to_string()).output(),
    )
    .await
    .expect("occupied-port startup did not exit")
    .unwrap();
    assert!(!output.status.success());
    assert!(output.stdout.is_empty());
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(
        stderr.contains(&format!("Cannot bind 0.0.0.0:{port}")),
        "{stderr}"
    );
}

#[tokio::test]
async fn live_http_uses_persistent_connections() {
    let (mut child, _stdout, port) = start().await;
    assert_live_http(port).await;
    // This test checks the transport; Unix signal tests below check graceful exit.
    timeout(TEST_TIMEOUT, child.kill()).await.unwrap().unwrap();
}

#[cfg(unix)]
async fn assert_graceful_exit(signal: nix::sys::signal::Signal) {
    use nix::{sys::signal::kill, unistd::Pid};

    let (child, mut stdout, port) = start().await;
    let _idle_connection = TcpStream::connect(("127.0.0.1", port)).await.unwrap();
    assert_live_http(port).await;
    let pid = Pid::from_raw(i32::try_from(child.id().unwrap()).unwrap());
    let started = std::time::Instant::now();
    kill(pid, signal).unwrap();
    let output = timeout(TEST_TIMEOUT, child.wait_with_output())
        .await
        .expect("graceful exit exceeded 10 seconds; kill-on-drop forced cleanup")
        .unwrap();
    assert!(started.elapsed() < TEST_TIMEOUT);
    assert!(output.status.success(), "exit status: {}", output.status);
    let stderr = String::from_utf8_lossy(&output.stderr);
    assert!(stderr.contains("Shutdown requested"), "{stderr}");
    assert!(stderr.contains("Application stopped."), "{stderr}");
    assert!(!stderr.contains("timed out"), "{stderr}");
    assert!(!stderr.contains("ERROR"), "{stderr}");
    assert!(stdout.next_line().await.unwrap().is_none());
    assert!(TcpStream::connect(("127.0.0.1", port)).await.is_err());
}

#[cfg(unix)]
#[tokio::test]
async fn sigterm_shuts_down_gracefully() {
    assert_graceful_exit(nix::sys::signal::Signal::SIGTERM).await;
}

#[cfg(unix)]
#[tokio::test]
async fn sigint_shuts_down_gracefully() {
    assert_graceful_exit(nix::sys::signal::Signal::SIGINT).await;
}
