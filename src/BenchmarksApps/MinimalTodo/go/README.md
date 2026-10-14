# Minimal Todo API

A standalone, read-only service using **Go 1.27** and the standard-library
`net/http` router and `encoding/json` serializer. There are no third-party
dependencies, so no `go.sum` is needed. `go.mod` declares `go 1.27` as a
minimum toolchain version (not an exact pin); the Docker builder floats on
the `golang:1.27-bookworm` tag, with no image digest pin.

## Contract

- `GET /todos`: the five fixed Todos, in ID order.
- `GET /todos/1` through `/todos/5`: one Todo.
- `GET /healthz`: the exact UTF-8 body `ready`.
- Invalid/missing IDs and unknown paths: empty `404`.
- Mutating methods on the list or a known item: empty `405`, with `Allow: GET`.

Todo responses are compact JSON with property order `id`, `title`, `dueBy`,
`isComplete`, explicit null dates, and no newline. The records are initialized
once per server in a private, read-only captured array; no code mutates or
exposes that array. Every successful Todo request calls `json.Marshal` on the
typed records. No response caching, compression, conditional responses,
alternate representations, per-request access logs, database, or outbound
service calls are enabled. HTTP/1.1 keep-alive is supported; TLS, HTTP/2, and
a proxy are not required. HEAD, OPTIONS, and trailing-slash aliases are
outside the v1 contract. Noncanonical paths containing duplicate slashes or
dot segments return an empty 404 rather than a router-generated redirect.

## Build and run

Prerequisites: Go **1.27**; a Linux x64 (`amd64`) or ARM64 (`arm64`) host for
native execution. Docker with BuildKit is optional for container builds. The
commands below use a POSIX shell from this project directory.

```sh
export GOTOOLCHAIN=local
mkdir -p bin
CGO_ENABLED=0 go build -trimpath -o bin/todo .
PORT=8080 ./bin/todo
```

`CGO_ENABLED=0` produces a self-contained release executable suitable for the
`ubuntu:26.04` container. Use `Ctrl+C` to stop a foreground process, or send
`kill -TERM <pid>` to its specific PID. Run the binary, not `go run`, for
reliable signal delivery.

```sh
curl --http1.1 --fail http://127.0.0.1:8080/healthz
curl --http1.1 --fail http://127.0.0.1:8080/todos
curl --http1.1 --fail http://127.0.0.1:8080/todos/1
```

`PORT` defaults to `8080` only when unset. A supplied value must consist of
ASCII decimal digits representing `1` through `65535`; an empty value, signs,
spaces, overflow, and out-of-range values fail with an error and exit code 1.
The service binds IPv4 `0.0.0.0` as required. Use a trusted local environment:
direct execution listens on all IPv4 interfaces and provides no
authentication. Container examples below publish only to loopback; nothing is
deployed remotely.

After binding and initializing its handlers, the service prints the exact
line `Application started.` to stdout once. Startup, shutdown, and unexpected
errors are logged to stderr. SIGINT/SIGTERM stop new work and allow active
requests to finish, with an **8-second graceful-shutdown deadline**. If that
expires, the service logs forced connection closure and exits unsuccessfully.
Non-default server limits are a 5-second request header timeout and a
60-second idle keep-alive timeout.

## Linux release targets

```sh
CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -trimpath -o bin/todo-linux-amd64 .
CGO_ENABLED=0 GOOS=linux GOARCH=arm64 go build -trimpath -o bin/todo-linux-arm64 .
```

Cross-compilation is not native execution: run the resulting binary on its
matching native Linux architecture.

## Container

The multi-stage Dockerfile builds without network dependency access (no
third-party Go modules to resolve). `docker build --platform ...` selects a
single target platform for the whole build: BuildKit pulls that platform's
`golang:1.27-bookworm` manifest and runs the build stage natively for it (via
emulation when the host architecture differs) rather than cross-compiling
from one native builder architecture to another -- this is different from
the standalone `GOARCH=...` cross-compilation commands above, which do
cross-compile from a single host. The final `ubuntu:26.04` image contains
only the static release executable, runs as non-root UID/GID 65532, and uses
an exec-form entrypoint for signal delivery. It needs neither a writable
filesystem nor runtime dependencies.

```sh
docker build --platform linux/amd64 -t minimal-todo:amd64 .
docker run --detach --name minimal-todo-local --platform linux/amd64 \
  --user 65532:65532 --read-only --cap-drop ALL \
  --security-opt no-new-privileges \
  --publish 127.0.0.1:8080:8080 minimal-todo:amd64
curl --http1.1 --fail http://127.0.0.1:8080/healthz
curl --http1.1 --fail http://127.0.0.1:8080/todos
docker stop --timeout 10 minimal-todo-local
docker rm minimal-todo-local
```

On a native ARM64 host, use `--platform linux/arm64` instead. Stop/remove
only your own named container. Image acquisition can require internet access
at build time; the running application needs none.

## Files

- `go.mod`, `main.go`, `server.go`: module (minimum Go version, no
  third-party dependencies) and application.
- `Dockerfile`, `.dockerignore`: floating multi-stage Linux container build.

Generated candidate source, admission review, and full test/validation
history for this cohort are owned by the separate
`dotnet-performance-tools` repository.
