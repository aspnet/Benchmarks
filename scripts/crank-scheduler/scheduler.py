from typing import Dict, List, Optional, Set, Tuple

from models import (CombinedConfiguration, Machine, MachineAssignment,
                    MachineCapability, MachineType, Scenario, ScenarioType,
                    Schedule, Stage)


class MachineAllocator:
    """Handles machine allocation logic with multi-type capabilities"""

    def __init__(self, machines: List[Machine], enforce_machine_groups: bool = True):
        self.machines = {m.name: m for m in machines}
        self.enforce_machine_groups = enforce_machine_groups

    def find_machine_assignment(self, scenario: Scenario, target_machine: str, used_machines: Set[str]) -> Optional[
            Tuple[Dict[MachineType, Machine], Dict[MachineType, str]]]:
        """Find the best machine assignment for a scenario on a specific target machine"""
        required_types = scenario.get_required_machine_types()
        assignment = {}
        profiles = {}
        used_machines_temp = used_machines.copy()
        sut_machine = None

        # Process each required machine type
        for machine_type in required_types:
            if machine_type == MachineType.SUT:
                # For SUT, REQUIRE the exact target machine - no substitution allowed
                machine_profile = self._select_exact_sut_machine(
                    target_machine, used_machines_temp, scenario)
                if not machine_profile:
                    return None

                machine, profile = machine_profile
                assignment[machine_type] = machine
                profiles[machine_type] = profile
                used_machines_temp.add(machine.name)
                sut_machine = machine  # Store SUT machine for preferred partner selection

            else:
                # For LOAD and DB, use SUT machine's preferred partners
                preferred_partners = sut_machine.preferred_partners if sut_machine else []
                machine_profile = self._select_best_machine_for_type(
                    machine_type, used_machines_temp, None, scenario, preferred_partners, sut_machine
                )
                if not machine_profile:
                    return None

                machine, profile = machine_profile
                assignment[machine_type] = machine
                profiles[machine_type] = profile
                used_machines_temp.add(machine.name)

        # Return combined assignment information
        return (assignment, profiles)

    def _select_best_machine_for_type(self, machine_type: MachineType,
                                      used_machines: Set[str],
                                      preferred_machine: Optional[str] = None,
                                      scenario: Optional[Scenario] = None,
                                      preferred_partners: Optional[List[str]] = None,
                                      sut_machine: Optional[Machine] = None) -> Optional[Tuple[Machine, str]]:
        """Select the best machine for a specific type with priority-based selection"""
        candidates = []

        for machine in self.machines.values():
            if machine.name in used_machines:
                continue

            capability = machine.get_capability(machine_type)
            if not capability:
                continue

            # Apply machine group filtering if enabled
            if self.enforce_machine_groups and sut_machine:
                if not self._machines_in_same_group(sut_machine, machine):
                    continue

            # Calculate effective priority
            priority = capability.priority

            # Highest priority for preferred machine (SUT case)
            if preferred_machine and machine.name == preferred_machine:
                priority = 0
            # High priority for preferred partners (LOAD/DB case)
            elif preferred_partners and machine.name in preferred_partners:
                # Give priority based on position in preferred_partners list
                try:
                    partner_index = preferred_partners.index(machine.name)
                    # 0.1, 0.2, 0.3, etc.
                    priority = 0.1 + (partner_index * 0.1)
                except ValueError:
                    pass  # Not in preferred partners, keep original priority

            candidates.append((priority, machine, capability))

        if not candidates:
            return None

        # Sort by priority (lower = better) and return best match
        candidates.sort(key=lambda x: x[0])
        _, best_machine, best_capability = candidates[0]

        # Select profile based on scenario preferences or default
        profile = self._select_profile_for_capability(
            best_capability, best_machine, machine_type, scenario)
        return (best_machine, profile)

    def _machines_in_same_group(self, machine1: Machine, machine2: Machine) -> bool:
        """Check if two machines are in the same group or if either has no group (compatible)"""
        # If either machine has no group, they are compatible with any machine
        if machine1.machine_group is None or machine2.machine_group is None:
            return True

        # Both machines have groups - they must match
        return machine1.machine_group == machine2.machine_group

    def _select_profile_for_capability(self, capability: MachineCapability, machine: Machine,
                                       machine_type: MachineType, scenario: Optional[Scenario] = None) -> str:
        """Select the best profile for a capability, considering scenario preferences"""

        # Check if scenario has a specific profile preference for this machine/type
        if scenario:
            preferred_profile = scenario.get_preferred_profile(
                machine.name, machine_type)
            if preferred_profile and preferred_profile in capability.profiles:
                return preferred_profile

        # Use default profile (guaranteed to be set in MachineCapability.__post_init__)
        return capability.default_profile or capability.profiles[0]

    def _select_exact_sut_machine(self, target_machine: str, used_machines: Set[str],
                                  scenario: Optional[Scenario] = None) -> Optional[Tuple[Machine, str]]:
        """Select the exact target machine for SUT role - no substitution allowed"""

        # Check if target machine is available
        if target_machine in used_machines:
            return None

        # Check if target machine exists and has SUT capability
        if target_machine not in self.machines:
            return None

        machine = self.machines[target_machine]
        capability = machine.get_capability(MachineType.SUT)
        if not capability:
            return None

        # Select profile based on scenario preferences or default
        profile = self._select_profile_for_capability(
            capability, machine, MachineType.SUT, scenario)
        return (machine, profile)


