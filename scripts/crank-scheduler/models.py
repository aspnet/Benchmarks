from dataclasses import dataclass, field
from typing import List, Dict, Optional, Set, Literal
from enum import Enum
import re


class MachineType(Enum):
    """Types of machines available"""
    SUT = "sut"  # System Under Test
    LOAD = "load"  # Load generation machine
    DB = "db"  # Database machine


class ScenarioType(Enum):
    """Number of machines required for scenario"""
    SINGLE = 1  # Just SUT
    DUAL = 2    # SUT + Load
    TRIPLE = 3  # SUT + Load + DB


@dataclass
class MachineCapability:
    """Represents a machine's ability to fulfill a specific role"""
    machine_type: MachineType
    priority: int  # 1 = preferred, 2 = secondary, etc.
    profiles: List[str]  # All available profiles for this type
    default_profile: Optional[str] = None  # Which profile to use by default (defaults to first profile)
    
    def __post_init__(self):
        if not self.profiles:
            raise ValueError(f"At least one profile must be specified for machine type {self.machine_type}")
        
        # If no default_profile specified, use the first profile in the list
        if self.default_profile is None:
            self.default_profile = self.profiles[0]
        
        if self.default_profile not in self.profiles:
            raise ValueError(f"Default profile '{self.default_profile}' not in available profiles: {self.profiles}")


@dataclass
class Machine:
    """Represents a machine that can run scenarios with multiple capabilities"""
    name: str
    capabilities: Dict[MachineType, MachineCapability]  # Direct mapping for efficient access
    preferred_partners: List[str] = field(default_factory=list)  # Ordered list of preferred partner machines
    
    def get_capability(self, machine_type: MachineType) -> Optional[MachineCapability]:
        """Get the capability for a specific machine type"""
        return self.capabilities.get(machine_type)
    
    def can_fulfill_type(self, machine_type: MachineType) -> bool:
        """Check if machine can fulfill a specific type"""
        return machine_type in self.capabilities
    
    def get_supported_types(self) -> List[MachineType]:
        """Get all machine types this machine can support, sorted by priority"""
        return sorted(self.capabilities.keys(), key=lambda mt: self.capabilities[mt].priority)

@dataclass
class Scenario:
    """A scenario to be scheduled"""
    name: str  # Just the scenario name (e.g., "plaintext")
    scenario_type: ScenarioType
    target_machines: List[str]  # List of target machine names (e.g., ["gold-lin", "gold-win"])
    template: str # Template file for YAML generation
    estimated_runtime: Optional[float] = None  # Runtime in minutes
    description: Optional[str] = None  # Human-readable description
    profile_overrides: Optional[Dict[str, Dict[MachineType, str]]] = None  # Machine-specific profile overrides
    
    def __post_init__(self):
        # Validate that we have target machines
        if not self.target_machines:
            raise ValueError(f"Scenario {self.name} must have at least one target machine")
    
    def get_preferred_profile(self, machine_name: str, machine_type: MachineType) -> Optional[str]:
        """Get preferred profile for a specific machine and type"""
        if not self.profile_overrides:
            return None
        machine_prefs = self.profile_overrides.get(machine_name, {})
        return machine_prefs.get(machine_type)
    
    def __hash__(self):
        # Use name and target machines for hash to allow same scenario on different machines
        return hash((self.name, tuple(sorted(self.target_machines))))
    
    def __eq__(self, other):
        if isinstance(other, Scenario):
            return (self.name == other.name and 
                   set(self.target_machines) == set(other.target_machines))
        return False
    
    def get_required_machine_types(self) -> List[MachineType]:
        """Get the types of machines required for this scenario"""
        if self.scenario_type == ScenarioType.SINGLE:
            return [MachineType.SUT]
        elif self.scenario_type == ScenarioType.DUAL:
            return [MachineType.SUT, MachineType.LOAD]
        elif self.scenario_type == ScenarioType.TRIPLE:
            return [MachineType.SUT, MachineType.LOAD, MachineType.DB]
        else:
            raise ValueError(f"Unknown scenario type: {self.scenario_type}")
    
    def get_display_name(self) -> str:
        """Get a display name that includes the target machines"""
        if len(self.target_machines) == 1:
            return f"{self.name}-{self.target_machines[0]}"
        else:
            return f"{self.name}-[{','.join(self.target_machines)}]"


