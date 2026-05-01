# Pod-Based Crank Scheduler

A simplified scheduler for assigning benchmark scenarios to machine "pods" and
generating Azure DevOps pipeline YAML files.

## Concept

A **pod** is a fixed group of machines that always run together:
- **SUT** (System Under Test) — required
- **Load** generator — optional, for dual/triple scenarios
- **DB** (Database) — optional, for triple scenarios

Pods that share physical machines (e.g., two pods using the same DB machine)
cannot run in the same stage. The scheduler handles this automatically.

## Quick Start

Run from the repository root:

```bash
# Show schedule summary
python scripts/pod-scheduler/main.py --config build/benchmarks_ci_pods.json

# Generate pipeline YAML files
python scripts/pod-scheduler/main.py \
    --config build/benchmarks_ci_pods.json \
    --yaml-output build

# Regenerate the Azure or Cobalt pipelines
python scripts/pod-scheduler/main.py \
    --config build/benchmarks_ci_azure_pods.json \
    --base-name benchmarks-ci-azure --yaml-output build

python scripts/pod-scheduler/main.py \
    --config build/benchmarks_ci_cobalt_pods.json \
    --base-name benchmarks-ci-cobalt --yaml-output build

# Show which pods share machines
python scripts/pod-scheduler/main.py --config build/benchmarks_ci_pods.json --show-conflicts

# List all runs without scheduling
python scripts/pod-scheduler/main.py --config build/benchmarks_ci_pods.json --list-runs
```

The header of every generated YAML embeds the exact regen command for that
file, so you can also copy the command from there.

The scheduler runs in **strict mode** by default: any unknown pod or invalid
pod-for-scenario reference fails with a non-zero exit code so config typos
cannot silently drop scenarios from the pipeline. Pass `--lenient` to fall
back to the previous warn-and-skip behavior.

Output is **deterministic**: identical input JSON always produces identical
YAML, so regenerations diff cleanly. To verify, run the snapshot tests:

```bash
cd scripts/pod-scheduler
python -m unittest discover tests
```

## Configuration Format

```json
{
    "metadata": {
        "name": "Config Name",
        "schedule": "0 3/12 * * *",
        "queues": ["citrine1", "citrine2", "citrine3", "mono"],
        "yaml_generation": {
            "target_yaml_count": 2,
            "schedule_offset_hours": 6
        },
        "pipeline": {
            "pool": "server",
            "service_bus_connection": "ASPNET Benchmarks Service Bus",
            "service_bus_namespace": "aspnetbenchmarks"
        }
    },
    "pods": [
        {
            "name": "gold-lin",
            "machines": { "sut": "gold-lin", "load": "gold-load", "db": "gold-db" },
            "profiles": { "sut": "gold-lin-app", "load": "gold-load-load", "db": "gold-db-db" }
        }
    ],
    "scenarios": [
        {
            "name": "Baselines",
            "template": "baselines-scenarios.yml",
            "type": 2,
            "pods": ["gold-lin", "gold-win"],
            "estimated_runtime": 30.0,
            "timeout": 120
        }
    ]
}
```

The `pipeline` block is optional; defaults match the legacy hardcoded values.

The `schedule` field's **hour** must be a `H` or `H/N` cron expression
(e.g. `3` or `3/12`). Lists, ranges, and `*` are rejected at load time so the
hour-offset used for split YAMLs cannot silently no-op.

### Pod Definition

| Field | Description |
|-------|-------------|
| `name` | Unique identifier for the pod |
| `machines.sut` | Physical machine name for SUT role |
| `machines.load` | Physical machine name for Load role (optional) |
| `machines.db` | Physical machine name for DB role (optional) |
| `profiles.sut` | Crank profile name for SUT |
| `profiles.load` | Crank profile name for Load (optional) |
| `profiles.db` | Crank profile name for DB (optional) |

### Scenario Definition

| Field | Description |
|-------|-------------|
| `name` | Display name (also used as part of the AzDO job id) |
| `template` | YAML scenario template to invoke |
| `type` | 1=SINGLE, 2=DUAL, 3=TRIPLE (see below) |
| `pods` | List of pod names this scenario targets (no duplicates) |
| `estimated_runtime` | Runtime estimate in minutes; defaults per type if omitted |
| `timeout` | Optional explicit AzDO `timeoutInMinutes` override. When unset, the generator picks `max(120, min(240, ceil(2 * estimated_runtime)))` |

### Scenario Types

| Type | Machines Used | Example |
|------|--------------|---------|
| 1 (SINGLE) | SUT only | Build, GC |
| 2 (DUAL) | SUT + Load | Baselines, Grpc, SignalR |
| 3 (TRIPLE) | SUT + Load + DB | Baselines Database, PGO, Proxies |

### Queue Assignment

The N-th run within a stage is assigned to `queues[N % len(queues)]`. Queues
are treated as interchangeable workers; if a queue is pinned to specific
hardware in your service-bus topology, set `metadata.queues` accordingly so
the order matches your hardware layout.

### Handling Shared Machines

Two pods can share load/DB machines. For example:
- `gold-lin` pod: SUT=gold-lin, Load=gold-load, DB=gold-db
- `gold-win` pod: SUT=gold-win, Load=gold-load2, DB=gold-db

These pods share `gold-db`. When both run type-3 scenarios, they cannot be in
the same stage. When `gold-win` runs a type-2 scenario (no DB), there's no
conflict.

### Future: Multiple SUTs per Class

If you get 2 SUT machines of the same class (e.g., gold-lin-1 and gold-lin-2),
create separate pods for each. They can share load/DB:

```json
{"name": "gold-lin-1", "machines": {"sut": "gold-lin-1", "load": "gold-load", "db": "gold-db"}, ...},
{"name": "gold-lin-2", "machines": {"sut": "gold-lin-2", "load": "gold-load", "db": "gold-db"}, ...}
```

The scheduler automatically prevents them from running simultaneously when they
share load/DB machines.

## Algorithm

1. **Expand** each scenario × pod into individual "runs"
2. **Sort** runs by runtime descending (longest-job-first), with the run name
   as a stable tie-breaker so output is deterministic
3. **Pack** into stages greedily — each run goes into the first stage where no
   physical machines conflict and the queue limit isn't exceeded
4. **Split** stages across multiple YAML files using bin-packing for balanced
   runtime, restoring the original stage order within each bin

## Files

| File | Purpose |
|------|---------|
| `main.py` | CLI entry point, summary display |
| `models.py` | Data classes (Pod, Scenario, Run, Stage, Schedule, PipelineSettings) |
| `scheduler.py` | Scheduling algorithm |
| `config_loader.py` | JSON config parser + validation |
| `generator.py` | YAML generation |
| `tests/` | Unit + snapshot tests (`python -m unittest`) |

This is intentionally script-style: the modules use absolute imports
(`from models import …`) and are run as `python main.py …`. To run the
tests, `cd scripts/pod-scheduler && python -m unittest discover tests`.

## Tradeoffs vs. the full crank-scheduler

By collapsing capabilities, priorities, preferred-partners, and machine groups
into fixed pod definitions, this scheduler is much smaller — but loses some
expressivity. If the hardware layout grows beyond "fixed SUT + load + DB
triples", the constraint solver from the full crank-scheduler may be a better
fit. For today's hardware, the simplification is intentional.
