# Minimal Todo API

A standalone C# / ASP.NET Core Minimal API with five immutable in-memory
records. There are no package dependencies, external services, file reads,
database, or credentials.

## Build

Install .NET SDK **11.0** (development/nightly channel), floating to whatever
patch build is currently published; no `global.json` SDK pin and no runtime
version pin in `Directory.Build.props`. The application has no package
dependencies, but still has its own `src/TodoApi/packages.lock.json` (used
for the Dockerfile's `--locked-mode` restore); with zero dependencies, its
recorded `net11.0` dependency set is empty.

From this directory (PowerShell; on Linux use `/` instead of `\`):

```powershell
dotnet restore TodoApi.slnx --locked-mode
dotnet build TodoApi.slnx --configuration Release --no-restore
```

## Run

```powershell
$env:PORT = '8080'
dotnet src\TodoApi\bin\Release\net11.0\TodoApi.dll
```

`PORT` defaults to `8080`; when set it must contain only ASCII decimal digits
and represent 1 through 65535. Invalid values and bind failures log
diagnostics and exit nonzero. The service listens on `0.0.0.0` over
HTTP/1.1, including persistent connections, and prints the exact line
`Application started.` once the listener and dataset are ready.

## Contract

- `GET /todos`: the five fixed Todos as a compact JSON array (property order
  `id`, `title`, `dueBy`, `isComplete`).
- `GET /todos/1` through `/todos/5`: one Todo.
- `GET /healthz`: plain UTF-8 `ready`.
- Invalid/missing IDs and unknown paths: empty `404`.
- Mutating methods (`POST`/`PUT`/`PATCH`/`DELETE`) on the list or a known
  item: empty `405`.

There is no compression, output caching, conditional response processing,
HTTPS redirection, or representation negotiation. HEAD/OPTIONS/trailing-slash
aliases are left to the framework and are not part of the contract.

## Shutdown and logging

Startup/readiness, shutdown, and unexpected failures are logged; routine
ASP.NET Core access logs are disabled. Unexpected request errors return 500.
Use Ctrl+C locally or SIGTERM on Linux: accepted requests are drained where
practical and new work stops, with an **8-second graceful shutdown timeout**
(inside the standard 10-second exit target). Do not use forced termination
for a normal stop.

The all-interface listener is not a public deployment: use a trusted local
machine/firewall. No deployment or cloud resources are configured.

## Container

```powershell
docker build --pull --tag minimal-todo:v1 .
docker run --detach --name minimal-todo-v1 --publish 127.0.0.1:8080:8080 --stop-timeout 10 minimal-todo:v1
curl.exe --fail --http1.1 http://127.0.0.1:8080/healthz
curl.exe --fail --http1.1 http://127.0.0.1:8080/todos
docker stop --time 10 minimal-todo-v1
docker rm minimal-todo-v1
```

The Dockerfile uses the floating multi-architecture Microsoft nightly
SDK/ASP.NET Core images (`mcr.microsoft.com/dotnet/nightly/{sdk,aspnet}:11.0`),
a multi-stage Release publish, the built-in non-root `$APP_UID` user, and an
exec-form entrypoint for signal delivery. One application process runs per
container; the SDK is needed at build time only.

Generated candidate source, admission review, and full test/validation
history for this cohort are owned by the separate
`dotnet-performance-tools` repository.
