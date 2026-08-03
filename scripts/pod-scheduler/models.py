"""
Data models for pod-based crank scheduling.

A "pod" is a fixed group of machines (SUT + optional load + optional DB) that
always run together. Pods sharing physical machines cannot run simultaneously,
which the scheduler enforces automatically.
"""

import re
from dataclasses import dataclass, field
from enum import IntEnum
from typing import Dict, List, Optional, Set


# Default per-type runtime estimates (minutes) used when a scenario provides
# none of its own. Lives here so models, scheduler, and tests share one source.
DEFAULT_RUNTIMES: Dict["ScenarioType", float] = {}  # populated below

# Pipeline plumbing defaults. Override in JSON metadata.pipeline.* if needed.
DEFAULT_PIPELINE_POOL = "server"
DEFAULT_PIPELINE_CONNECTION = "ASPNET Benchmarks Service Bus"
DEFAULT_PIPELINE_NAMESPACE = "aspnetbenchmarks"

# AzDO job identifier rule. Letters/digits/underscore, must not start with a
# digit, no longer than 100 characters.
JOB_ID_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]{0,99}$")


class ScenarioType(IntEnum):
    """Number of machine roles required for a scenario."""
    SINGLE = 1   # SUT only
    DUAL = 2     # SUT + Load
    TRIPLE = 3   # SUT + Load + DB


DEFAULT_RUNTIMES.update({
    ScenarioType.SINGLE: 30.0,
    ScenarioType.DUAL: 45.0,
    ScenarioType.TRIPLE: 60.0,
})


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
    """A fixed group of machines that run scenarios together."""
    name: str
    # Physical machine names for each role
    sut: str
    load: Optional[str] = None
    db: Optional[str] = None
    # Crank profile names for each role
    sut_profile: str = ""
    load_profile: Optional[str] = None
    db_profile: Optional[str] = None

    def machines_for_type(self, scenario_type: ScenarioType) -> Set[str]:
        """Return the set of physical machines used for a given scenario type."""
        machines = {self.sut}
        if scenario_type >= ScenarioType.DUAL and self.load:
            machines.add(self.load)
        if scenario_type >= ScenarioType.TRIPLE and self.db:
            machines.add(self.db)
        return machines

    def profiles_for_type(self, scenario_type: ScenarioType) -> List[str]:
        """Return the ordered list of profiles for a given scenario type."""
        profiles = [self.sut_profile]
        if scenario_type >= ScenarioType.DUAL and self.load_profile:
            profiles.append(self.load_profile)
        if scenario_type >= ScenarioType.TRIPLE and self.db_profile:
            profiles.append(self.db_profile)
        return profiles

    def validate(self, scenario_type: ScenarioType) -> Optional[str]:
        """Check if this pod can run the given scenario type. Returns error or None."""
        if scenario_type >= ScenarioType.DUAL and not self.load:
            return f"Pod '{self.name}' has no load machine for DUAL/TRIPLE scenario"
        if scenario_type >= ScenarioType.TRIPLE and not self.db:
            return f"Pod '{self.name}' has no db machine for TRIPLE scenario"
        return None


@dataclass
class Scenario:
    """A benchmark scenario that runs on one or more pods."""
    name: str
    template: str
    type: ScenarioType
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
    """Top-level configuration loaded from JSON."""
    name: str
    schedule: str
    queues: List[str]
    target_yaml_count: int
    schedule_offset_hours: int
    pods: Dict[str, Pod]
    scenarios: List[Scenario]
    pipeline: PipelineSettings = field(default_factory=PipelineSettings)
