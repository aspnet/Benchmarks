# Crank PerfLab exporter

`Crank.PerfLabExporter` converts completed Crank `--json` output into the
PerfLab result contract. Crank continues writing its existing SQL rows; this
tool runs afterward and can optionally upload the converted report.

## Counter mapping

The default policy is
[`build/crank-perflab-counter-policy.json`](../../build/crank-perflab-counter-policy.json).
Each mapping identifies a top-level Crank result path and supplies the PerfLab
counter name, unit, direction, role, threshold, optional scale, and optional
excluded scenario names.

The initial monitored counters are requests/sec, mean latency, P99 latency,
startup time, and published size. Published size is scaled from KB to bytes.
Other finite numeric results are retained as non-top counters using their
fully qualified source path and the `value` unit. Objects, arrays,
distributions, and non-finite values are not emitted as counters.

## Convert locally

The Crank result must contain the same `perflab.*` properties supplied by the
Trend templates:

- `perflab.build.*` for the runtime repository and branch;
- `perflab.lane.*` and `perflab.configuration.*` for stable machine identity;
- `perflab.scenario.*` for test name, family, and categories;
- `perflab.azureDevOps.*`, `perflab.sql.*`, and
  `perflab.perfRepoHash`.

Runtime and ASP.NET Core commits are read from normalized Crank dependencies.
When the runtime commit timestamp is absent, the exporter resolves it through
the GitHub commits API.

```powershell
dotnet run --project src\Crank.PerfLabExporter -- convert `
  --crank-json artifacts\crank-results.json `
  --counter-policy build\crank-perflab-counter-policy.json `
  --output-directory artifacts
```

Current Crank JSON contains one aggregate value per result, so each PerfLab
counter contains one sample. Timestamped Crank measurements are recorded as
metadata but are not treated as independent samples.

## Upload

```powershell
dotnet run --project src\Crank.PerfLabExporter -- upload `
  --crank-json artifacts\crank-results.json `
  --counter-policy build\crank-perflab-counter-policy.json `
  --output-directory artifacts `
  --storage-account pvscmdupload `
  --container results `
  --queue resultsqueue
```

Storage authentication supports `default`, `managed-identity`, and
`certificate`. Certificate secrets are read from environment variables. The
blob name is deterministic, and the queue message matches
`performance/scripts/upload.py`:

```json
{"container_name": "results", "blob_name": "crank/.../report.perflab.json"}
```

## Controller afterJob

Trend loads the canonical
[`build/perflab.profile.yml`](../../build/perflab.profile.yml), pinned to the
same Benchmarks revision as the scenario configs, and selects `--profile perflab`
in its existing Crank invocation. The profile attaches one `perflab-export`
`afterJob` command to **application only**; it does not add benchmark runs or
hooks to load/database jobs. There is no worker `postProcess` payload.

Publication defaults to off. All current generated Trend callers explicitly
set `enablePerfLabPublication: false`, which becomes
`--variable perfLabPublication=false`. Enabling the caller changes only that
gate. The command exports only when publication is enabled and the Controller
supplies `result.returnCode == 0 && result.jobResults.jobs.Count > 0`.
Disabled, failed, and empty/skipped runs select explicit logging no-op commands,
so the command runner never sees an all-false selection on supported hosts.
Other `afterJob` cleanup is not globally suppressed.

Use a Controller version that exposes the final `ExecutionResult` as `result`
before running `afterJob`, after its final JSON writes. The exporter runs on
the **Controller/worker host**, selected by `job.environment.platform`, not the
remote SUT OS: Windows uses `powershell.exe`; Linux/macOS use Bash. It reads
`crank-results.json` in the worker attempt's working directory, not beside the
generated hook script. A nonzero exporter exit fails the command
(`continueOnError: false`) and is subject to the existing worker retry policy.
Export time is part of the existing job timeout, not a separate timeout budget.

Before enabling publication, deploy the exporter and policy to the worker.
Set the trusted worker environment variable
`CRANK_AZDO_POST_PROCESS_EXECUTABLE` to the exporter executable/path (no arguments).
The historical variable name is retained for deployment compatibility only;
the worker no longer interprets a post-process configuration. Do not put an
executable path or credential values in a Service Bus message.

Existing Trend parameters remain supported via profile variables: policy path,
storage account/container/queue, Crank version environment-variable name, and
the four certificate-authentication environment-variable names. Defaults remain
`crank-perflab-counter-policy.json`, `pvscmdupload`, `results`, `resultsqueue`,
`CRANK_VERSION`, and the `PERFLAB_UPLOAD_*` names declared in the profile.
Shell arguments are single-quoted with embedded apostrophes escaped for each
shell; only environment-variable **names**, never credential values, enter the
profile scripts. Credentials are read by the external exporter.

Local `convert` and optional `upload` remain independent exporter commands;
neither conversion nor upload logic is built into the Controller.

## Build and test

```powershell
dotnet test src\Crank.PerfLabExporter.Tests -c Release
dotnet pack src\Crank.PerfLabExporter -c Release
```
