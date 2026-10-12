# Minimal Todo API

A standalone, read-only service built with **Rust 1.96, Axum 0.8.9, and
Tokio 1.53.1**. Five immutable structured records are initialized once at
startup; Axum's `Json` serializer serializes those records on every
successful request. There is no database, request-path filesystem access,
external service, UI, or mutable business state.

## Build and run

Prerequisites: Rust/Cargo 1.96 and the platform's native C linker. Rustup's
`rust-toolchain.toml` selects the requested compiler (a floating minor
version, not an exact patch pin). Direct dependencies are exactly pinned in
`Cargo.toml`; `Cargo.lock` pins the complete dependency graph. The intended
deployment platforms are native Linux x64 and ARM64 with glibc. Windows x64
is also supported for local development.

```sh
cargo build --release --locked
PORT=8080 ./target/release/minimal-todo
```

PowerShell:

```powershell
$env:PORT = '8080'
.\target\release\minimal-todo.exe
```

`PORT` defaults to `8080`. It must contain only decimal digits and represent a
value from 1 through 65535. Whitespace, signs, empty values, zero, and overflow
are rejected with a diagnostic and a nonzero exit code. The service binds to
`0.0.0.0`; use your local firewall to restrict native-process access. Examples
use loopback only; no automatic deployment or public exposure is configured.

After successful binding and signal-handler initialization, stdout contains
exactly one readiness line:

```text
Application started.
```

```sh
curl --http1.1 -i http://127.0.0.1:8080/healthz
curl --http1.1 -i http://127.0.0.1:8080/todos
curl --http1.1 -i http://127.0.0.1:8080/todos/1
```

`GET /todos` returns all five records in ascending ID order, with exact compact
UTF-8 JSON bytes and property order `id`, `title`, `dueBy`, `isComplete`.
`GET /todos/1` through `/todos/5` return individual records. Missing or malformed
IDs and unrelated paths return empty `404` responses. POST, PUT, PATCH, and
DELETE on `/todos` or known item paths return empty `405` responses.
`GET /healthz` returns plain text `ready`. HTTP/1.1 persistent connections are
supported. Compression, response caching, conditional responses, content
negotiation, HTTPS redirects, and per-request access logging are not enabled.
No runtime network access is needed except serving client requests.

## Shutdown and logging

Use Ctrl+C locally, SIGTERM/SIGINT on Linux, or `docker stop --time 10`.
Windows also handles Ctrl+Break and console-close notifications. The service
stops accepting new connections and gives accepted work up to **8 seconds** to
drain. A timeout is logged as an error and exits unsuccessfully; this leaves
headroom inside the 10-second target. Startup failures, shutdown, and unexpected
errors go to stderr. Normal Tokio worker-thread/runtime defaults are retained;
the explicit drain deadline is the only lifecycle customization.

## Linux containers

The multi-stage build produces a release executable and a minimal
`ubuntu:26.04` runtime with UID/GID 10001. Exec-form entrypoint preserves
signal delivery; one application process runs per container. The image has
no application credentials and does not need a writable filesystem.

On a **native Linux x64** Docker engine:

```sh
docker build --platform linux/amd64 -t minimal-todo:1 .
docker run --rm --name minimal-todo-local --read-only --cap-drop ALL --security-opt no-new-privileges -p 127.0.0.1:8080:8080 minimal-todo:1
# In a second terminal:
curl --http1.1 -i http://127.0.0.1:8080/todos
docker stop --time 10 minimal-todo-local
```

On a **native Linux ARM64** Docker engine, use `--platform linux/arm64`
instead. Both base images are floating multi-architecture tags supporting
x64 and ARM64; the locked Cargo graph still pins the complete dependency
graph exactly.

Generated candidate source, admission review, and full test/validation
history for this cohort are owned by the separate
`dotnet-performance-tools` repository.
