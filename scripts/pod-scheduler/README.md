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

```bash
# Show schedule summary
python main.py --config ../../build/benchmarks_ci_pods.json

# Generate pipeline YAML files
python main.py --config ../../build/benchmarks_ci_pods.json \
    --yaml-output ../../build

# Show which pods share machines
python main.py --config ../../build/benchmarks_ci_pods.json --show-conflicts

# List all runs without scheduling
python main.py --config ../../build/benchmarks_ci_pods.json --list-runs
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
            "estimated_runtime": 30.0
        }
    ]
}
```

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

### Scenario Types

| Type | Machines Used | Example |
|------|--------------|---------|
| 1 (SINGLE) | SUT only | Build, GC |
| 2 (DUAL) | SUT + Load | Baselines, Grpc, SignalR |
| 3 (TRIPLE) | SUT + Load + DB | Baselines Database, PGO, Proxies |

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
2. **Sort** runs by runtime descending (longest-job-first)
3. **Pack** into stages greedily — each run goes into the first stage where no
   physical machines conflict and the queue limit isn't exceeded
4. **Split** stages across multiple YAML files using bin-packing for balanced
   runtime

## Files

| File | Lines | Purpose |
|------|-------|---------|
| `main.py` | ~160 | CLI entry point, summary display |
| `models.py` | ~115 | Data classes (Pod, Scenario, Run, Stage, Schedule) |
| `scheduler.py` | ~95 | Scheduling algorithm |
| `config_loader.py` | ~50 | JSON config parser |
| `generator.py` | ~150 | YAML generation |

Total: ~570 lines (vs ~2000 in the full crank-scheduler)
