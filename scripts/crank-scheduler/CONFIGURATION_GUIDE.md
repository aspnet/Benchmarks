# Complete Configuration Options Guide

This guide demonstrates all possible configuration options using `example_complete_features.json`.

## Machine Configuration Options

### 1. Single-Type Machine (Traditional)

```json
{
  "name": "single-type-machine",
  "capabilities": {
    "sut": {
      "priority": 1,
      "profiles": ["single-type-basic"],
      "default_profile": "single-type-basic"
    }
  },
  "preferred_partners": ["dedicated-load", "dedicated-db"]
}
```

**Features:**

- ✅ One machine type only (SUT)
- ✅ Single profile available
- ✅ Preferred partners for load/db roles

### 2. Multi-Type Machine (Advanced)

```json
{
  "name": "multi-type-machine",
  "capabilities": {
    "sut": {
      "priority": 1,
      "profiles": [
        "multi-sut-normal",
        "multi-sut-high-cpu", 
        "multi-sut-low-memory"
      ],
      "default_profile": "multi-sut-normal"
    },
    "load": {
      "priority": 2,
      "profiles": [
        "multi-load-normal",
        "multi-load-high-throughput",
        "multi-load-burst-mode"
      ],
      "default_profile": "multi-load-normal"
    },
    "db": {
      "priority": 3,
      "profiles": [
        "multi-db-normal",
        "multi-db-memory-optimized"
      ],
      "default_profile": "multi-db-normal"
    }
  }
}
```

**Features:**

- ✅ Multiple machine types (SUT, LOAD, DB)
- ✅ Priority ordering (1=preferred, 2=secondary, 3=fallback)
- ✅ Multiple sub-profiles per type
- ✅ Default profile for each type

### 3. Specialized Machine

```json
{
  "name": "dedicated-load",
  "capabilities": {
    "load": {
      "priority": 1,
      "profiles": [
        "dedicated-load-standard",
        "dedicated-load-high-connections",
        "dedicated-load-low-latency"
      ],
      "default_profile": "dedicated-load-standard"
    }
  }
}
```

**Features:**

- ✅ Dedicated to one role (LOAD only)
- ✅ Multiple specialized profiles
- ✅ No preferred partners needed

### 4. Specialized Machines (Dedicated Role)

```json
{
  "name": "dedicated-db",
  "capabilities": {
    "db": {
      "priority": 1,
      "profiles": [
        "dedicated-db-standard",
        "dedicated-db-high-iops",
        "dedicated-db-large-dataset"
      ],
      "default_profile": "dedicated-db-standard"
    }
  },
  "preferred_partners": []
}
```

**Features:**

- ✅ Dedicated to one role (DB only)
- ✅ Multiple specialized profiles for different workloads
- ✅ No preferred partners needed (self-contained)

## Scenario Configuration Options

### 1. Basic Scenario (Default Profiles)

```json
{
  "name": "Simple Single Machine Test",
  "template": "simple-single.yml",
  "scenario_type": 1,
  "target_machines": ["single-type-machine", "multi-type-machine"],
  "estimated_runtime": 10.0,
  "description": "Basic single machine scenario with default profiles"
}
```

**Result:** Uses default profiles for all machines

### 2. Custom Profile Selection

```json
{
  "name": "Triple Machine Test with Custom Profiles",
  "template": "triple-custom.yml", 
  "scenario_type": 3,
  "target_machines": ["multi-type-machine"],
  "estimated_runtime": 45.0,
  "profile_overrides": {
    "multi-type-machine": {
      "sut": "multi-sut-high-cpu",
      "load": "multi-load-high-throughput",
      "db": "multi-db-memory-optimized"
    }
  }
}
```

**Result:** Uses specific custom profiles for each machine type

### 3. Mixed Profile Usage

```json
{
  "name": "Mixed Profile Scenario",
  "template": "mixed-profiles.yml",
  "scenario_type": 2,
  "target_machines": ["single-type-machine", "multi-type-machine"],
  "profile_overrides": {
    "multi-type-machine": {
      "sut": "multi-sut-low-memory"
    }
  }
}
```

**Result:**

- `single-type-machine`: Uses default profile
- `multi-type-machine` SUT: Uses custom profile
- `multi-type-machine` LOAD: Uses default profile

## Configuration Properties Explained

### Machine Properties

| Property             | Required | Description                                    |
| -------------------- | -------- | ---------------------------------------------- |
| `name`               | ✅        | Unique machine identifier                      |
| `capabilities`       | ✅        | Dict of machine types this machine can fulfill |
| `preferred_partners` | ❌        | List of preferred machines for other roles     |

### Capability Properties

| Property          | Required | Description                                                         |
| ----------------- | -------- | ------------------------------------------------------------------- |
| `machine_type`    | ✅        | Key: "sut", "load", or "db"                                         |
| `priority`        | ✅        | 1=preferred, 2=secondary, 3=fallback                                |
| `profiles`        | ✅        | List of available profile names                                     |
| `default_profile` | ❌        | Which profile to use by default (defaults to first profile in list) |

### Scenario Properties

| Property            | Required | Description                        |
| ------------------- | -------- | ---------------------------------- |
| `name`              | ✅        | Scenario identifier                |
| `template`          | ✅        | YAML template file                 |
| `scenario_type`     | ✅        | 1=single, 2=dual, 3=triple machine |
| `target_machines`   | ✅        | List of machines to run on         |
| `estimated_runtime` | ❌        | Runtime in minutes                 |
| `description`       | ❌        | Human-readable description         |
| `profile_overrides` | ❌        | Custom profile overrides           |

### Profile Overrides Structure

```json
"profile_overrides": {
  "machine-name": {
    "machine-type": "profile-name"
  }
}
```

## Testing the Configuration

Run these commands to test the example:

```bash
# Test configuration loading and basic validation
python main.py --config example_complete_features.json --list-jobs

# Test scheduling without YAML generation
python main.py --config example_complete_features.json

# Test with template and YAML generation
python main.py --config example_complete_features.json --template benchmarks.template.liquid --yaml-output ./output

# List jobs grouped by target machine
python main.py --config example_complete_features.json --list-jobs-by-machine
```

## Key Features Demonstrated

1. ✅ **Multi-type machines** - One machine can be SUT, LOAD, and DB
2. ✅ **Priority-based selection** - Preferred roles vs fallback roles  
3. ✅ **Sub-profile support** - Multiple profiles per machine type
4. ✅ **Default behavior** - No configuration change needed for existing setups
5. ✅ **Custom overrides** - Specific profiles for specific scenarios
6. ✅ **Backward compatibility** - Old single-type machines still work
7. ✅ **Flexible scheduling** - Scheduler automatically picks best assignments
