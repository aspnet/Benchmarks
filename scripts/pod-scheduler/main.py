#!/usr/bin/env python3
"""
Pod-based crank scheduler.

Assigns benchmark scenarios to machine pods and generates Azure DevOps
pipeline YAML files. Pods define fixed machine groupings (SUT + load + DB),
and the scheduler ensures no physical machine is double-booked per stage.

Usage:
    python main.py --config ./build/benchmarks_ci_pods.json
    python main.py --config ./build/benchmarks_ci_pods.json \\
        --yaml-output ./build
"""

import argparse
import json
import os
import sys
from typing import List

from config_loader import ConfigError, load_config
from generator import GeneratorError, generate_yamls, schedule_to_template_data
from models import Schedule, ScheduleConfig
from scheduler import (
    SchedulerError,
    create_schedule,
    expand_runs,
    split_schedule,
)


def print_summary(config: ScheduleConfig, schedule: Schedule) -> None:
    """Print a human-readable schedule summary."""
    print(f"\n{'=' * 70}")
    print(f"SCHEDULE SUMMARY: {config.name}")
    print(f"{'=' * 70}")
    print(f"  Pods: {len(config.pods)}")
    print(f"  Scenarios: {len(config.scenarios)}")
    print(f"  Total runs: {schedule.total_runs}")
    print(f"  Stages: {len(schedule.stages)}")
    print(f"  Queues: {len(config.queues)} ({', '.join(config.queues)})")
    print(f"  Est. total time: {schedule.total_duration:.0f} min "
          f"({schedule.total_duration / 60:.1f} hrs)")
    print()

    machine_time = {}
    machine_total = {}
    for stage in schedule.stages:
        stage_machines = set()
        for run in stage.runs:
            for m in run.machines_used:
                stage_machines.add(m)
                machine_time.setdefault(m, 0)
                machine_time[m] += run.estimated_runtime
        for m in stage_machines:
            machine_total.setdefault(m, 0)
            machine_total[m] += stage.duration

    if machine_total:
        print("MACHINE UTILIZATION:")
        for m in sorted(machine_total.keys()):
            busy = machine_time.get(m, 0)
            total = machine_total.get(m, 1)
            pct = (busy / total * 100) if total > 0 else 0
            filled = int(pct / 5)
            bar = "#" * filled + "." * (20 - filled)
            print(f"  {m:<25} {bar} {pct:5.1f}%  "
                  f"({busy:.0f}/{total:.0f} min)")
        print()

    print("STAGE BREAKDOWN:")
    for i, stage in enumerate(schedule.stages):
        print(f"\n  Stage {i} (Duration: {stage.duration:.0f} min)")
        print(f"  {'Queue':<8} {'Scenario':<40} {'Runtime':>8}  "
              f"Machines")
        print(f"  {'-' * 80}")
        for j, run in enumerate(stage.runs):
            queue = config.queues[j % len(config.queues)]
            machines = ", ".join(sorted(run.machines_used))
            print(f"  {queue:<8} {run.name:<40} {run.estimated_runtime:>6.0f}m  "
                  f"{machines}")
    print()


def print_split_summary(schedules: List[Schedule], config: ScheduleConfig) -> None:
    """Print summary of multi-YAML split."""
    if len(schedules) <= 1:
        return
    print(f"YAML SPLIT ({len(schedules)} files):")
    for i, sched in enumerate(schedules):
        print(f"  YAML {i + 1}: {len(sched.stages)} stages, "
              f"{sched.total_runs} runs, "
              f"{sched.total_duration:.0f} min")
    print()


def print_pod_conflicts(config: ScheduleConfig) -> None:
    """Show which pods share physical machines (potential conflicts)."""
    machine_pods = {}
    for pod in config.pods.values():
        for role in ["sut", "load", "db"]:
            machine = getattr(pod, role)
            if machine:
                machine_pods.setdefault(machine, []).append(
                    (pod.name, role)
                )

    shared = {m: pods for m, pods in machine_pods.items() if len(pods) > 1}
    if shared:
        print("SHARED MACHINES (pods that cannot run simultaneously):")
        for machine, pods in sorted(shared.items()):
            pod_strs = [f"{name}({role})" for name, role in pods]
            print(f"  {machine:<20} used by: {', '.join(pod_strs)}")
        print()


def _format_source_path(path: str) -> str:
    """Render a config path for embedding in generated YAML headers.

    Tries to express the path relative to the repo root using forward slashes
    so the regen command is identical on Windows and POSIX shells. Falls back
    to ``./<basename>`` if the path is outside the repo.
    """
    repo_root = os.path.abspath(
        os.path.join(os.path.dirname(__file__), "..", "..")
    )
    abs_path = os.path.abspath(path)
    try:
        rel = os.path.relpath(abs_path, repo_root)
    except ValueError:
        rel = os.path.basename(abs_path)
    rel = rel.replace(os.sep, "/")
    if rel.startswith("../"):
        rel = os.path.basename(abs_path)
    return f"./{rel}"


def _build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Pod-based crank scheduler"
    )
    parser.add_argument(
        "--config", required=True,
        help="Path to JSON configuration file"
    )
    parser.add_argument(
        "--yaml-output",
        help="Directory to write generated YAML files"
    )
    parser.add_argument(
        "--base-name", default="benchmarks-ci",
        help="Base filename for generated YAMLs (default: benchmarks-ci)"
    )
    parser.add_argument(
        "--target-yamls", type=int,
        help="Override number of YAML files to generate"
    )
    parser.add_argument(
        "--template-data", action="store_true",
        help="Print template data as JSON (for debugging)"
    )
    parser.add_argument(
        "--list-runs", action="store_true",
        help="List all expanded runs without scheduling"
    )
    parser.add_argument(
        "--show-conflicts", action="store_true",
        help="Show pods that share physical machines"
    )
    parser.add_argument(
        "--lenient", action="store_true",
        help="Warn instead of fail on unknown or invalid pod references. "
             "Off by default so config typos do not silently drop scenarios."
    )
    return parser


def main(argv: List[str] = None) -> int:
    parser = _build_arg_parser()
    args = parser.parse_args(argv)

    try:
        print(f"Loading config: {args.config}")
        config = load_config(args.config)
        print(f"  Loaded {len(config.pods)} pods, "
              f"{len(config.scenarios)} scenarios")

        strict = not args.lenient

        if args.show_conflicts:
            print_pod_conflicts(config)

        if args.list_runs:
            runs = expand_runs(config, strict=strict)
            print(f"\nAll runs ({len(runs)} total):")
            for r in runs:
                machines = ", ".join(sorted(r.machines_used))
                print(f"  {r.name:<45} type={r.scenario.type.value}  "
                      f"runtime={r.estimated_runtime:.0f}m  "
                      f"machines=[{machines}]")
            return 0

        schedule = create_schedule(config, strict=strict)
        print_summary(config, schedule)
        print_pod_conflicts(config)

        yaml_count = args.target_yamls or config.target_yaml_count
        schedules = split_schedule(schedule, yaml_count)
        print_split_summary(schedules, config)

        if args.template_data:
            for i, sched in enumerate(schedules):
                data = schedule_to_template_data(sched, config)
                print(f"\n--- Template data for YAML {i + 1} ---")
                print(json.dumps(data, indent=2))

        if args.yaml_output:
            print(f"Generating {len(schedules)} YAML file(s)...")
            generate_yamls(
                schedules, config, args.yaml_output,
                base_name=args.base_name,
                source_config=_format_source_path(args.config),
            )
            print("Done!")
        return 0
    except (ConfigError, SchedulerError, GeneratorError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
