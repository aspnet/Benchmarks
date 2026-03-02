"""
Data models for pod-based crank scheduling.

A "pod" is a fixed group of machines (SUT + optional load + optional DB) that
always run together. Pods sharing physical machines cannot run simultaneously,
which the scheduler enforces automatically.
"""

from dataclasses import dataclass, field
from enum import IntEnum
from typing import Dict, List, Optional, Set


class ScenarioType(IntEnum):
    """Number of machine roles required for a scenario."""
    SINGLE = 1   # SUT only
    DUAL = 2     # SUT + Load
    TRIPLE = 3   # SUT + Load + DB


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
    pods: List[str]  # Pod names this scenario targets
    estimated_runtime: float = 45.0  # Minutes


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
        """Sanitized name suitable for YAML job identifiers."""
        return self.name.replace(" ", "_").replace("-", "_")

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
        """Check if a run can be added without machine conflicts or queue overflow."""
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
class ScheduleConfig:
    """Top-level configuration loaded from JSON."""
    name: str
    schedule: str
    queues: List[str]
    target_yaml_count: int
    schedule_offset_hours: int
    pods: Dict[str, Pod]
    scenarios: List[Scenario]
