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
`csharp-project`, which is available only for manual comparisons and is not
scheduled. In each Docker scenario, the application job
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

Without a size profile, manual runs retain normal, typed Crank job defaults --
`cpuLimitRatio: 1`, `memoryLimitInBytes: 536870912` (512 MiB), `port: 8080` --
with no custom `onConfigure` hook. These are overridable the same way `build/containers-scenarios.yml`
overrides its own jobs: `--application.cpuLimitRatio`,
`--application.memoryLimitInBytes`, `--application.port`. Each scenario's
`load` role sets its own `rate: 100` (a safe, low manual-smoke default; the
imported Bombardier job's own default is `0`, meaning uncapped) and
`serverPort: 8080` (matching the application's default port, since the
imported job's own default is `5000`); both are overridable via
`--variable rate=...`/`--variable serverPort=...`. Regular scheduled runs
always select a size profile for CPU, memory, and `load.variables.rate`;
they never execute a separate default-size case. The scheduled
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

### Execution paths: scheduled Docker vs. manual direct C#

The Docker scenarios (`csharp`, `go`, `rust`, backed by the `todo-csharp`/
`todo-go`/`todo-rust` jobs) are unchanged and remain the canonical
generation/update/admission/pre-promotion comparison path: fully resolvable
for manual runs and for the separate `dotnet-performance-tools` generation
flow. These three Docker scenarios are the only scheduled implementations,
and every scheduled case selects one of the size profiles below.

The manual-only `csharp-project` scenario still builds the same
`src/TodoApi/TodoApi.csproj` directly with Crank (`project:` +
`framework: net11.0` + `channel: latest`), without Docker. It retains its
existing source, resource, port, and manual-smoke load defaults; no regular
dispatch selects it. Direct and Docker results are different execution
paths, not interchangeable container results.

### Scheduled container resource tiers

`build/minimal-todo-scenarios.yml` expands one canonical language list
(`csharp`, `go`, and `rust`, all Docker) across these four paired
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

All four C# Docker tiers run every regular slot; Go and Rust retain the
weekly condition `Math.round(Date.now() / 43200000) % 14 == 0`, combined
with the existing pod condition. This is evaluated independently per
pipeline/pod, not one globally deduplicated weekly schedule; an ad hoc
or duplicate pipeline run is not deduplicated by this condition alone.
The existing consumers in `build/benchmarks-ci-01.yml` (`gold-lin`, cron
`0 3/12 * * *`) and
`build/benchmarks-ci-azure.yml` (`azure-arm64` and `cobalt-cloud-lin`, cron
`0 2/12 * * *`) invoke this template, so the tiers are regular scheduled
work, not just manual aliases. Machine profiles, queues, cron expressions,
and pod conditions are unchanged. Only Minimal Todo's enclosing job timeout
in the pod configs/generated pipelines is 150 minutes: twelve 10-minute
case budgets plus 30 minutes of headroom. Every regular invocation publishes
12 conditional matrix cases; subject to the pod condition, all 12 are
active in the weekly slot and only the four C# cases are active otherwise.
No extra baseline or default-size cases are dispatched.

Every tier uses HTTP/1.1 GET `/todos`, 128 connections, 30 seconds of warmup,
120 seconds of measurement, a 2-second timeout, and the three explicit
headers above. The dispatch appends the unchanged `steadystate.profile.yml`
as its final config; its measurement window uses `load.variables.duration`.
Each size profile is the source of the tier's target rate. Results use the
existing `ContainerBenchmarks` table with distinct
`scenario=MinimalTodo-<language>-<profile>` and `size=<profile>` properties,
plus CPU, byte-memory, rate, and `todo/execution=docker` properties. Historical
results and manual direct C# results are not relabeled as container results.

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
Scheduled runs pin the app source to the triggering commit; manual runs
retain the normal source overrides described above.

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

The scheduled targets are the explicit per-size values above, not a
per-language or universal platform capacity guarantee. Resource headroom is
not independently verified for every scheduled machine/image combination.
Full generated test suites, fixture/contract checkers, review, and campaign/validation history
for this cohort are owned by the separate `dotnet-performance-tools`
repository's canonical candidate archive.
