#!/usr/bin/env python3
"""
Crank Scheduler - A scheduling system for machine/scenario assignments

This tool helps schedule scenarios across multiple machines with various constraints:
- Machine preferences and types
- Runtime optimization
- Queue management
- Stage-based execution
"""

import argparse
import sys
from pathlib import Path
from typing import List
from models import Machine, Scenario, Schedule, CombinedConfiguration, MachineType
from scheduler import CrankScheduler
from utils import DataLoader, ScheduleExporter
from template_generator import TemplateCLI
from schedule_operations import ScheduleOperations
from schedule_splitter import ScheduleSplitter, MultiYamlSummary



def main():
    parser = argparse.ArgumentParser(
        description="Crank Scheduler - Optimize scenario scheduling across machines",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  # Generate schedule from JSON files
  python main.py --config config.json --format table
        """
    )
    
    # Input options
    input_group = parser.add_argument_group('Input Options')
    input_group.add_argument('-c', '--config', type=str, help='Path to combined configuration file (machines + scenarios + metadata) (JSON)')
    
    # Output options
    output_group = parser.add_argument_group('Output Options')
    output_group.add_argument('--summary-output', type=str, help='Summary Output file path (default: print to console)')
    output_group.add_argument('--yaml-output', type=str, help='Output directory for YAML files (default: current directory)')

      # Scheduling options
    schedule_group = parser.add_argument_group('Scheduling Options')
    schedule_group.add_argument('--list-jobs', action='store_true',
                               help='List all jobs that will be scheduled without running the scheduler')
    schedule_group.add_argument('--list-jobs-by-machine', action='store_true',
                               help='List all jobs grouped by target SUT machine')
    
    # Add template arguments
    TemplateCLI.add_template_arguments(parser)
    
    args = parser.parse_args()
    
    # Load data
    try:
        machines, scenarios, config = load_data(args)
    except Exception as e:
        print(f"Error loading data: {e}", file=sys.stderr)
        return 1
    
    if not machines:
        print("Error: No machines loaded", file=sys.stderr)
        return 1
    
    if not scenarios:
        print("Error: No scenarios loaded", file=sys.stderr)
        return 1
      
    # Handle list-jobs option
    if args.list_jobs:
        list_scheduled_jobs(scenarios, machines)
        return 0
    
    # Handle list-jobs-by-machine option
    if args.list_jobs_by_machine:
        list_jobs_by_machine(scenarios, machines)
        return 0
    
    # Create scheduler and generate schedule
    print(f"Creating schedule for {len(scenarios)} scenarios on {len(machines)} machines...")
    scheduler = CrankScheduler(machines, scenarios, config)
    schedule = scheduler.create_schedule()
    
    splitter = ScheduleSplitter(config)
    partial_schedules = splitter.split_schedule(schedule)    
    print(f"Schedule split into {len(partial_schedules)} part(s)")
    
    # Process template/YAML generation if template is specified
    if args.template:
        yamls_generated = ScheduleOperations.process_yaml_generation(args, partial_schedules, config)

                # Print summary
        summary = MultiYamlSummary(yamls_generated, len(config.scenarios), partial_schedules)
        summary.print_summary()
        summary.print_execution_plans()
        
        if not yamls_generated:
            raise RuntimeError("No YAML files were generated. Check your template and configuration.")
    else:
        # No template specified, just show the partial schedules
        ScheduleOperations.show_partial_schedules(partial_schedules, config)
    return 0


def load_data(args) -> tuple[List[Machine], List[Scenario], CombinedConfiguration]:
    """Load machine and scenario data based on arguments"""
        
    # Try combined configuration first
    config_path = Path(args.config)
    if not config_path.exists():
        raise FileNotFoundError(f"Combined config file not found: {config_path}")
    
    config = DataLoader.load_combined_configuration(str(config_path))
    return config.machines, config.scenarios, config

def export_schedule(schedule: Schedule, args):
    """Export schedule in the specified format"""
    content = ScheduleExporter.to_summary_table(schedule)
    
    # Output to file or console
    if args.summary_output:
        with open(args.summary_output, 'w') as f:
            f.write(content)
        print(f"Schedule exported to: {args.summary_output}")
    else:
        print(content)


def show_statistics(schedule: Schedule):
    """Show schedule statistics"""
    print(f"\nSchedule Statistics:")
    print(f"  Total stages: {len(schedule.stages)}")
    total_time = schedule.total_estimated_time or 0
    print(f"  Total time: {total_time:.1f} minutes")
    if len(schedule.stages) > 0:
        print(f"  Average stage duration: {total_time/len(schedule.stages):.1f} minutes")
    
    # Count scenarios by type
    scenario_counts = {}
    total_scenarios = 0
    for stage in schedule.stages:
        for assignment in stage.assignments:
            scenario_type = assignment.scenario.scenario_type
            scenario_counts[scenario_type] = scenario_counts.get(scenario_type, 0) + 1
            total_scenarios += 1
    
    print(f"  Total scenarios: {total_scenarios}")
    for scenario_type, count in scenario_counts.items():
        print(f"    {scenario_type.name} machine scenarios: {count}")

def list_scheduled_jobs(scenarios: List[Scenario], machines: List[Machine]):
    """List all jobs that will be scheduled without running the scheduler"""
    print("=" * 60)
    print("SCHEDULED JOBS LIST")
    print("=" * 60)
    
    # Create a map of machine names to machine objects for quick lookup
    machine_map = {m.name: m for m in machines}
    
    # Expand scenarios into individual jobs
    total_jobs = 0
    jobs_by_scenario = {}
    
    for scenario in scenarios:
        jobs_by_scenario[scenario.name] = []
        for target_machine in scenario.target_machines:
            # Check if target machine exists
            if target_machine not in machine_map:
                print(f"WARNING: Target machine '{target_machine}' not found in machine list")
                continue
                
            job_name = f"{scenario.name}-{target_machine}"
            estimated_runtime_str = f"{scenario.estimated_runtime:.1f}min" if scenario.estimated_runtime else "Unknown"
            machine_types = [mt.value for mt in scenario.get_required_machine_types()]
            
            jobs_by_scenario[scenario.name].append({
                'name': job_name,
                'target_machine': target_machine,
                'scenario_type': scenario.scenario_type.value,
                'estimated_runtime': estimated_runtime_str,
                'machine_types': machine_types
            })
            total_jobs += 1
    
    # Display summary
    print(f"Total scenarios: {len(scenarios)}")
    print(f"Total jobs: {total_jobs}")
    print(f"Available machines: {len(machines)}")
    print()
    
    # Display jobs grouped by scenario
    for scenario_name, jobs in jobs_by_scenario.items():
        if not jobs:
            continue
            
        print(f"Scenario: {scenario_name}")
        print(f"  Jobs: {len(jobs)}")
        print(f"  Target machines: {', '.join([job['target_machine'] for job in jobs])}")
        print(f"  Machine types needed: {', '.join(jobs[0]['machine_types'])}")
        print(f"  Runtime: {jobs[0]['estimated_runtime']}")
        print()
        
        for job in jobs:
            print(f"    → {job['name']} ({job['scenario_type']} machines, {job['estimated_runtime']})")
        print()
    
    # Display machine summary
    print("=" * 40)
    print("MACHINE SUMMARY")
    print("=" * 40)
    
    machines_by_type = {}
    for machine in machines:
        # Get primary machine type (lowest priority capability)
        if machine.capabilities:
            primary_type = min(machine.capabilities.keys(), key=lambda mt: machine.capabilities[mt].priority)
            machine_type = primary_type.value
        else:
            continue  # Skip machines with no capabilities
        
        if machine_type not in machines_by_type:
            machines_by_type[machine_type] = []
        machines_by_type[machine_type].append(machine.name)
    
    for machine_type, machine_names in machines_by_type.items():
        print(f"{machine_type.upper()} machines ({len(machine_names)}): {', '.join(machine_names)}")
    
    print()
    print("=" * 40)
    print("JOB REQUIREMENTS ANALYSIS")
    print("=" * 40)
    
    # Analyze job requirements
    single_machine_jobs = sum(1 for scenario in scenarios for _ in scenario.target_machines if scenario.scenario_type.value == 1)
    dual_machine_jobs = sum(1 for scenario in scenarios for _ in scenario.target_machines if scenario.scenario_type.value == 2)
    triple_machine_jobs = sum(1 for scenario in scenarios for _ in scenario.target_machines if scenario.scenario_type.value == 3)
    
    print(f"Single machine jobs (SUT only): {single_machine_jobs}")
    print(f"Dual machine jobs (SUT + Load): {dual_machine_jobs}")
    print(f"Triple machine jobs (SUT + Load + DB): {triple_machine_jobs}")
    print()
    
    # Check for potential scheduling issues
    sut_machines = len([m for m in machines if MachineType.SUT in m.capabilities])
    load_machines = len([m for m in machines if MachineType.LOAD in m.capabilities])
    db_machines = len([m for m in machines if MachineType.DB in m.capabilities])
    
    max_concurrent_dual = min(sut_machines, load_machines)
    max_concurrent_triple = min(sut_machines, load_machines, db_machines)
    
    print(f"Maximum concurrent dual-machine jobs: {max_concurrent_dual}")
    print(f"Maximum concurrent triple-machine jobs: {max_concurrent_triple}")
    if dual_machine_jobs > 0 and load_machines == 0:
        print("⚠️  WARNING: Dual-machine jobs require load machines, but none are available!")
    if triple_machine_jobs > 0 and db_machines == 0:
        print("⚠️  WARNING: Triple-machine jobs require database machines, but none are available!")
    if triple_machine_jobs > 0 and load_machines == 0:
        print("⚠️  WARNING: Triple-machine jobs require load machines, but none are available!")

def list_jobs_by_machine(scenarios: List[Scenario], machines: List[Machine]):
    """List all jobs grouped by target machine"""
    print("=" * 60)
    print("JOBS BY MACHINE")
    print("=" * 60)
    
    # Create a map of machine names to machine objects for quick lookup
    machine_map = {m.name: m for m in machines}
    
    # Collect all jobs and group by machine
    jobs_by_machine = {}
    total_jobs = 0
    
    # Initialize all machines in the map
    for machine in machines:
        jobs_by_machine[machine.name] = []
    
    # Expand scenarios into individual jobs and group by machine
    for scenario in scenarios:
        for target_machine in scenario.target_machines:
            if target_machine not in machine_map:
                print(f"WARNING: Target machine '{target_machine}' not found in machine list")
                continue
                
            job_name = f"{scenario.name}-{target_machine}"
            estimated_runtime_str = f"{scenario.estimated_runtime:.1f}min" if scenario.estimated_runtime else "Unknown"

            job_info = {
                'name': job_name,
                'scenario_name': scenario.name,
                'scenario_type': scenario.scenario_type.value,
                'estimated_runtime': estimated_runtime_str,
                'machine_types': [mt.value for mt in scenario.get_required_machine_types()]
            }
            
            jobs_by_machine[target_machine].append(job_info)
            total_jobs += 1
    
    # Display summary
    machines_with_jobs = sum(1 for jobs in jobs_by_machine.values() if jobs)
    SUT_machines = len([m for m in machines if MachineType.SUT in m.capabilities])
    print(f"Total machines: {len(machines)}")
    print(f"Primary (SUT) Machines: {SUT_machines}")
    print(f"Primary Machines with Jobs: {machines_with_jobs}")
    print(f"Total jobs: {total_jobs}")
    print()
    
    # Sort machines by machine type and then by name for consistent output
    # Don't include non-SUT machines in the summary
    def get_primary_machine_type(machine):
        if machine.capabilities:
            primary_type = min(machine.capabilities.keys(), key=lambda mt: machine.capabilities[mt].priority)
            return primary_type.value
        return 'unknown'
    
    sorted_machines = sorted(machines, key=lambda m: (get_primary_machine_type(m), m.name))
    filtered_machines = [m for m in sorted_machines if MachineType.SUT in m.capabilities]
    # Display jobs for each machine
    for machine in filtered_machines:
        machine_jobs = jobs_by_machine[machine.name]
        # Extract numeric runtime from string (e.g., "5.0min" -> 5.0)
        total_runtime = 0.0
        for job in machine_jobs:
            runtime_str = job['estimated_runtime']
            if runtime_str != "Unknown":
                # Extract number from string like "5.0min"
                try:
                    total_runtime += float(runtime_str.replace('min', ''))
                except (ValueError, AttributeError):
                    pass  # Skip if we can't parse the runtime
        
        # Get primary machine type for display
        primary_type = get_primary_machine_type(machine)
        print(f"Machine: {machine.name} ({primary_type.upper()})")
        print(f"  Jobs: {len(machine_jobs)}")
        print(f"  Total runtime: {total_runtime:.1f} minutes")
        
        if machine.preferred_partners:
            partners_str = ", ".join(machine.preferred_partners)
            print(f"  Preferred partners: {partners_str}")
        
        if not machine_jobs:
            print("  → No jobs assigned")
        else:
            # Sort jobs by scenario name for consistent output
            sorted_jobs = sorted(machine_jobs, key=lambda j: j['scenario_name'])
            for job in sorted_jobs:
                scenario_types_str = ", ".join(job['machine_types'])
                print(f"    → {job['name']} ({scenario_types_str}, {job['estimated_runtime']})")
        print()
    
    # Summary statistics
    print("=" * 40)
    print("MACHINE UTILIZATION SUMMARY")
    print("=" * 40)
    
    machines_by_type = {}
    runtime_by_type = {}
    jobs_by_type = {}
    
    for machine in sorted_machines:
        machine_type = get_primary_machine_type(machine)
        machine_jobs = jobs_by_machine[machine.name]
        # Extract numeric runtime from string (e.g., "5.0min" -> 5.0)
        total_runtime = 0.0
        for job in machine_jobs:
            runtime_str = job['estimated_runtime']
            if runtime_str != "Unknown":
                # Extract number from string like "5.0min"
                try:
                    total_runtime += float(runtime_str.replace('min', ''))
                except (ValueError, AttributeError):
                    pass  # Skip if we can't parse the runtime
        
        if machine_type not in machines_by_type:
            machines_by_type[machine_type] = 0
            runtime_by_type[machine_type] = 0
            jobs_by_type[machine_type] = 0
        
        machines_by_type[machine_type] += 1
        runtime_by_type[machine_type] += total_runtime
        jobs_by_type[machine_type] += len(machine_jobs)
    
    # For now, only show statistics for SUT machines
    machine_type = 'sut'
    if machine_type in machines_by_type:
        avg_runtime = runtime_by_type[machine_type] / machines_by_type[machine_type] if machines_by_type[machine_type] > 0 else 0
        print(f"{machine_type.upper()} machines:")
        print(f"  Count: {machines_by_type[machine_type]}")
        print(f"  Total jobs: {jobs_by_type[machine_type]}")
        print(f"  Total runtime: {runtime_by_type[machine_type]:.1f} minutes")
        print(f"  Average runtime per machine: {avg_runtime:.1f} minutes")
        print()

if __name__ == "__main__":
    sys.exit(main())
