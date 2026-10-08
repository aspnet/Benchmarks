# Minimal Todo v1

A small HTTP CRUD workload (`/todos`, `/healthz`) implemented three times —
C#, Go, and Rust — to compare minimal API frameworks under an identical
fixed-rate load. Source generation, admission review, and quota-calibration
tooling for this cohort — including the HTTP contract fixture and the
one-time state/body verification that uses it — live in the separate
`dotnet-performance-tools` repository. This package is the shipped,
day-to-day Crank runtime surface only: the applications and one scenario
file. There is no Python tooling and no fixture here.

## Applications

Applications live in `src/BenchmarksApps/MinimalTodo/{csharp,go,rust}`, each
with its own `Dockerfile` and README covering its local build/test lifecycle.

Base images are floating (no pinned digests), multi-arch, and share one final
runtime OS across all three languages:

| Language | Build image | Final image |
| --- | --- | --- |
| C# (.NET 11, ASP.NET Core Minimal APIs) | `mcr.microsoft.com/dotnet/nightly/sdk:11.0` | `mcr.microsoft.com/dotnet/nightly/aspnet:11.0` (Ubuntu 26.04) |
| Go (`net/http`) | `golang:1.27-bookworm` | `ubuntu:26.04` |
| Rust (Axum/Tokio) | `rust:1.96-bookworm` | `ubuntu:26.04` |

.NET 12 images are not yet published; C# intentionally floats on the .NET 11
nightly channel until a stable .NET 12 tag exists. Floating tags mean the
exact compiler/runtime/OS patch level can change between runs; this is a
deliberate simplification over pinning digests, at the cost of exact
reproducibility between invocations.

## Scenario

[minimal-todo.benchmarks.yml](minimal-todo.benchmarks.yml) defines three
Docker-building scenarios (`csharp`, `go`, `rust`), each with two roles:
`application` and `load`; plus one optional non-Docker comparison scenario,
`csharp-project` (see below). In each Docker scenario, the application job
clones `aspnet/Benchmarks` at `sourceRevision`, builds that language's
`Dockerfile`, and waits for `Application started.`
(the only readiness gate a regular run performs — the full HTTP state/body
contract is verified once, separately, by the `dotnet-performance-tools`
generation/admission flow, not on every run). `load` imports and runs the
official, unmodified
[`dotnet/crank` Bombardier job](https://raw.githubusercontent.com/dotnet/crank/main/src/Microsoft.Crank.Jobs.Bombardier/bombardier.yml)
(floating on its own `main`, matching this round's floating-image intent)
against `/todos` at a fixed offered rate, with `transport: http1`,
`presetHeaders: none`, and explicit `customHeaders` for
`Accept: application/json`, `Connection: keep-alive`, and
`Accept-Encoding: identity`. No per-job architecture gating is applied:
images are multi-arch, so the scenario runs unmodified on x64 or ARM64
agents.

`sourceRevision` defaults to `main` (manual-run convenience: `main` resolves
to a full clone checked out on the branch tip) and otherwise must be an
explicit 40-character lowercase commit hash (enforced by `onConfigure`); any
other value, including an arbitrary branch name, is rejected. The scheduled
pipeline always passes the exact triggering build's commit SHA (see
`build/minimal-todo-scenarios.yml`), never the `main` default. A SHA is
checked out via the full-clone `"#" + sha` form Crank requires for an exact
commit — a bare SHA would instead attempt a shallow branch clone and fail.
Manual connected runs use the shared `build/ci.profile.yml` /
`build/azure.profile.yml` profiles (or explicit
`--variable serverAddress=...`/endpoint overrides) the same way every other
scenario in this repository does; there is no scenario-specific profile
file.

### Optional direct Crank build (C#, not scheduled)

The `csharp-project` scenario is an optional, **not scheduled** side-by-side
comparison point: it builds the exact same `src/TodoApi/TodoApi.csproj`
directly with Crank (`project:` + `framework: net11.0` + `channel: latest`),
with no Docker image at all, using the same `sourceRevision`/CPU/memory/port
validation and the same imported Bombardier load job as the Docker `csharp`
scenario. `channel: latest` selects a coherent latest SDK/runtime/ASP.NET
Core build (the user's goal is comparing against current released/floating
versions, not an incompatible bleeding-edge combination).

This only changes how the app is **packaged and started** — a native process
started by Crank on the agent's host OS instead of a container image pulled
from a registry. Crank's own CPU/memory limits (`CpuLimitRatio`/
`MemoryLimitInBytes`, via Linux cgroups) are applied identically to both the
Docker and direct jobs, so a Docker-vs-direct comparison isolates packaging/
runtime/host-OS effects, not a CPU/memory quota difference. `csharp-project`
is **not** added to `build/minimal-todo-scenarios.yml` or any pod/CI
configuration — the scheduled `csharp` scenario remains the Docker baseline
unchanged; running `csharp-project` is always a manual, explicit
`--scenario csharp-project` invocation.

### Results

Append the **unchanged** `../steadystate.profile.yml` once as the final
`--config` (not an import). Reporting is otherwise stock Crank: the official
Bombardier wrapper's own request/bad-response/latency/RPS/throughput
measurements, the agent's own CPU/memory measurements, and the shared
steadystate profile's CPU/working-set P90 helpers. `bombardier/raw` (the raw
per-request JSON payload) is excluded from persistence to avoid storing that
large aggregate; no other custom result definitions or hooks are shipped —
calibration-quality analysis belongs to `dotnet-performance-tools`.

The shipped `rate` (25200 req/s, see `build/minimal-todo-scenarios.yml`) was
calibrated and accepted against the **prior** pinned-digest C#/Go/Rust images
and project settings. This round floated the Docker base images and moved the
C# project to `net11.0`; that calibration has not been re-run against the new
images, so a post-merge scheduled run is the first operational confirmation
under the new images, not a re-validation.
