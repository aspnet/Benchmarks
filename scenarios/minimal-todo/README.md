# Minimal Todo v1

A small HTTP CRUD workload (`/todos`, `/healthz`) implemented three times —
C#, Go, and Rust — to compare minimal API frameworks under an identical
fixed-rate load. Source generation, admission review, quota-calibration, and
the full generated test suite/fixture/validation history for this cohort are
owned by the separate `dotnet-performance-tools` repository, which holds the
canonical candidate archive. This package is the shipped, day-to-day Crank
runtime surface only: the applications (production code only, no test
projects/fixtures) and one scenario file.

## Applications

Applications live in `src/BenchmarksApps/MinimalTodo/{csharp,go,rust}`, each
with its own `Dockerfile` and README covering its local build/run lifecycle.
Production code only; no test projects, fixtures, or validation evidence are
shipped here (see "Results" below for where that history lives).

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
`application` and `load`; plus one direct, non-Docker scenario,
`csharp-project` (see "Execution paths" below for the baseline and resource-tier
schedules). In each Docker scenario, the application job
clones `aspnet/Benchmarks` (the `app` source, `branchOrCommit` defaulting to
`main`), builds that language's
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

Each app job uses normal, typed Crank job properties -- `cpuLimitRatio: 1`,
`memoryLimitInBytes: 536870912` (512 MiB), `port: 8080` -- with no custom
`onConfigure` hook. These are overridable the same way `build/containers-scenarios.yml`
overrides its own jobs: `--application.cpuLimitRatio`,
`--application.memoryLimitInBytes`, `--application.port`. Each scenario's
`load` role sets its own `rate: 100` (a safe, low manual-smoke default; the
imported Bombardier job's own default is `0`, meaning uncapped) and
`serverPort: 8080` (matching the application's default port, since the
imported job's own default is `5000`); both are overridable via
`--variable rate=...`/`--variable serverPort=...`, exactly like the
scheduled pipeline does for its accepted rate. The scheduled
pipeline pins the exact triggering build's commit via
`--application.sources.app.branchOrCommit "#<sha>"` (see
`build/minimal-todo-scenarios.yml`); a manual run defaults to the `app`
source's `main` branch, or can pass any normal Crank source override
(`--application.sources.app.branchOrCommit`, `.localFolder`, etc.) the same
way as any other scenario. A SHA must use the full-clone `"#" + sha` form
Crank requires for an exact commit — a bare SHA would instead attempt a
shallow branch clone and fail; this is enforced by normal Crank source
semantics, not a scenario-specific guard. Exact-commit generation provenance
is enforced by the separate `dotnet-performance-tools` admission flow, not by
this YAML. Manual connected runs use the shared `build/ci.profile.yml` /
`build/azure.profile.yml` profiles (or explicit
`--variable serverAddress=...`/endpoint overrides) the same way every other
scenario in this repository does; there is no scenario-specific profile
file.

### Execution paths: Docker comparisons vs. direct C# baseline

The Docker scenarios (`csharp`, `go`, `rust`, backed by the `todo-csharp`/
`todo-go`/`todo-rust` jobs) are unchanged and remain the canonical
generation/update/admission/pre-promotion comparison path: fully resolvable
for manual runs and for the separate `dotnet-performance-tools` generation
flow. They also supply the scheduled container resource tiers below.

`build/minimal-todo-scenarios.yml` retains the existing 1 CPU / 512 MiB /
25200 req/s baseline dispatch:

- **C#** submits `--scenario csharp-project` (the direct, non-Docker build
  described below) on every regular scheduled run, at the same cadence as
  before (each pipeline's own ~12-hour cron, unconditionally).
- **Go and Rust** still submit the Docker `go`/`rust` scenarios, but now run
  **weekly** instead of twice a day: each scenario entry adds
  `condition: Math.round(Date.now() / 43200000) % 14 == 0` (true once every
  14 half-day scheduled slots, i.e. once per ~7 days), combined with the
  existing pod-level condition (`(pod condition) && (scenario condition)`,
  matching the idiom already used in `build/containers-scenarios.yml`). This
  is evaluated independently per pipeline/pod (the `gold-lin` pod via the
  `ci01` pipeline's cron, `azure-arm64`/`cobalt-cloud-lin` via the
  Azure pipeline's own cron) -- it is **not** one single global weekly
  schedule shared across every pod, and an ad hoc or duplicate pipeline run
  is not deduplicated by this condition alone.

The `csharp-project` scenario builds the exact same `src/TodoApi/TodoApi.csproj`
directly with Crank (`project:` + `framework: net11.0` + `channel: latest`),
with no Docker image at all, using the same source/CPU/memory/port
defaults and the same imported Bombardier load job as the Docker `csharp`
scenario. `channel: latest` selects a coherent latest SDK/runtime/ASP.NET
Core build (the goal is comparing against current released/floating
versions, not an incompatible bleeding-edge combination). This only changes
how the app is **packaged and started** — a native process started by Crank
on the agent's host OS instead of a container image pulled from a registry.
Crank's own CPU/memory limits (`CpuLimitRatio`/`MemoryLimitInBytes`, via
Linux cgroups) are applied identically to both the Docker and direct jobs,
so a Docker-vs-direct comparison isolates packaging/runtime/host-OS effects,
not a CPU/memory quota difference. The distinct scenario name
(`csharp-project` vs. `csharp`) is itself recorded via the existing
`--command-line-property` flag already used by the scheduled dispatch, so
direct and Docker runs are already distinguishable in persisted results
without any new custom property.

**Comparison status (disclosed, not a formal acceptance):** a preliminary,
exploratory comparison between the Docker and direct C# builds showed
roughly 3% lower CPU quota usage for the direct build with similar latency;
process-exit verification had an unresolved local-reader-bug caveat and was
not conclusively proven either way. This is **not** a fully accepted paired
test or a recalibration — the existing `rate` (25200 req/s) is retained as
the prior operational policy value, unchanged and not re-derived from this
comparison. The direct build was still adopted for day-to-day C# scheduling
based on this preliminary closeness, as an explicit operational decision.

### Scheduled container resource tiers

In addition to those unchanged baselines, the same dispatch template expands
the Docker `csharp`, `go`, and `rust` scenarios across these four paired
profiles. Each profile applies typed `application.cpuLimitRatio` and
`application.memoryLimitInBytes` fields plus `load.variables.rate`; its target
is identical for all three languages.

| Profile | CPU cores | Memory (MiB) | Memory (bytes) | Target (req/s) |
| --- | --- | --- | --- | --- |
| `250m-64mib-2000rps` | 0.25 | 64 | 67108864 | 2000 |
| `500m-128mib-10000rps` | 0.5 | 128 | 134217728 | 10000 |
| `1000m-256mib-20000rps` | 1 | 256 | 268435456 | 20000 |
| `2000m-512mib-37500rps` | 2 | 512 | 536870912 | 37500 |

`cpuLimitRatio` is the number/fraction of **one CPU core**, not a fraction
of the host's core count. These profiles do not change CPU affinity or the
manual defaults when no size profile is selected.

The new C# Docker tiers run every regular slot; Go and Rust tiers use the
same weekly condition as their existing baselines. The existing consumers
in `build/benchmarks-ci-01.yml` (`gold-lin`, cron `0 3/12 * * *`) and
`build/benchmarks-ci-azure.yml` (`azure-arm64` and `cobalt-cloud-lin`, cron
`0 2/12 * * *`) invoke this template, so the tiers are regular scheduled
work, not just manual aliases. Machine profiles, queues, cron expressions,
and pod conditions are unchanged. Only Minimal Todo's enclosing job timeout
in the pod configs/generated pipelines increases from 40 to 180 minutes:
the weekly slot now dispatches 15 sequential cases (three baselines plus
twelve resource cases), each retaining its existing 10-minute task timeout.

Every tier uses HTTP/1.1 GET `/todos`, 128 connections, 30 seconds of warmup,
120 seconds of measurement, a 2-second timeout, and the three explicit
headers above. The dispatch appends the unchanged `steadystate.profile.yml`
as its final config; its measurement window uses `load.variables.duration`.
No rate override from the legacy baseline is applied to tier runs. Results
use the existing `ContainerBenchmarks` table with distinct
`scenario=MinimalTodo-<language>-<profile>` and `size=<profile>` properties,
plus CPU, byte-memory, rate, and `todo/execution=docker` properties. Direct
C# baseline results are not relabeled as container results.

For example, a connected manual run with a size profile and the same load
settings is:

```bash
crank --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/minimal-todo/minimal-todo.benchmarks.yml \
  --scenario csharp --profile 250m-64mib-2000rps \
  --variable connections=128 --variable warmup=30 --variable duration=120 \
  --variable timeout=2 --variable serverPort=8080 \
  --config build/ci.profile.yml --profile gold-lin-app --profile gold-load-load \
  --config build/azure.profile.yml \
  --config scenarios/steadystate.profile.yml
```

Select `go` or `rust` with the same profile for the paired implementations.
As for the baselines, scheduled runs pin the app source to the triggering
commit; manual runs retain the normal source overrides described above.

These targets were selected from Linux ARM64 Cobalt Docker comparisons,
not certified across all scheduled machines or floating image versions.
Each selected pair had two consecutive C# passes and one matched Go and
Rust pass, with at least 99% successful target-rate attainment and no
HTTP/transport errors. The quarter-core C# case had about 56 ms P99 latency
and used about 28% of its quota: the tiny tier is an informative low-quota
case, not a low-latency SLA recommendation. Higher fractional-quota offered
rates missed target attainment; this is not evidence of a Docker build
failure or a universal language capacity ceiling. Limits are based on the
configured quotas/logged limits, not independent Docker HostConfig
attestation. No admission or capacity gate is added to scheduled execution.

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
under the new images, not a re-validation. 25200 req/s remains the legacy baseline's
**operational policy value** applied uniformly to its three languages —
the additional container tiers use their explicit per-size targets above.
Neither is a per-language or
per-resource-point capacity guarantee, and resource headroom is not
independently verified for every combination. Full generated test
suites, fixture/contract checkers, review, and campaign/validation history
for this cohort are owned by the separate `dotnet-performance-tools`
repository's canonical candidate archive.
