"""
Core scheduling algorithm for pod-based crank scheduling.

Uses a greedy longest-job-first approach with machine-collision avoidance.
Since pods define fixed machine groupings, the scheduler only needs to
ensure no physical machine is used twice in the same stage.

Output is deterministic: identical input JSON always produces identical
schedules, so generated YAML files diff cleanly across regenerations.
"""

from typing import List, Tuple

from models import (
    DEFAULT_RUNTIMES,
    Run,
    Schedule,
    ScheduleConfig,
    Stage,
)


class SchedulerError(ValueError):
    """Raised when the scheduler refuses to build a schedule."""


def expand_runs(config: ScheduleConfig, strict: bool = True) -> List[Run]:
    """Expand scenarios x pods into individual runs.

    With ``strict=True`` (the default) an unknown pod or a pod that cannot
    satisfy a scenario's type raises :class:`SchedulerError`. With
    ``strict=False`` the offending entry is skipped and a warning is printed.
    """
    runs: List[Run] = []
    for scenario in config.scenarios:
        for pod_name in scenario.pods:
            pod = config.pods.get(pod_name)
            if pod is None:
                msg = (
                    f"Scenario '{scenario.name}' references unknown pod "
                    f"'{pod_name}'"
                )
                if strict:
                    raise SchedulerError(msg)
                print(f"  WARNING: {msg}, skipping")
                continue
            error = pod.validate(scenario.type)
            if error:
                msg = f"{error} for scenario '{scenario.name}'"
                if strict:
                    raise SchedulerError(msg)
                print(f"  WARNING: {msg}, skipping")
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


def create_schedule(config: ScheduleConfig, strict: bool = True) -> Schedule:
    """Create a schedule by greedy longest-job-first packing.

    1. Expand all scenario x pod combinations into runs.
    2. Sort by runtime descending (longest-job-first heuristic).
    3. Greedily pack runs into stages, checking machine collisions.

    Sort key includes the run name as a tie-breaker so the result is stable.
    """
    runs = expand_runs(config, strict=strict)
    queue_count = len(config.queues)
    if queue_count == 0:
        raise SchedulerError(
            "Cannot schedule with zero queues. Configure metadata.queues."
        )

    runs.sort(key=lambda r: (-r.estimated_runtime, r.name))

    schedule = Schedule()
    for run in runs:
        for stage in schedule.stages:
            if stage.can_add(run, queue_count):
                stage.runs.append(run)
                break
        else:
            schedule.stages.append(Stage(runs=[run]))

    return schedule


def split_schedule(schedule: Schedule, target_count: int) -> List[Schedule]:
    """Split a schedule into multiple sub-schedules using bin-packing.

    Stages are packed longest-first into the lightest bin to balance total
    runtime, then each bin's stages are restored to their original ordering
    so the generated YAML files preserve scenario sequence.
    """
    if target_count <= 1:
        return [schedule]

    indexed: List[Tuple[int, Stage]] = list(enumerate(schedule.stages))
    indexed.sort(key=lambda pair: -pair[1].duration)

    bins: List[List[Tuple[int, Stage]]] = [[] for _ in range(target_count)]
    bin_durations = [0.0] * target_count
    for original_index, stage in indexed:
        target = min(range(target_count), key=lambda i: bin_durations[i])
        bins[target].append((original_index, stage))
        bin_durations[target] += stage.duration

    result: List[Schedule] = []
    for entries in bins:
        if not entries:
            continue
        entries.sort(key=lambda pair: pair[0])
        result.append(Schedule(stages=[stage for _, stage in entries]))
    return result
