# Crank Scheduler

A sophisticated scheduling system for managing performance test scenario execution across multiple machines with various constraints and preferences. This component is designed to be integrated into larger performance testing infrastructure projects.

## Overview

The Crank Scheduler solves the complex problem of optimally scheduling performance test scenarios across a fleet of machines while respecting:

- Machine type constraints and multi-capability support (SUT, Load, DB)
- Machine preferences and partnerships
- Runtime optimization to minimize idle time
- Stage-based execution where all scenarios in a stage must complete before the next stage begins
- Queue-based load balancing across multiple execution pipelines
- YAML generation for CI/CD pipeline integration

## Features

- **Multi-Capability Machine Support**: Machines can have multiple capabilities (SUT, Load, DB) with priority-based assignments
- **Smart Machine Allocation**: Respects machine preferences while falling back gracefully to available alternatives
- **Runtime Estimation**: Automatically estimates runtimes for unknown scenarios based on similar ones
- **Stage Optimization**: Groups scenarios to minimize total execution time
- **Queue-Based Load Balancing**: Distributes work across multiple execution queues
- **Liquid Template Integration**: Generates YAML configurations for CI/CD pipelines using templates
- **Multiple Input/Output Formats**: JSON, CSV, YAML support
- **Schedule Splitting**: Divides large schedules into manageable chunks for parallel execution
- **Extensible Design**: Easy to add new optimization algorithms and constraints

## Installation

As a nested component, ensure Python 3 (latest recommended) and the requirements are installed:

```bash
pip install -r requirements.txt
```

## Quick Start

The scheduler uses a combined configuration format that includes machines, scenarios, and metadata in a single JSON file.

### Basic Usage

```bash
# Generate schedule from combined configuration
python main.py --config config.json

# Generate YAML files for CI/CD pipelines
python main.py --config config.json --template benchmarks.template.liquid --yaml-output ./output

# List all jobs without scheduling
python main.py --config config.json --list-jobs

# List jobs grouped by machine
python main.py --config config.json --list-jobs-by-machine
```

## Configuration

The scheduler uses a combined configuration format that includes machines, scenarios, and metadata. See `example_complete_features.json` for a comprehensive example.

### Combined Configuration Structure

```json
{
  "metadata": {
    "name": "Configuration Name",
    "description": "Description of the configuration",
    "version": "2.0",
    "schedule": "0 6/12 * * *",
    "queues": ["queue1", "queue2"],
    "yaml_generation": {
      "target_yaml_count": 2,
      "schedule_offset_hours": 6
    }
  },
  "machines": [...],
  "scenarios": [...]
}
```

### Machines Configuration

Machines support multiple capabilities with priority-based assignment:

```json
{
  "name": "multi-capability-machine",
  "capabilities": {
    "sut": {
      "priority": 1,
      "profiles": ["sut-profile-1", "sut-profile-2"],
      "default_profile": "sut-profile-1"
    },
    "load": {
      "priority": 2,
      "profiles": ["load-profile"]
    }
  },
  "preferred_partners": ["partner-machine-1", "partner-machine-2"]
}
```

#### Machine Properties

- **name**: Unique machine identifier
- **capabilities**: Dictionary of machine types with their configurations
  - **priority**: Assignment priority (1 = highest priority)
  - **profiles**: Available profiles for this capability
  - **default_profile**: Default profile to use (optional, defaults to first profile)
- **preferred_partners**: Ordered list of preferred partner machines

### Scenarios Configuration

```json
{
  "name": "performance-test-scenario",
  "scenario_type": 2,
  "estimated_runtime": 45.0,
  "target_machines": ["machine-1", "machine-2"]
}
```

#### Scenario Properties

- **name**: Scenario identifier
- **scenario_type**: Number of machines required (1=SUT only, 2=SUT+Load, 3=SUT+Load+DB)
- **estimated_runtime**: Runtime in minutes (optional)
- **target_machines**: List of specific machines to run this scenario on

## Usage Examples

### Basic Scheduling

