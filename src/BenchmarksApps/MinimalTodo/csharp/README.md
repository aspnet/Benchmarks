# Minimal Todo API

A standalone C# / ASP.NET Core Minimal API with five immutable in-memory records.
Each successful Todo request uses ASP.NET Core's JSON serializer; the golden file
is only test input. There are no application package dependencies or external
services. Requests do not read files, and no database or credentials are needed.

## Prerequisites and build

Install .NET SDK **11.0** (development/nightly channel), floating to whatever
patch build is currently published; no `global.json` SDK pin and no runtime
version pin in `Directory.Build.props`. Release is the normal optimized build.
NuGet dependencies (tests only) have exact direct versions and a generated
`packages.lock.json` file. The application has no package dependencies: its
target framework is set in `Directory.Build.props`, without a platform-dependent
package lock.

From this directory (PowerShell commands):

```powershell
dotnet restore TodoApi.slnx --locked-mode
dotnet build TodoApi.slnx --configuration Release --no-restore
dotnet test TodoApi.slnx --configuration Release --no-build --no-restore --blame-hang-timeout 60s
$env:PORT = '8080'
dotnet src\TodoApi\bin\Release\net11.0\TodoApi.dll
```

On Linux use `/` instead of `\` in command paths and
`PORT=8080 dotnet src/TodoApi/bin/Release/net11.0/TodoApi.dll`.
The HTTP tests use real Kestrel listeners on temporary ports, not a mocked server.
They verify exact fixture bytes, every item, malformed/missing IDs, all specified
mutations, unknown paths, health, compression/negotiation/conditional headers,
concurrent and persistent-connection reads, invalid and occupied ports, and
bounded graceful host shutdown. Linux additionally runs SIGTERM and SIGINT
subprocess checks; those checks are explicitly skipped on Windows.

## Run and contract

`PORT` defaults to `8080`; when set it must contain only ASCII decimal digits
and represent 1 through 65535. Invalid values and bind failures log diagnostics
and exit nonzero. The service listens on `0.0.0.0` over HTTP/1.1, including
persistent connections. It prints the exact line `Application started.` only
after the listener and dataset are ready.

```powershell
curl.exe --http1.1 --include http://127.0.0.1:8080/todos
curl.exe --http1.1 --include http://127.0.0.1:8080/todos/1
curl.exe --http1.1 --include http://127.0.0.1:8080/healthz
```

On Linux use `curl` instead of `curl.exe`. `/todos` returns the exact compact
golden array; `/todos/1` through `/todos/5` return individual objects.
`/healthz` returns plain UTF-8 `ready`. Missing/malformed IDs and unknown paths
return empty 404s. POST/PUT/PATCH/DELETE on the list or known items return empty
405s. There is no compression, output caching, conditional response processing,
HTTPS redirection, or representation negotiation. HEAD/OPTIONS/trailing-slash
aliases are left to the framework and are not part of the contract.

Startup/readiness, shutdown, and unexpected failures are logged; routine
ASP.NET Core access logs are disabled. Unexpected request errors are logged by
the exception-handler middleware and return 500, not a success response.
Non-default settings are HTTP/1.1-only listening, the specified JSON formatting,
the log filter, strict runtime selection, and an **8-second graceful host
shutdown timeout** to leave margin for the 10-second exit target.
Use Ctrl+C locally or SIGTERM on Linux; accepted requests are drained where
practical and new work stops. Do not use forced termination for a normal stop.

The required all-interface listener is not a public deployment: use a trusted
local machine/firewall. No deployment or cloud resources are configured.

## Native Linux builds and container

For deployment via an installed native .NET runtime, a managed-only publish
works on either Linux architecture without downloading a native apphost:

```powershell
dotnet publish src\TodoApi\TodoApi.csproj --configuration Release --self-contained false --no-restore -p:UseAppHost=false --output artifacts\managed
```

On the native Linux host, run `PORT=8080 dotnet artifacts/managed/TodoApi.dll`.
This uses that host's native .NET runtime; it does not use emulation.

Framework-dependent native-target publishes (runnable only with the matching
Linux architecture and the pinned runtimes):

```powershell
dotnet publish src\TodoApi\TodoApi.csproj --configuration Release --runtime linux-x64 --self-contained false --output artifacts\linux-x64
dotnet publish src\TodoApi\TodoApi.csproj --configuration Release --runtime linux-arm64 --self-contained false --output artifacts\linux-arm64
```

Run the resulting `TodoApi` executable on its matching native Linux host.
Cross-publishing is not native execution. On each native Linux x64/ARM64 host,
run the release tests above, then build and smoke-test the corresponding image.
The Dockerfile uses floating multi-architecture Microsoft nightly SDK/runtime
images, a multi-stage Release publish, the built-in non-root `$APP_UID` user,
and an exec-form entrypoint for signal delivery. One application process runs
per container. The SDK/dependencies are needed at build time only.

```powershell
docker info --format '{{.OSType}}/{{.Architecture}}'
docker build --pull --tag minimal-todo:v1 .
docker run --detach --name minimal-todo-v1 --publish 127.0.0.1:8080:8080 --stop-timeout 10 minimal-todo:v1
docker logs minimal-todo-v1
curl.exe --fail --http1.1 http://127.0.0.1:8080/healthz
curl.exe --fail --http1.1 http://127.0.0.1:8080/todos
docker stop --time 10 minimal-todo-v1
docker inspect --format '{{.State.ExitCode}}' minimal-todo-v1
docker rm minimal-todo-v1
```

Use a native engine for each architecture, not emulation. The host publication
is loopback-only; do not use privileged containers or host networking. Wait for
the exact readiness line before sending requests. Confirm list-response bytes
against `tests/TodoApi.Tests/minimal-todo-v1.expected.json` and exit code zero
within 10 seconds; report any timeout or forced exit (including code 137).
Remove only the container you created.

## Verification in the supplied environment

Local host: **Windows x64**, SDK **10.0.401**, .NET and ASP.NET Core runtimes
**10.0.12**. The generated runtime configuration requires both framework versions
exactly. No SDK installation, Git operation, deployment, or daemon change was
performed.

- Release solution build passed with zero warnings/errors.
- HTTP/lifecycle tests: **68 passed, 1 Linux-only theory skipped** (69 reported
  tests). That skipped theory covers SIGTERM and SIGINT when run on Linux.
  Local readiness, real HTTP requests, and graceful `StopAsync` shutdown passed
  with the 10-second bound; native Linux process-signal handling is not verified.
- The supplied golden fixture was copied byte-for-byte: **355 bytes**, SHA-256
  `43f7b08fc8a117d3d1b5b1c863141bf080bbce0d56f7606b95b6c73582bf73ad`.
- Framework-dependent Release publishing passed. The managed-only output is
  in `artifacts/managed`; this is not evidence of native Linux execution.
- Logs and the machine-readable test report are under
  `artifacts/verification` (`contract.trx`).

**Environment limitations and remaining checks:**

NuGet's public service index failed with a TLS `HandshakeFailure`. Locked test
dependencies were available in the local cache, so the executed restore used
`dotnet restore TodoApi.slnx --locked-mode -p:NuGetAudit=false`.
This is a command-local workaround, not a project-wide audit disable:
**vulnerability auditing was not performed**. Repeat
`dotnet restore TodoApi.slnx --locked-mode` with working NuGet access to perform
the normal audit. Do not disable TLS certificate verification.

Both `--runtime linux-x64` and `--runtime linux-arm64` publish commands shown
above were attempted but failed with **NU1301** while retrieving the missing
platform packs. They must be rerun with working package access; no successful
RID-specific publish or native execution is claimed.

Docker CLI **29.8.1** is installed, but `docker version` could not connect to
`dockerDesktopLinuxEngine` (named pipe not found). The engine was not started or
reconfigured. The exact unexecuted steps are the `docker build`, `docker run`,
readiness/fixture smoke checks, and timed `docker stop`/exit inspection shown
above, on **both native Linux x64 and ARM64 engines**. Container image
availability, non-root execution, and container shutdown remain unverified.
On each native Linux architecture also run
`dotnet test TodoApi.slnx --configuration Release --filter FullyQualifiedName~LinuxSignalShutsDownReadyProcessWithinTenSeconds`
to execute the currently skipped process-signal checks. No emulated execution
has been used or counted as native validation.
