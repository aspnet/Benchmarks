# JSON benchmark example from aspnet/Benchmarks

Source file: `build/benchmarks_ci.json` in `aspnet/Benchmarks`.

## Why this file matters

This JSON file defines benchmark machine capabilities and scenario templates used for CI orchestration. Use it to reason about profile naming conventions and role placement strategy.

## Example excerpt

```json
{
  "metadata": {
    "name": "CI Benchmarks Configuration",
    "queues": ["citrine1", "citrine2", "citrine3", "mono"]
  },
  "machines": [
    {
      "name": "intel-load2",
      "capabilities": {
        "load": { "priority": 1, "profiles": ["intel-load2-load"] },
        "sut": { "priority": 2, "profiles": ["intel-load2-app"] },
        "db": { "priority": 3, "profiles": ["intel-load2-db"] }
      }
    },
    {
      "name": "intel-db",
      "capabilities": {
        "db": { "priority": 1, "profiles": ["intel-db-db"] },
        "sut": { "priority": 2, "profiles": ["intel-db-app"] },
        "load": { "priority": 3, "profiles": ["intel-db-load"] }
      }
    }
  ]
}
```

## How to apply this in crank commands

- Pick profiles by role suffix:
  - `*-app` for `application` (SUT)
  - `*-load` for load generator
  - `*-db` for database/service role
- Combine profiles to isolate roles on separate machines.
- Pair this profile strategy with scenario definitions from YAML configs (`--config ...yml`).
