# Minimal Todo API

A standalone, read-only service using **Go 1.27** and the standard-library
`net/http` router and `encoding/json` serializer. There are no third-party
dependencies, so no `go.sum` is needed. The module declares the requested Go
version; the Docker builder pins that version and its image digest.

## Contract

- `GET /todos`: the five fixed Todos, in ID order.
- `GET /todos/1` through `/todos/5`: one Todo.
- `GET /healthz`: the exact UTF-8 body `ready`.
- Invalid/missing IDs and unknown paths: empty `404`.
- Mutating methods on the list or a known item: empty `405`, with `Allow: GET`.

Todo responses are compact JSON with property order `id`, `title`, `dueBy`,
`isComplete`, explicit null dates, and no newline. The records are initialized
once per server in a private, read-only captured array. Go has no const collection
type; no code mutates or exposes that array. Every successful Todo request calls
`json.Marshal` on the typed records. The golden fixture is **only read by tests**.
No response caching, compression, conditional responses, alternate representations,
per-request access logs, database, or outbound service calls are enabled.
HTTP/1.1 keep-alive is supported. TLS, HTTP/2, and a proxy are not required.
HEAD, OPTIONS, and trailing-slash aliases are outside the v1 contract.
Noncanonical paths containing duplicate slashes or dot segments return an empty
404 rather than a router-generated redirect.

## Build and run locally

Prerequisites: Go **1.27**; a Linux x64 (`amd64`) or ARM64 (`arm64`) host for
native execution. Docker with BuildKit is optional for container builds.
The commands below use a POSIX shell from this project directory.

```sh
export GOTOOLCHAIN=local
go version
mkdir -p bin
CGO_ENABLED=0 go build -trimpath -o bin/todo .
PORT=8080 ./bin/todo
```

Go's normal `go build` is optimized; no debug build flags or runtime tuning are
used. `CGO_ENABLED=0` produces a self-contained release executable suitable for
the ubuntu:26.04 container. Use `Ctrl+C` to stop a foreground process, or send
`kill -TERM <pid>` to its specific PID. Run the binary, not `go run`, for reliable
signal delivery.

```sh
curl --http1.1 --fail http://127.0.0.1:8080/healthz
curl --http1.1 --fail http://127.0.0.1:8080/todos
curl --http1.1 --fail http://127.0.0.1:8080/todos/1
```

`PORT` defaults to `8080` only when unset. A supplied value must consist of ASCII
decimal digits representing `1` through `65535`; an empty value, signs, spaces,
overflow, and out-of-range values fail with an error and exit code 1. Leading
zeroes in **ports** are accepted; leading zeroes in **Todo IDs** are not.
The service binds IPv4 `0.0.0.0` as required. Use a trusted local environment:
direct execution listens on all IPv4 interfaces and provides no authentication.
Container examples below publish only to loopback; nothing is deployed remotely.

After binding and initializing its handlers, the service prints the exact line
`Application started.` to stdout once. Startup, shutdown, and unexpected errors
are logged to stderr. SIGINT/SIGTERM stop new work and allow active requests to
finish, with an **8-second graceful-shutdown deadline**. If that expires, the
service logs forced connection closure and exits unsuccessfully. A second signal
uses the OS default behavior. Non-default server limits are a 5-second request
header timeout and a 60-second idle keep-alive timeout; these limit stalled
connections without changing the response representation.

## Tests

```sh
go test -count=1 -timeout=120s ./...
go vet ./...
go test -race -count=1 -timeout=120s ./...
TODO_TEST_BINARY="$PWD/bin/todo" go test -count=1 -timeout=120s \
  -run 'Test(InvalidPortStartup|OccupiedPortStartup|ProcessLifecycle)$' .
```

The race detector additionally needs a supported host architecture and C compiler.
Tests check the exact supplied fixture bytes, all five items, malformed/missing
IDs, unknown paths, every required mutating method, readiness, no redirect,
compression/caching/negotiation suppression, concurrent and repeated immutable
reads, explicit error logging, port validation, occupied-port failure, persistent
HTTP/1.1 connections, and SIGINT/SIGTERM shutdown within 10 seconds. The lifecycle
tests normally launch the same server entry point in a bounded test subprocess;
`TODO_TEST_BINARY` instead exercises the separately built release executable.
Signal tests are skipped on Windows, where POSIX signal delivery is unavailable.

## Linux release targets

```sh
CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build -trimpath -o bin/todo-linux-amd64 .
CGO_ENABLED=0 GOOS=linux GOARCH=arm64 go build -trimpath -o bin/todo-linux-arm64 .
```

Cross-compilation is not native execution. On **each matching physical/native
Linux architecture**, run the test commands above, build `bin/todo`, and exercise
its start/request/SIGTERM cycle. Do not treat an emulated ARM64 run on x64 as
native ARM64 validation.

## Container

The multi-stage Dockerfile builds without network dependency access, using the
native builder to cross-compile for the selected target. The final ubuntu:26.04 image
contains only the static release executable, runs as non-root UID/GID 65532,
and uses exec-form entrypoint for signal delivery. It needs neither a writable
filesystem nor runtime dependencies.

