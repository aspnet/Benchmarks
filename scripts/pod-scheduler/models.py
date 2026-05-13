"""
Data models for pod-based crank scheduling.

A "pod" is a fixed group of machines that always run together. Pods sharing
physical machines cannot run simultaneously, which the scheduler enforces
automatically. Each pod has parallel ``machines`` and ``profiles`` lists:
``machines[i]`` runs ``profiles[i]``, and the position is the role -- slot 0
is the SUT, slot 1 is the load generator, slot 2 is the database.
"""

import re
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Set


# Scenario type is just the count of machine roles required (1 = SUT only,
# 2 = SUT + load, 3 = SUT + load + db). Stored as a plain int so that adding
# a fourth role later is a one-line change.
SCENARIO_TYPE_SINGLE = 1
SCENARIO_TYPE_DUAL = 2
SCENARIO_TYPE_TRIPLE = 3

# Default per-type runtime estimates (minutes) used when a scenario provides
# none of its own. Keyed by the integer role count.
DEFAULT_RUNTIMES: Dict[int, float] = {
    SCENARIO_TYPE_SINGLE: 30.0,
    SCENARIO_TYPE_DUAL:   45.0,
    SCENARIO_TYPE_TRIPLE: 60.0,
}

# Canonical role label per slot index. Used only for the human-readable
# ``--show-conflicts`` diagnostic output. The scheduler itself doesn't care
# about the names.
ROLE_NAMES: List[str] = ["sut", "load", "db"]

# Pipeline plumbing defaults. Override in metadata.pipeline.* in the config.
DEFAULT_PIPELINE_POOL = "server"
DEFAULT_PIPELINE_CONNECTION = "ASPNET Benchmarks Service Bus"
DEFAULT_PIPELINE_NAMESPACE = "aspnetbenchmarks"

# AzDO job identifier rule. Letters/digits/underscore, must not start with a
# digit, no longer than 100 characters.
JOB_ID_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]{0,99}$")


def sanitize_job_id(raw: str) -> str:
    """Sanitize an arbitrary string into a valid AzDO job identifier.

    Replaces any character that isn't [A-Za-z0-9_] with '_', collapses runs of
    underscores, prefixes a leading digit with '_', and truncates at 100
    characters. The result is guaranteed to match ``JOB_ID_RE``.
    """
    cleaned = re.sub(r"[^A-Za-z0-9_]", "_", raw)
    cleaned = re.sub(r"_+", "_", cleaned).strip("_") or "_"
    if cleaned[0].isdigit():
        cleaned = "_" + cleaned
    cleaned = cleaned[:100]
    if not JOB_ID_RE.match(cleaned):
        # Should be unreachable, but assert keeps callers honest.
        raise ValueError(f"Could not sanitize {raw!r} into a valid job id")
    return cleaned


@dataclass
class Pod:
    """A fixed group of machines that run scenarios together.

    ``machines`` and ``profiles`` are parallel arrays: ``machines[i]`` runs
    ``profiles[i]``. The role of slot ``i`` is the i-th of ``ROLE_NAMES``;
    a pod with two entries can run single and dual scenarios, three entries
    adds triple.
    """
    name: str
    machines: List[str]
    profiles: List[str]

    def __post_init__(self) -> None:
        if not self.machines:
            raise ValueError(f"Pod '{self.name}' has no machines")
        if len(self.machines) != len(self.profiles):
            raise ValueError(
                f"Pod '{self.name}': machines has {len(self.machines)} "
                f"entries but profiles has {len(self.profiles)}"
            )

    def machines_for_type(self, scenario_type: int) -> Set[str]:
        """Return the set of physical machines used for a given scenario type."""
        return set(self.machines[:scenario_type])

    def profiles_for_type(self, scenario_type: int) -> List[str]:
        """Return the ordered list of profiles for a given scenario type."""
        return list(self.profiles[:scenario_type])

    def validate(self, scenario_type: int) -> Optional[str]:
        """Check if this pod can run the given scenario type. Returns error or None."""
        if scenario_type > len(self.machines):
            return (
                f"Pod '{self.name}' has only {len(self.machines)} machine(s) "
                f"but scenario needs {scenario_type}"
            )
        return None


@dataclass
class Scenario:
    """A benchmark scenario that runs on one or more pods."""
    name: str
    template: str
    # Number of machine roles required. 1 = single (SUT only), 2 = dual
    # (SUT + load), 3 = triple (SUT + load + db). Stored as plain int so the
    # set can grow later without touching this type.
    type: int
    pods: List[str]
    estimated_runtime: float = 45.0
    # Optional explicit timeout (minutes) for the generated AzDO job. When
    # None, the generator derives one from estimated_runtime.
    timeout: Optional[int] = None


@dataclass
class Run:
    """A single execution: one scenario on one pod."""
    scenario: Scenario
    pod: Pod
    estimated_runtime: float

    @property
    def name(self) -> str:
        return f"{self.scenario.name} {self.pod.name}"

    @property
    def job_name(self) -> str:
        """Sanitized identifier suitable for AzDO ``- job:`` use."""
        return sanitize_job_id(self.name)

    @property
    def machines_used(self) -> Set[str]:
        return self.pod.machines_for_type(self.scenario.type)

    @property
    def profiles(self) -> List[str]:
        return self.pod.profiles_for_type(self.scenario.type)


@dataclass
class Stage:
    """A group of runs that execute in parallel (no machine conflicts)."""
    runs: List[Run] = field(default_factory=list)

    @property
    def machines_in_use(self) -> Set[str]:
        result: Set[str] = set()
        for run in self.runs:
            result |= run.machines_used
        return result

    @property
    def duration(self) -> float:
        return max((r.estimated_runtime for r in self.runs), default=0)

    def can_add(self, run: Run, queue_count: int) -> bool:
        """True if the run fits without machine conflicts or queue overflow."""
        if len(self.runs) >= queue_count:
            return False
        return run.machines_used.isdisjoint(self.machines_in_use)


@dataclass
class Schedule:
    """Complete schedule: ordered list of stages."""
    stages: List[Stage] = field(default_factory=list)

    @property
    def total_duration(self) -> float:
        return sum(s.duration for s in self.stages)

    @property
    def total_runs(self) -> int:
        return sum(len(s.runs) for s in self.stages)


@dataclass
class PipelineSettings:
    """Pipeline-level plumbing values rendered into the YAML."""
    pool: str = DEFAULT_PIPELINE_POOL
    service_bus_connection: str = DEFAULT_PIPELINE_CONNECTION
    service_bus_namespace: str = DEFAULT_PIPELINE_NAMESPACE


@dataclass
class ScheduleConfig:
    """Top-level configuration loaded from a YAML or JSON file."""
    name: str
    schedule: str
    queues: List[str]
    target_yaml_count: int
    schedule_offset_hours: int
    pods: Dict[str, Pod]
    scenarios: List[Scenario]
    pipeline: PipelineSettings = field(default_factory=PipelineSettings)
