---
name: crank-benchmarks
description: Build, review, and troubleshoot crank benchmark commands for aspnet/Benchmarks and custom scenarios. Use when configuring multi-agent benchmark runs (`application`/`db`/`load`), selecting `--scenario` and `--profile` combinations, adding diagnostics (counters/traces/dumps), pinning .NET versions/commits, and saving comparable benchmark outputs with `--json`.
---

# Crank Benchmarks

## Overview

Use this skill to produce reliable `crank` commands, explain profile/scenario wiring, and capture comparable results across repeated runs.
Default to framework-band targeting (`--application.framework`) and persisted JSON output unless the user asks for deeper pinning.

## Workflow

1. Identify benchmark intent (scenario, scale, target machines, diagnostics).
2. Resolve required configs and profile mappings before assembling command arguments, preferring predefined profile groups when available.
3. For regression investigations, validate version availability from package feeds before running:
   - Query the relevant flat-container index (for example `.../flat2/microsoft.netcore.app.runtime.linux-x64/index.json`) to list actually published versions.
   - Cross-check benchmarked versions vs published versions to detect missing intermediate builds.
   - For each candidate intermediate version, fetch the package `.nuspec` and read `<repository ... commit=\"...\">` to map the version to a concrete `dotnet/dotnet` commit.
   - Use these commit anchors to reduce and prioritize culprit ranges even when some benchmark runs are missing.
4. Build a single canonical command line with:
   - `--config` files
   - one `--scenario`
   - required `--profile` values for each role
   - optional diagnostics and optional version-pinning switches (for regression reproduction)
   - optional git source pinning with `--application.source.branchOrCommit`
   - `--json <output.json>` for persisted results
5. Validate command completeness against scenario roles (`application`, optional `db`, `load`).
6. Produce follow-up compare commands when prior JSON files are available.

## Quick-start example

Minimal APIs JSON (TechEmpower) on the Linux gold machines:

```bash
crank \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/src/BenchmarksApps/TechEmpower/Minimal/minimal.benchmarks.yml \
  --scenario json \
  --profile aspnet-gold-lin \
  --application.framework net11.0 \
  --application.options.collectCounters true \
  --json minimal-json.json
```

The Platform variant uses `--config .../scenarios/platform.benchmarks.yml` instead; see `references/techempower-scenarios.md`.

## Defaults and guardrails

Follow these defaults unless the user asks otherwise:

- Keep one explicit `--scenario`.
- Prefer one deterministic profile group (for example `aspnet-gold-lin` or `aspnet-gold-win`, defined in `scenarios/aspnet.profiles.standard.yml`) before role-specific profile tuples.
- When the user is not on the benchmark network (or says relay), use the `-relay` twin of the profile together with `--relay` (bare `--relay` authenticates via `az login`); see `references/techempower-scenarios.md`.
- Include exactly the profiles needed to satisfy scenario roles and machine placement.
- Add `--json <file>` for every run meant to be compared later.
- Use `--application.framework net10.0|net11.0` as the normal version selector; pin SDK/runtime/aspnetcore versions only when reproducing a .NET regression.
- When pinning versions for regressions, prefer versions confirmed in package feed indexes rather than assuming all CI build labels were published.
- If benchmark history skips versions, fetch `.nuspec` metadata for missing versions to map to source commits before narrowing culprit changes.
- When using a git-based benchmark source, use `--application.source.branchOrCommit <branch_name>` or `--application.source.branchOrCommit #<commit_hash>` to target specific code.
- Add `--application.collectDependencies true` only for CI transitive dependency analysis.
- Add `build/azure.profile.yml` only when Azure machines/relay/auth-specific profiles are required.
- Prefer additive diagnostics (`--*.collectCounters`, traces, dumps) over changing scenario behavior.
- If result comparison is requested, include `--compare <prior.json>` arguments or a follow-up `crank compare` step.

## Reference loading

Read references based on task:

- `references/api_reference.md` for command argument semantics and repeatable command templates.
- `references/techempower-scenarios.md` for the TechEmpower Minimal APIs and Platform plaintext/json/fortunes scenarios, their config files, and CI trend names.

## Output expectations

- Return a final crank command that is directly runnable.
- Include short rationale for each `--profile` selected.
- Call out when optional version pins are intentionally omitted or included.
- Include output filenames for JSON persistence and comparison readiness.
