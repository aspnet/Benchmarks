# Collecting profiles, traces and dumps

Source: crank controller README (`src/Microsoft.Crank.Controller/README.md`, "Diagnostics" section). Diagnostics are job-level options: replace `[JOB]` with `application` (usually), `load` or `db`.

Do not confuse `--application.profile true` (collect a profile of the job) with `--profile <name>` (a machine/config profile such as `aspnet-gold-lin`). Both can be in the same command.

## CPU profile / trace (dotnet-trace)

```bash
crank \
  --config https://raw.githubusercontent.com/aspnet/Benchmarks/main/src/BenchmarksApps/TechEmpower/Minimal/minimal.benchmarks.yml \
  --scenario json \
  --profile aspnet-gold-lin \
  --application.framework net11.0 \
  --application.profile true \
  --application.options.traceOutput minimal-json \
  --json minimal-json-trace.json
```

- `--[JOB].profile true`: collect a profile. `--[JOB].profileType ultra|dotnet-trace|perfview` picks the tool (default `dotnet-trace`).
- `--[JOB].dotnetTrace true`: collect a dotnet-trace EventPipe trace directly.
- `--[JOB].dotnetTraceProviders "<profile|provider|clrevent>,..."`: default is the `cpu-sampling` profile. Examples: `gc-verbose`, `Microsoft-DotNETCore-SampleProfiler`, `JIT+Contention`.
- `--[JOB].dotnetTraceBufferSizeMB <n>`: EventPipe buffer, default 256; raise it if events are dropped (for example allocation sampling).
- `--[JOB].dotnetTraceRequestRundown false`: shorter stop on long runs, at the cost of symbol rundown.
- `--[JOB].dotnetTraceCollectMode default|collect|collect-linux`: `collect` shells out to the dotnet-trace CLI; `collect-linux` does perf_event-based whole-machine sampling with kernel and native frames (Linux only, needs root and kernel >= 6.4, fails hard if prerequisites are missing). `--[JOB].dotnetTraceStopTimeoutSec <s>` sets the finalize grace period for those two modes.
- `--[JOB].collectStartup true`: include the startup phase (before the app is ready) in the trace.
- `--[JOB].options.traceOutput <name>`: trace file name. A prefix gets `*.DATE*.zip` appended; a specific path is used as is. The trace is downloaded to the machine running `crank`.

## Native traces (PerfView / perf)

- `--[JOB].collect true`: PerfView on Windows, perf/PerfCollect on Linux.
- `--[JOB].collectArguments "<args>"`: default `BufferSizeMB=1024;CircularMB=4096;TplEvents=None;Providers=Microsoft-Diagnostics-DiagnosticSource:0:0;KernelEvents=default+ThreadTime-NetworkTCPIP`; append `;GcOnly` for GC-only traces.

## Counters

- `--[JOB].options.collectCounters true`: collects `System.Runtime` by default.
- `--[JOB].options.counterProviders <provider>` (repeatable), e.g. `Microsoft-AspNetCore-Server-Kestrel`, `Microsoft.AspNetCore.Hosting`, `Npgsql`.

## Dumps and logs

- `--[JOB].options.dumpType full|heap|mini` with `--[JOB].options.dumpOutput <name>`.
- `--[JOB].options.downloadOutput true` with `--[JOB].options.downloadOutputOutput <name>`: job output log.
- `--[JOB].options.downloadBuildLog true`: build log.
- `--[JOB].additionalProcesses <name>`: also record CPU usage for another process.

## Guidance

- Collect traces in a separate run from the measurement run: profiling adds overhead and would skew the comparable results. Keep the same scenario, profile and framework for both.
- Use a unique `--options.traceOutput` name per run, and mention the output file name in the answer.