```sh
docker build --platform linux/amd64 -t minimal-todo:amd64 .
docker build --platform linux/arm64 -t minimal-todo:arm64 .
docker run --detach --name minimal-todo-local --platform linux/amd64 \
  --user 65532:65532 --read-only --cap-drop ALL \
  --security-opt no-new-privileges \
  --publish 127.0.0.1:8080:8080 minimal-todo:amd64
curl --http1.1 --fail http://127.0.0.1:8080/healthz
curl --http1.1 --fail http://127.0.0.1:8080/todos
docker stop --timeout 10 minimal-todo-local
docker inspect --format '{{.State.ExitCode}}' minimal-todo-local
docker logs minimal-todo-local
docker rm minimal-todo-local
```

On a native ARM64 host, use `--platform linux/arm64` and `minimal-todo:arm64`
instead. The expected exit code after `docker stop` is 0, with
`Shutdown requested.` and `Application stopped.` in the logs, without forced
termination. Stop/remove only your own named container. Image acquisition can
require internet access at build time; the running application needs none.

## Validation record and limitations

Verified on September 30, 2026:

| Check | Result |
| --- | --- |
| Compiler and embedded application runtime | `go version go1.27 linux/amd64`, from the floating official builder image |
| Available native execution platform | Linux x86_64, Docker Desktop and Ubuntu-24.04 WSL; no ARM64 host |
| Container tools | Docker client/engine 29.8.1, Docker Desktop 4.93.0, Buildx v0.37.1 |
| Formatting and static checks | `gofmt` clean; `go vet ./...` passed |
| Automated contract/lifecycle tests | `go test -count=1 -timeout=120s ./...` passed |
| Race detection | `go test -race -count=1 -timeout=120s ./...` passed |
| Release binaries | Static Linux amd64 and arm64 builds passed; ELF architecture and Go build metadata checked |
| Native x64 release execution | Invalid configuration, occupied port, HTTP/1.1 keep-alive, readiness, SIGINT and SIGTERM tests passed against the release binary, in both Docker and WSL |
| Container builds | Both `linux/amd64` and `linux/arm64` image builds passed; both declare UID/GID 65532 and exec-form entrypoint |
| Native x64 container smoke | 44 real HTTP assertions passed, including all records, exact golden bytes, errors, mutations, and disabled compression/conditional responses |
| Container shutdown | SIGTERM exited 0 in 0.550 seconds, with shutdown logs and no forced termination; the owned container was removed |

The supplied and copied fixture have the same SHA-256:
`43f7b08fc8a117d3d1b5b1c863141bf080bbce0d56f7606b95b6c73582bf73ad`.
Source inspection confirms `json.Marshal` is called for each successful Todo
request and production code never reads the fixture file. Compiler/test containers
ran non-root with network disabled; the smoke container was read-only, non-root,
capability-dropped, and published only to a random loopback port. No global SDK
was installed. Go was unavailable directly on the Windows PATH/WSL, so compilation
used the pinned local Docker toolchain instead.

**Unexecuted:** native ARM64 test execution, release start/request/shutdown, and
ARM64 image smoke. The ARM64 binary/image were cross-built on x64, not executed
or emulated. On a native Linux ARM64 host with Go 1.27 and Docker installed,
the outstanding steps are:

```sh
export GOTOOLCHAIN=local
go test -count=1 -timeout=120s ./...
go test -race -count=1 -timeout=120s ./...
CGO_ENABLED=0 go build -trimpath -o bin/todo-linux-arm64 .
TODO_TEST_BINARY="$PWD/bin/todo-linux-arm64" go test -count=1 -timeout=120s \
  -run 'Test(InvalidPortStartup|OccupiedPortStartup|ProcessLifecycle)$' .
docker build --platform linux/arm64 -t minimal-todo:arm64 .
docker run --detach --name minimal-todo-arm-local --platform linux/arm64 \
  --user 65532:65532 --read-only --cap-drop ALL \
  --security-opt no-new-privileges \
  --publish 127.0.0.1:8080:8080 minimal-todo:arm64
curl --http1.1 --fail http://127.0.0.1:8080/healthz
curl --http1.1 --fail http://127.0.0.1:8080/todos -o bin/arm-container-todos.json
cmp testdata/minimal-todo-v1.expected.json bin/arm-container-todos.json
docker stop --timeout 10 minimal-todo-arm-local
docker inspect --format '{{.State.ExitCode}}' minimal-todo-arm-local
docker logs minimal-todo-arm-local
docker rm minimal-todo-arm-local
```

Verify exit code 0 and clean lifecycle logs within 10 seconds. The race check
requires a C compiler on that ARM64 host.

## Files

- `go.mod`, `main.go`, `server.go`: pinned module and application.
- `server_test.go`, `lifecycle_test.go`: HTTP, concurrency, error, and process tests.
- `testdata/minimal-todo-v1.expected.json`: exact supplied golden bytes.
- `Dockerfile`, `.dockerignore`: pinned multi-stage Linux container build.
- `README.md`: local operation, build/test/container instructions and limitations.
- `bin/`: both static release binaries and the x64 contract-test executable.
- `validation/`: Go checks, native WSL checks, both image-build logs, and container
  smoke/lifecycle evidence from this implementation run.

Local image tags from this run are `minimal-todo-go-v1-0e15c70e:amd64` and
`minimal-todo-go-v1-0e15c70e:arm64`. No service is left running.