@dataclass
class MachineAssignment:
    """Assignment of machines to a scenario"""
    scenario: Scenario
    machines: Dict[MachineType, Machine]  # Map of machine type to assigned machine
    profiles: Dict[MachineType, str]  # Map of machine type to selected profile
    queue_id: int
    target_machine: str  # The specific target machine this assignment is for
    
    def get_total_machines(self) -> int:
        return len(self.machines)
    
    def get_machine_names(self) -> List[str]:
        return [machine.name for machine in self.machines.values()]
    
    def get_display_name(self) -> str:
        """Get a display name for this assignment"""
        return f"{self.scenario.name}-{self.target_machine}"
    
    def get_profile_for_machine_type(self, machine_type: MachineType) -> Optional[str]:
        """Get the profile being used for a specific machine type"""
        return self.profiles.get(machine_type)


@dataclass
class Stage:
    """A stage in the schedule where scenarios run in parallel"""
    stage_id: int
    assignments: List[MachineAssignment]
    estimated_duration: Optional[float] = None  # Duration in minutes
    
    def get_used_machines(self) -> Set[str]:
        """Get all machine names used in this stage"""
        used = set()
        for assignment in self.assignments:
            used.update(assignment.get_machine_names())
        return used
    
    def calculate_duration(self) -> float:
        """Calculate the estimated duration of this stage"""
        if not self.assignments:
            return 0.0
        
        # Stage duration is the maximum runtime of any scenario in the stage
        max_runtime = 0.0
        for assignment in self.assignments:
            if assignment.scenario.estimated_runtime:
                max_runtime = max(max_runtime, assignment.scenario.estimated_runtime)
        
        self.estimated_duration = max_runtime
        return max_runtime


@dataclass
class Schedule:
    """Complete schedule with all stages"""
    stages: List[Stage]
    total_estimated_time: Optional[float] = None
    
    def calculate_total_time(self) -> float:
        """Calculate total estimated time for the schedule"""
        total = sum(stage.calculate_duration() for stage in self.stages)
        self.total_estimated_time = total
        return total
    
    def get_machine_utilization(self) -> Dict[str, float]:
        """Calculate utilization percentage for each machine"""
        if not self.stages or self.total_estimated_time == 0:
            return {}
        
        machine_usage = {}
        
        for stage in self.stages:
            stage_duration = stage.calculate_duration()
            for assignment in stage.assignments:
                for machine in assignment.machines.values():
                    if machine.name not in machine_usage:
                        machine_usage[machine.name] = 0.0
                    machine_usage[machine.name] += stage_duration
        
        # Convert to percentages
        utilization = {}
        for machine_name, usage_time in machine_usage.items():
            utilization[machine_name] = (usage_time / self.total_estimated_time) * 100
        
        return utilization


@dataclass
class YamlGenerationConfig:
    """Configuration for multi-YAML generation"""
    target_yaml_count: int = 2
    schedule_offset_hours: int = 6

@dataclass
class ConfigurationMetadata:
    """Metadata for a combined configuration file"""
    name: str
    description: str
    version: str
    schedule: str
    queues: List[str]
    yaml_generation: Optional[YamlGenerationConfig] = None

@dataclass 
class CombinedConfiguration:
    """Combined machines and scenarios configuration"""
    metadata: ConfigurationMetadata
    machines: List[Machine]
    scenarios: List[Scenario]

@dataclass
class PartialSchedule:
    """A partial schedule containing a subset of stages"""
    name: str
    stages: List[Stage]
    total_estimated_time: Optional[float] = None
    
    def __post_init__(self):
        if self.total_estimated_time is None:
            self.total_estimated_time = sum(stage.estimated_duration or 0 for stage in self.stages)
    
    def add_stage(self, stage: Stage):
        """Add a stage to this partial schedule"""
        self.stages.append(stage)
        self.total_estimated_time = (self.total_estimated_time or 0) + (stage.estimated_duration or 0)