class CrankScheduler:
    """Main scheduler class that creates optimized schedules"""

    def __init__(self, machines: List[Machine], scenarios: List[Scenario], config: CombinedConfiguration):
        self.machines = machines
        self.scenarios = scenarios
        self.max_queues = len(config.metadata.queues)
        self.allocator = MachineAllocator(
            machines, config.metadata.enforce_machine_groups)

        # Estimate runtimes for all scenarios
        for scenario in self.scenarios:
            if scenario.estimated_runtime is None:
                scenario.estimated_runtime = estimate_runtime(scenario)

    def create_schedule(self) -> Schedule:
        """Create an optimized schedule"""
        # Expand scenarios into individual runs for each target machine
        scenario_runs = []
        for scenario in self.scenarios:
            for target_machine in scenario.target_machines:
                scenario_runs.append((scenario, target_machine))

        # Sort by estimated runtime (descending) to minimize idle time
        sorted_runs = sorted(scenario_runs,
                             key=lambda run: run[0].estimated_runtime or 0,
                             reverse=True)

        stages = []
        remaining_runs = sorted_runs.copy()
        stage_id = 0

        while remaining_runs:
            stage = self._create_stage(stage_id, remaining_runs)
            if not stage.assignments:
                # If we can't create any assignments, we might have a problem
                print(
                    f"Warning: Could not schedule {len(remaining_runs)} scenario runs")
                break

            stages.append(stage)

            # Remove scheduled runs
            scheduled_runs = set()
            for assignment in stage.assignments:
                scheduled_runs.add(
                    (assignment.scenario, assignment.target_machine))
            remaining_runs = [
                run for run in remaining_runs if run not in scheduled_runs]
            stage_id += 1

        schedule = Schedule(stages=stages)
        schedule.calculate_total_time()
        return schedule

    def _create_stage(self, stage_id: int, available_runs: List[Tuple]) -> Stage:
        """Create a single stage by packing scenario runs optimally"""
        assignments = []
        used_machines = set()
        # Track assignments per queue
        queue_assignments = [[] for _ in range(self.max_queues)]

        # Try to assign scenario runs to this stage
        for scenario, target_machine in available_runs.copy():
            assignment_result = self.allocator.find_machine_assignment(
                scenario, target_machine, used_machines)

            if assignment_result:
                machine_assignment, profiles = assignment_result

                # Find the queue with the fewest assignments to balance load
                queue_id = min(range(self.max_queues),
                               key=lambda q: len(queue_assignments[q]))

                assignment = MachineAssignment(
                    scenario=scenario,
                    machines=machine_assignment,
                    profiles=profiles,
                    queue_id=queue_id,
                    target_machine=target_machine
                )
                assignments.append(assignment)
                queue_assignments[queue_id].append(assignment)
                used_machines.update(assignment.get_machine_names())

                # Stop if we've reached the maximum number of assignments for this stage
                # (one per queue at most)
                if len(assignments) >= self.max_queues:
                    break

        stage = Stage(stage_id=stage_id, assignments=assignments)
        stage.calculate_duration()
        return stage


def estimate_runtime(scenario: Scenario) -> float:
    """Estimate runtime for a scenario"""
    if scenario.estimated_runtime is not None:
        return scenario.estimated_runtime        # Try to find similar scenarios by name

    # Fallback to a default estimate based on scenario complexity
    if scenario.scenario_type == ScenarioType.SINGLE:
        return 30.0  # 30 minutes default
    elif scenario.scenario_type == ScenarioType.DUAL:
        return 45.0  # 45 minutes default
    else:  # TRIPLE
        return 60.0  # 60 minutes default
