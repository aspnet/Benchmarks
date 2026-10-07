# Minimal Todo API

A standalone, read-only service built with **Rust 1.96.0, Axum 0.8.9, and
Tokio 1.53.1**. Five immutable structured records are initialized once at startup.
Axum's `Json` serializer serializes those records on every successful request;
the golden fixture is only compiled into tests, never read or served by the app.
There is no database, request-path filesystem access, external service, UI, or
mutable business state.

## Build and run

Prerequisites: Rust/Cargo 1.96.0 and the platform's native C linker. Rustup's
`rust-toolchain.toml` selects the requested compiler. Direct dependencies are
exactly pinned in `Cargo.toml`; `Cargo.lock` pins the complete dependency graph.
The intended deployment platforms are native Linux x64 and ARM64 with glibc.
Windows x64 is also supported for local development.

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

## Tests

```sh
cargo fmt --all -- --check
cargo clippy --all-targets --locked -- -D warnings
cargo test --release --locked
```

Tests cover exact list and individual-item bytes, content types, malformed and
missing IDs, mutations, unknown paths, readiness, repeated/concurrent reads,
compression and conditional-header behavior, invalid/occupied ports, and real
HTTP/1.1 requests sharing one TCP connection. On Unix, subprocess tests send both
SIGTERM and SIGINT and require clean exit within 10 seconds, shutdown diagnostics,
and listener closure, including with an idle client connected. Subprocess waits
are bounded; abandoned test children are killed. The transport-only test
deliberately kills its owned child for cleanup; this is separate from the
graceful-termination tests.

## Shutdown and logging

Use Ctrl+C locally, SIGTERM/SIGINT on Linux, or `docker stop --time 10`.
Windows also handles Ctrl+Break and console-close notifications. The service
stops accepting new connections and gives accepted work up to **8 seconds** to
drain. A timeout is logged as an error and exits unsuccessfully; this leaves
headroom inside the 10-second target. Startup failures, shutdown, and unexpected
errors go to stderr. Normal Tokio worker-thread/runtime defaults are retained;
the explicit drain deadline is the only lifecycle customization.

## Linux containers

The multi-stage build produces a release executable and a minimal Debian
runtime with UID/GID 10001. Exec-form entrypoint preserves signal delivery;
one application process runs per container. The image has no application
credentials and does not need a writable filesystem.

On a **native Linux x64** Docker engine:

```sh
docker build --platform linux/amd64 --target test -t minimal-todo:test .
docker build --platform linux/amd64 -t minimal-todo:1 .
docker run --rm --name minimal-todo-local --read-only --cap-drop ALL --security-opt no-new-privileges -p 127.0.0.1:8080:8080 minimal-todo:1
# In a second terminal:
curl --http1.1 -i http://127.0.0.1:8080/todos
docker stop --time 10 minimal-todo-local
```

On a **native Linux ARM64** Docker engine, run the same steps using
`--platform linux/arm64` on both builds. Do not treat an emulated build or run as
native ARM64 validation. Native non-container builds use the same Cargo release
and test commands on each architecture. Both base images are floating
multi-architecture tags supporting x64 and ARM64; the locked Cargo graph still
pins the complete dependency graph exactly.

## Validation in the supplied environment

Executed September 30, 2026:

| Check | Result |
| --- | --- |
| Windows x64 compiler | `rustc 1.96.0 (ac68faa20 2026-05-25)`, LLVM 22.1.2, host `x86_64-pc-windows-msvc` |
| Linux x64 compiler | Same Rust/LLVM versions, host `x86_64-unknown-linux-gnu` |
| Cargo | `1.96.0 (30a34c682 2026-05-25)` on both platforms |
| Application dependencies | Axum 0.8.9, Tokio 1.53.1; native executable, no separately installed language runtime |
| Native Windows checks | Release build, formatting, Clippy with warnings denied, and all 13 applicable tests passed |
| Native Linux x64 checks | Docker test-stage release build and all 15 tests passed, including SIGTERM/SIGINT |
| Offline/non-root tests | All 15 compiled Linux tests passed again with network disabled, read-only filesystem, UID/GID 10001, and all capabilities dropped |
| Runtime image | Linux/amd64 on Docker Engine 29.8.1 / Docker Desktop 4.93.0 (WSL2 x64); Debian glibc `2.36-9+deb12u14` |
| Runtime HTTP smoke | Exact golden list/all five items, readiness, errors, all mutations, no compression/redirect/caching, persistent HTTP/1.1 connection, and 32 concurrent reads passed |
| Runtime shutdown | `docker stop --time 10` exited 0 in 436 ms; shutdown log present, no timeout or forced termination |
| Fixture integrity | Supplied and copied fixture SHA-256 matched: `43f7b08fc8a117d3d1b5b1c863141bf080bbce0d56f7606b95b6c73582bf73ad` |

Windows commands used `cargo +stable` because the already installed `stable`
toolchain was exactly 1.96.0; no global SDK or toolchain was installed. Commands
were `cargo +stable fmt --all -- --check`,
`cargo +stable clippy --all-targets --locked -- -D warnings`,
`cargo +stable test --release --locked`, and
`cargo +stable build --release --locked`. Linux builds used the pinned compiler.
Container build commands were bounded to 10 minutes each.

Local image tags are `minimal-todo-v1-rust-f897238a:test` and
`minimal-todo-v1-rust-f897238a:runtime`; the final runtime image ID is
`sha256:92ec20f14455857aba5af24d985fe38f7516ebca954fb65630e2fda0ab9930d9`.
All owned verification containers were removed; no application was left running.

An initial auxiliary offline test invocation used an executable glob that also
matched the application binary. The tests passed, but the subsequent server run
hit the harness's 120-second deadline and its owned container was forcibly
removed. Rerunning only the exact test executables passed within the 60-second
bound. This was a verification-harness selection error, not an application
graceful-shutdown timeout. The dedicated signal tests and final runtime shutdown
required no forced termination.

**Not executed:** native Linux ARM64 compilation, tests, and container smoke,
because no native ARM64 host/engine is available. No ARM emulation or remote
machine was used. On a native ARM64 host, the remaining steps are:

```sh
cargo build --release --locked
cargo test --release --locked
docker build --platform linux/arm64 --target test -t minimal-todo:arm64-test .
docker build --platform linux/arm64 -t minimal-todo:arm64 .
docker run --name minimal-todo-arm64 --read-only --cap-drop ALL --security-opt no-new-privileges -d -p 127.0.0.1:8080:8080 minimal-todo:arm64
curl --http1.1 -f http://127.0.0.1:8080/healthz
curl --http1.1 -fsS http://127.0.0.1:8080/todos -o todos.actual.json
cmp tests/fixtures/minimal-todo-v1.expected.json todos.actual.json
docker stop --time 10 minimal-todo-arm64
docker inspect --format '{{.State.ExitCode}}' minimal-todo-arm64
docker logs minimal-todo-arm64
docker rm minimal-todo-arm64
rm todos.actual.json
```

Require exit code 0, one readiness line, a clean shutdown log, and completion
within 10 seconds; report any timeout/forced termination. Windows console-signal
delivery was not automated; its local transport and startup-failure checks
passed, while graceful signal delivery was verified on the target Linux x64 OS.
