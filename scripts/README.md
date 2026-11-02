# Scripts

## Opt-in RPS per CPU metrics

Add [`rps-per-cpu.yml`](rps-per-cpu.yml) to an HTTP benchmark and select `--script rps-per-cpu`. Loading the config without selecting a script does not change the results or require CPU measurements. The named script uses the scenario job names `application` and `load`, not their underlying job definitions.

From the root of a local checkout, for example:

```powershell
crank --config .\scenarios\plaintext.benchmarks.yml --scenario plaintext --profile local --config .\scripts\rps-per-cpu.yml --script rps-per-cpu --json results.json
```

For a pinned remote config, replace `<commit-sha>` with the full Benchmarks commit SHA containing the helper (and choose the appropriate machine profile):

```powershell
crank --config .\scenarios\plaintext.benchmarks.yml --scenario plaintext --profile local --config https://raw.githubusercontent.com/aspnet/Benchmarks/<commit-sha>/scripts/rps-per-cpu.yml --script rps-per-cpu --json results.json
```

The script adds two results to the application job, with console labels and JSON metadata, without changing existing peak CPU results:

| Result key | Calculation |
| --- | --- |
| `http/rps/per-process-cpu` | Load `http/rps/mean` / arithmetic mean of application `benchmarks/cpu` samples |
| `http/rps/per-machine-cpu` | Load `http/rps/mean` / arithmetic mean of application `benchmarks/cpu/global` samples |

Both units are **RPS per CPU percentage point**, not requests per CPU second. For example, 10,000 RPS / 25% CPU = 400 RPS per percentage point; the denominator is 25, not 0.25. Process CPU is normalized to 0-100 by the host logical CPU count or the configured `cpuSet` size. Machine CPU is host-wide, not normalized to that process's `cpuSet`. Different host CPU counts or `cpuSet` sizes therefore change the meaning of the process ratio; compare only compatible configurations. Machine CPU includes unrelated work and any colocated load generator.

The means use all retained samples independently for each CPU metric, not their maxima or a time-weighted average. Those samples can include startup/warmup and need not cover the same window as load RPS. Ensure the retained CPU samples and load measurement window are comparable before interpreting the ratios. In particular, the current Linux Crank machine-CPU collector runs `vmstat 1 100` (100 reports), so long benchmarks may have machine samples covering only part of the load phase.

Missing jobs, missing samples, nonnumeric/nonfinite/negative inputs, zero CPU means, and nonfinite ratios fail with an explicit error. Individual zero CPU samples and zero RPS are valid when both CPU means are positive. The application job must have exactly one endpoint: the helper rejects zero or multiple endpoint measurement sets rather than assigning shared throughput to an arbitrary application's CPU. The numerator is the load job's already aggregated/reduced `http/rps/mean`; that throughput must correspond to the selected application.

For other scenario job names, add a named script in your own config and invoke the reusable helper:

```yaml
scripts:
  api-rps-per-cpu: |
    require("rps-per-cpu-helper");
    addRpsPerCpu("api", "traffic");
```

Load both configs and select your script instead of `rps-per-cpu`:

```powershell
crank --config .\my-benchmark.yml --scenario my-scenario --profile my-profile --config .\scripts\rps-per-cpu.yml --script api-rps-per-cpu --json results.json
```

The helper relies on Crank's automatically loaded `default.config.yml` for `avg`. Named scripts run after `onResultsCreated`, while measurements and metadata are still available; do not remove or rewrite the needed samples in an earlier hook. `--no-measurements` and `--no-metadata` remove those fields from saved results after scripts run.
