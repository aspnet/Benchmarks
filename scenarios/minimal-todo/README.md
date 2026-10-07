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
scenarios (`csharp`, `go`, `rust`), each with two roles: `application` and
`load`. The application job clones the exact `sourceRevision` commit of
`aspnet/Benchmarks`, builds that language's `Dockerfile`, and waits for
`Application started.` (the only readiness gate a regular run performs — the
full HTTP state/body contract is verified once, separately, by the
`dotnet-performance-tools` generation/admission flow, not on every run).
`load` runs the unchanged official Bombardier wrapper from
`dotnet/crank@25c21e9a86b4e485e6f53850c2d7766505d27211` at a fixed offered
rate against `/todos`. No per-job architecture gating is applied: images are
multi-arch, so the scenario runs unmodified on x64 or ARM64 agents.

`sourceRevision` must be an explicit 40-character commit hash (enforced by
`onConfigure`); a mutable branch name is rejected. Manual connected runs use
the shared `build/ci.profile.yml` / `build/azure.profile.yml` profiles (or
explicit `--variable serverAddress=...`/endpoint overrides) the same way
every other scenario in this repository does; there is no scenario-specific
profile file.

### Results and calibration

Append the **unchanged** `../steadystate.profile.yml` once as the final
`--config` (not an import). The scenario's own `onResultsCreating`/
`onResultsCreated` hooks validate the measured Bombardier result strictly:
exact request URL/method/client/rate/duration/connections/timeout and request
headers, response counters all 2xx with zero other status classes, zero
transport errors, and delivered rate/duration within 5% of the offered
values. They also compute a `todo/calibration/*` summary (CPU tail mean/P90/
max over the last 60 measured seconds, half-window drift, sample gaps).
`todo/cpu/raw/*` and `todo/memory/*` are plain Crank aggregate helpers over
the full running window. This is standard day-to-day perf validation —
native HTTP status/error/workload/resource checks, not a full state/body
contract probe.

The shipped `rate` (25200 req/s, see `build/minimal-todo-scenarios.yml`) was
calibrated and accepted against the **prior** pinned-digest C#/Go/Rust images
and project settings. This round floated the Docker base images and moved the
C# project to `net11.0`; that calibration has not been re-run against the new
images, so a post-merge scheduled run is the first operational confirmation
under the new images, not a re-validation.

## Offline resolution

From the repository root, with no agent/network/Docker required:

```powershell
crank --config .\scenarios\minimal-todo\minimal-todo.benchmarks.yml --config .\scenarios\steadystate.profile.yml --scenario rust --profile offline --iterations 1 --debug resolved.json
```

The `offline` profile sets both role endpoints to an empty array and a dummy
all-zero `sourceRevision`; this resolves and validates the configuration
structure without a real deploy.