```bash
# Generate schedule and display summary
python main.py --config config.json

# Generate YAML files for CI/CD integration
python main.py --config config.json --template benchmarks.template.liquid --yaml-output ./output
```

### Analysis and Debugging

```bash
# List all jobs without executing scheduler
python main.py --config config.json --list-jobs

# List jobs grouped by target machine
python main.py --config config.json --list-jobs-by-machine
```

## Architecture

### Core Components

1. **Models** (`models.py`): Data structures for machines, scenarios, schedules, and configurations
2. **Scheduler** (`scheduler.py`): Core scheduling algorithms and machine allocation logic
3. **Utils** (`utils.py`): Input/output utilities and data format conversion
4. **Template Generator** (`template_generator.py`): Liquid template processing for YAML generation
5. **Schedule Operations** (`schedule_operations.py`): Schedule processing and YAML generation workflows
6. **Schedule Splitter** (`schedule_splitter.py`): Logic for dividing schedules into manageable chunks
7. **Main** (`main.py`): Command-line interface and orchestration

### Key Classes

- **Machine**: Represents a physical machine with multiple capabilities and preferences
- **Scenario**: Represents a performance test scenario with requirements and target machines
- **MachineAllocator**: Handles machine assignment logic with preference matching and capability-based selection
- **CrankScheduler**: Main scheduler that creates optimized schedules with stage-based execution
- **ScheduleSplitter**: Divides large schedules into smaller, parallelizable units

### Scheduling Algorithm

1. **Expand scenarios** into individual runs for each target machine
2. **Estimate runtimes** for scenarios without known values using similarity-based estimation
3. **Sort scenarios** by runtime (descending) to minimize idle time using longest-job-first heuristic
4. **Create stages** by packing scenario runs optimally:
   - Try to assign each scenario to available machines respecting capabilities and preferences
   - Ensure no machine conflicts within a stage (each machine used only once per stage)
   - Balance load across multiple queues within each stage
5. **Split schedule** into manageable chunks for parallel execution
6. **Generate YAML** configurations for CI/CD pipeline integration

## Machine Preference System

The scheduler uses a sophisticated multi-tiered machine matching system:

1. **Capability-Based Matching**: Match machines based on their defined capabilities and priorities
2. **Explicit Preferences**: Use machines specified in `preferred_partners` list
3. **Priority-Based Fallback**: Select machines based on capability priority levels
4. **Type-Based Matching**: Match by machine type when specific preferences aren't available
5. **Graceful Degradation**: Use any available machine of the correct type as last resort

### Example Preference Logic

```text
Scenario needs a load machine for target "gold-lin":
1. Check gold-lin's preferred_partners for load-capable machines
2. Try load machines in preference order
3. Fall back to any available load-capable machine by priority
4. Fail if no suitable machines are available
```

## Output Formats

### Table Format (Default)

Human-readable summary with stage breakdown, machine utilization, and execution plans.

### JSON Format

Structured data suitable for programmatic consumption:

```json
{
  "total_estimated_time": 120.0,
  "stages": [
    {
      "stage_id": 0,
      "estimated_duration": 60.0,
      "assignments": [...]
    }
  ]
}
```

### CSV Format

Flat format suitable for spreadsheet analysis:

```csv
stage_id,queue_id,scenario,sut_machine,load_machine,db_machine,estimated_runtime,stage_duration
0,0,plaintext-gold-lin,gold-lin,gold-load,,35.0,60.0
```

### YAML Format

Generated CI/CD pipeline configurations using Liquid templates for integration with build systems.

## Integration as a Nested Component

This scheduler is designed to be integrated into larger performance testing infrastructure projects. Key integration points:

- **Configuration Management**: Use combined JSON configuration format for easy integration
- **Template System**: Leverage Liquid templates for generating custom CI/CD configurations
- **Modular Design**: Import and use individual components (`CrankScheduler`, `ScheduleSplitter`, etc.)
- **Extensible Architecture**: Add custom machine types and scenario types
