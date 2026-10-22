# Crank Command Playbook

## Command anatomy

Build commands in this order:

1. `--config` (base scenario + profile overlays)
2. `--scenario` (single orchestrated scenario to run)
3. `--profile` entries (one or more placement overlays)
4. job-level overrides (for example `--application.framework net11.0`)
5. diagnostics (`--application.options.collectCounters true`, traces, dumps)
6. persistence (`--json <file>` and optional `--compare <file>`)

## Common options used in benchmark authoring

- `--config <path-or-url>`: load scenario/profile definitions from YAML files.
- `--scenario <name>`: select one scenario from loaded configuration.
- `--profile <name>`: apply profile overlays; prefer deterministic profile groups first (for example `aspnet-gold-lin`, `aspnet-perf-win`), then fall back to per-role profiles (`*-app`, `*-load`, `*-db`) when needed.
- `--[JOB].framework <tfm>`: override target framework per job.
- `--[JOB].sdkVersion|runtimeVersion|aspNetCoreVersion <version>`: optional pins for reproducing runtime regressions; normally omit and use only `--[JOB].framework`.
- `--[JOB].source.branchOrCommit <branch_name|#commit_hash>`: for git-backed sources, run a specific branch or exact commit.
- `--[JOB].options.collectCounters true`: enable dotnet counter collection.
- `--[JOB].collectDependencies true`: CI-focused switch used to track transitive dependency changes.
- `--[JOB].noGlobalJson false`: OrchardCore-specific override; do not apply by default to non-Orchard scenarios.
- `--json <output.json>`: persist results to JSON for later comparison.
- `--compare <baseline.json>`: compare current run against previous persisted results.

## Config selection notes

- Include `build/ci.profile.yml` for most ASP.NET benchmark profile sets.
- Include `build/azure.profile.yml` only when using Azure-specific machine profiles or relay/auth configuration.

## Reproducible command template

```bash
crank \
  --config <scenario-config.yml> \
  --config build/ci.profile.yml \
  --scenario <scenario-name> \
  --profile <group-profile-or-role-profiles> \
  [--application.source.branchOrCommit <branch_name|#commit_hash>] \
  --application.framework <tfm> \
  --application.options.collectCounters true \
  --json <results.json>
```

## Validation checklist before execution

- Scenario exists in loaded config set.
- All role profiles are provided for roles referenced by scenario.
- Profile names are spelled exactly as declared in imported config files.
- Output file path for `--json` is writable.
- Diagnostics flags match target job names (`application`, `load`, `db`).
