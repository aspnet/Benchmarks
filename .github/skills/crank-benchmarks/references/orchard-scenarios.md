# Orchard Core crank scenarios

## Source scenario

From `aspnet/Benchmarks`:

- `scenarios/orchard.benchmarks.yml`
- common profiles such as `build/ci.profile.yml` and `scenarios/steadystate.profile.yml`
- add `build/azure.profile.yml` only when Azure machine profiles/relay/auth are required

## Role model in Orchard scenarios

`about-sqlite`:
- `application`: Orchard app job (`orchard`)
- `load`: load generator (`bombardier`)
- `db`: not required for sqlite scenario profile

`about-postgresql`:
- `db`: PostgreSQL service
- `application`: Orchard app job (`orchard`)
- `load`: load generator (`bombardier`)

Prefer deterministic profile groups with one argument:
- `aspnet-gold-lin`
- `aspnet-perf-win`

Use role-specific profile tuples only when custom machine placement is needed:
- `gold-win-app` for application
- `gold-load2-load` for load
- `gold-db-db` for db

## Orchard-focused baseline command

```bash
crank \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/orchard.benchmarks.yml \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/build/ci.profile.yml \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/scenarios/steadystate.profile.yml \
  --scenario about-sqlite \
  --profile aspnet-gold-lin \
  --application.noGlobalJson false \
  --application.framework net11.0 \
  --application.options.collectCounters true \
  --json orchard-about-sqlite.json
```

## Practical guidance

- Keep scenario unchanged when measuring deltas; vary profiles/versions one factor at a time.
- Use framework selection (`net10.0`, `net11.0`) by default; pin sdk/runtime/aspnetcore versions only for regression investigations.
- For git source selection, use `--application.source.branchOrCommit <branch_name>` or `--application.source.branchOrCommit #<commit_hash>`.
- `--application.collectDependencies true` is typically CI-only for dependency change tracking.
- `--application.noGlobalJson false` is OrchardCore-specific and should not be generalized.
- Persist each run with a unique JSON filename to enable reliable compare workflows.
