"""
Core scheduling algorithm for pod-based crank scheduling.

Uses a greedy longest-job-first approach with machine-collision avoidance.
Since pods define fixed machine groupings, the scheduler only needs to
ensure no physical machine is used twice in the same stage.
"""

from typing import List

from models import Run, Schedule, ScheduleConfig, ScenarioType, Stage


DEFAULT_RUNTIMES = {
    ScenarioType.SINGLE: 30.0,
    ScenarioType.DUAL: 45.0,
    ScenarioType.TRIPLE: 60.0,
}


def expand_runs(config: ScheduleConfig) -> List[Run]:
    """Expand scenarios × pods into individual runs."""
    runs = []
    for scenario in config.scenarios:
        for pod_name in scenario.pods:
            pod = config.pods.get(pod_name)
            if pod is None:
                print(f"  WARNING: Scenario '{scenario.name}' references "
                      f"unknown pod '{pod_name}', skipping")
                continue
            error = pod.validate(scenario.type)
            if error:
                print(f"  WARNING: {error} for scenario "
                      f"'{scenario.name}', skipping")
                continue
            runtime = scenario.estimated_runtime
            if runtime <= 0:
                runtime = DEFAULT_RUNTIMES.get(scenario.type, 45.0)
            runs.append(Run(
                scenario=scenario,
                pod=pod,
                estimated_runtime=runtime,
            ))
    return runs


def create_schedule(config: ScheduleConfig) -> Schedule:
    """
    Create an optimized schedule from the configuration.

    Algorithm:
    1. Expand all scenario × pod combinations into runs
    2. Sort by runtime descending (longest-job-first heuristic)
    3. Greedily pack runs into stages, checking machine collisions
    """
    runs = expand_runs(config)
    queue_count = len(config.queues)

    # Longest-job-first for better bin packing
    runs.sort(key=lambda r: r.estimated_runtime, reverse=True)

    schedule = Schedule()
    for run in runs:
        placed = False
        for stage in schedule.stages:
            if stage.can_add(run, queue_count):
                stage.runs.append(run)
                placed = True
                break
        if not placed:
            new_stage = Stage(runs=[run])
            schedule.stages.append(new_stage)

    return schedule


def split_schedule(schedule: Schedule, target_count: int) -> List[Schedule]:
    """
    Split a schedule into multiple sub-schedules using bin-packing.

    Assigns stages to bins (sub-schedules) to balance total runtime.
    """
    if target_count <= 1:
        return [schedule]

    # Sort stages by duration descending for better packing
    sorted_stages = sorted(
        schedule.stages, key=lambda s: s.duration, reverse=True
    )

    # Create bins, pack longest-first into lightest bin
    bins: List[Schedule] = [Schedule() for _ in range(target_count)]
    for stage in sorted_stages:
        lightest = min(bins, key=lambda b: b.total_duration)
        lightest.stages.append(stage)

    # Re-sort stages within each bin to maintain original ordering
    stage_order = {id(s): i for i, s in enumerate(schedule.stages)}
    for b in bins:
        b.stages.sort(key=lambda s: stage_order.get(id(s), 0))

    return [b for b in bins if b.stages]
