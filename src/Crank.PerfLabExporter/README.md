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

## Worker post-process

Trend carries a disabled-by-default Crank worker post-process:

```json
{
  "postProcess": {
    "enabled": false,
    "name": "Crank PerfLab export",
    "args": [
      "upload",
      "--crank-json", "crank-results.json",
      "--counter-policy", "crank-perflab-counter-policy.json",
      "--identity-source", "crank",
      "--identity-property-prefix", "perflab.",
      "--crank-version-environment-variable", "CRANK_VERSION",
      "--storage-account", "pvscmdupload",
      "--container", "results",
      "--queue", "resultsqueue",
      "--storage-authentication", "certificate",
      "--tenant-id-environment-variable", "PERFLAB_UPLOAD_TENANT_ID",
      "--client-id-environment-variable", "PERFLAB_UPLOAD_CLIENT_ID",
      "--certificate-base64-environment-variable", "PERFLAB_UPLOAD_CERTIFICATE_BASE64",
      "--certificate-password-environment-variable", "PERFLAB_UPLOAD_CERTIFICATE_PASSWORD"
    ]
  }
}
```

Only environment-variable names cross Service Bus. Publication remains off
until `enablePerfLabPublication` is explicitly enabled in a Trend caller.

## Build and test

```powershell
dotnet test src\Crank.PerfLabExporter.Tests -c Release
dotnet pack src\Crank.PerfLabExporter -c Release
```
