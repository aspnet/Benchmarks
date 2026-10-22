# TechEmpower scenarios: Minimal APIs and Platform

Both app variants implement the TechEmpower plaintext, json and fortunes tests and are defined in `aspnet/Benchmarks`.

| Variant | Config file | App project | Job name |
|---|---|---|---|
| Minimal APIs | `src/BenchmarksApps/TechEmpower/Minimal/minimal.benchmarks.yml` | `src/BenchmarksApps/TechEmpower/Minimal/Minimal.csproj` | `minimal` |
| Platform | `scenarios/platform.benchmarks.yml` | `src/BenchmarksApps/TechEmpower/PlatformBenchmarks/PlatformBenchmarks.csproj` | `platformbenchmarks` |

Raw URL prefix: `https://raw.githubusercontent.com/aspnet/Benchmarks/main/`

Both configs import `scenarios/aspnet.profiles.standard.yml` (which defines the machine profiles) and the crank `wrk` job.

## Scenarios

| Scenario | Path | Load | Connections | Roles |
|---|---|---|---|---|
| `plaintext` | `/plaintext` | wrk, pipeline 16 | 1024 | application, load |
| `json` | `/json` | wrk | 512 | application, load |
| `fortunes` | `/fortunes` | wrk, html headers | 512 | application, load, db (PostgreSQL) |

Platform also defines `fortunes_dapper` and `fortunes_ef` (same roles as `fortunes`). Minimal also has `*_result` variants (for example `json_result`). Both configs define further database scenarios (`single_query`, `multiple_queries`, `updates`).

Fortunes needs the `db` role. The `db` job builds PostgreSQL from `TechEmpower/FrameworkBenchmarks`, and the app reads it via `{{databaseServer}}` from the profile.

## Profiles

Use one profile group so all roles are placed consistently:

- `aspnet-gold-lin`: Linux gold machines (`asp-gold-lin` app, `asp-gold-load` load, `asp-gold-db` db).
- `aspnet-gold-win`: Windows gold machines.
- `aspnet-cobalt-hosted-lin` / `aspnet-cobalt-hosted-al3-lin`: Cobalt (ARM) hosted Linux machines.
- Each has a `-relay` twin (for example `aspnet-gold-lin-relay`); see Relay profiles below.

Names are defined in `scenarios/aspnet.profiles.standard.yml`; check there for the current list before using others.

## Relay profiles

Non-relay profiles (for example `aspnet-gold-lin`) address the crank agents directly, e.g. `http://asp-gold-lin:5001`, so they only work from a machine on the benchmark network.

`-relay` profiles define the same roles, addresses and variables but reach the agents through Azure Relay endpoints (for example `https://aspnetperf.servicebus.windows.net/goldlin`, `goldload`, `golddb`). Use them from outside the network, and as CI/PR-benchmark bots do:

- Swap only the profile name: `--profile aspnet-gold-lin-relay`. Scenarios, configs and other arguments are unchanged.
- Also pass a bare `--relay` (no value). The controller then authenticates to the relay with Azure CLI credentials, so run `az login` first with an account that has access. No connection string, client id, certificate or other auth options are needed.
- If authentication fails ("Failed to authenticate with Azure CLI"), check `az account show` and re-run `az login`.
- Gold Windows (`aspnet-gold-win-relay`) uses `goldwin`, `goldload2` and `golddb`.
- Do not mix relay and non-relay profiles in one command.

```bash
crank \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/platform.benchmarks.yml \
  --scenario json \
  --profile aspnet-gold-lin-relay \
  --relay \
  --application.framework net11.0 \
  --json platform-json.json
```

## Commands

Minimal APIs:

```bash
crank \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/src/BenchmarksApps/TechEmpower/Minimal/minimal.benchmarks.yml \
  --scenario plaintext|json|fortunes \
  --profile aspnet-gold-lin \
  --application.framework net11.0 \
  --json minimal-<scenario>.json
```

Platform:

```bash
crank \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/platform.benchmarks.yml \
  --scenario plaintext|json|fortunes \
  --profile aspnet-gold-lin \
  --application.framework net11.0 \
  --json platform-<scenario>.json
```

## CI trend names

The same scenarios run in CI (`build/trend-scenarios.yml`, `build/trend-database-scenarios.yml`) using `$(minimalJobs)` / `$(platformJobs)` from `build/job-variables.yml`:

| CI display name | Scenario | Extra args |
|---|---|---|
| Plaintext Minimal APIs | `plaintext` | `--load.connections 1024` |
| Json Minimal APIs | `json` | `--load.connections 512` |
| Fortunes Minimal APIs | `fortunes` | |
| Plaintext Platform | `plaintext` | `--load.connections 1024` |
| Json Platform | `json` | `--load.connections 512` |
| Fortunes Platform | `fortunes` | |
| Fortunes Platform EF / Dapper | `fortunes_ef` / `fortunes_dapper` | |
| Plaintext / Json Platform Inline | `plaintext` / `json` | `--application.environmentVariables DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1` |

## Guidance

- Compare Minimal vs Platform by running the same scenario, profile and framework; vary one factor at a time.
- Keep a unique `--json` filename per run so `--compare` works.
