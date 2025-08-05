import json
import yaml
import csv
from typing import List
from models import Machine, Scenario, Schedule, MachineType, ScenarioType, ConfigurationMetadata, CombinedConfiguration, YamlGenerationConfig, MachineCapability


class DataLoader:
    """Load machine and scenario data from various formats"""

    @staticmethod
    def load_combined_configuration(file_path: str) -> CombinedConfiguration:
        """Load combined machines and scenarios configuration from JSON file"""
        with open(file_path, 'r') as f:
            # if the file is empty or not valid JSON, raise an error
            try:
                data = json.load(f)
            except (json.JSONDecodeError, ValueError):
                raise ValueError("Invalid JSON file")

        # Load metadata
        metadata_data = data.get('metadata', {})
        
        # Load YAML generation config if present
        yaml_gen_data = metadata_data.get('yaml_generation')
        yaml_generation = None
        if yaml_gen_data:
            yaml_generation = YamlGenerationConfig(
                target_yaml_count=yaml_gen_data.get('target_yaml_count', 2),
                schedule_offset_hours=yaml_gen_data.get('schedule_offset_hours', 6),
            )
        
        metadata = ConfigurationMetadata(
            name=metadata_data.get('name', 'Configuration'),
            description=metadata_data.get('description', ''),
            version=metadata_data.get('version', '1.0'),
            schedule=metadata_data.get('schedule'),
            queues=metadata_data.get('queues', []),
            yaml_generation=yaml_generation,
            enforce_machine_groups=metadata_data.get('enforce_machine_groups', True)
        )
        
        # Load machines
        machines = []
        for machine_data in data.get('machines', []):
            # Parse capabilities
            capabilities = {}
            for machine_type_str, cap_data in machine_data.get('capabilities', {}).items():
                machine_type = MachineType(machine_type_str)
                
                # Get profiles list - required
                profiles = cap_data.get('profiles', [])
                if not profiles:
                    # Fallback: create a default profile name if none specified
                    profiles = [f"{machine_data['name']}-{machine_type_str}"]
                
                # Get default_profile - optional, will default to first profile if not specified
                default_profile = cap_data.get('default_profile')
                
                capability = MachineCapability(
                    machine_type=machine_type,
                    priority=cap_data.get('priority', 1),
                    profiles=profiles,
                    default_profile=default_profile  # Can be None, will be set to first profile in __post_init__
                )
                capabilities[machine_type] = capability
            
            machines.append(Machine(
                name=machine_data['name'],
                capabilities=capabilities,
                preferred_partners=machine_data.get('preferred_partners', []),
                machine_group=machine_data.get('machine_group')
            ))
        
        # Load scenarios
        scenarios = []
        for scenario_data in data.get('scenarios', []):
            scenario_type = ScenarioType(scenario_data.get('type'))
            
            # Parse profile overrides if present
            profile_overrides = None
            if 'profile_overrides' in scenario_data:
                profile_overrides = {}
                for machine_name, prefs in scenario_data['profile_overrides'].items():
                    machine_prefs = {}
                    for machine_type_str, profile_name in prefs.items():
                        machine_type = MachineType(machine_type_str)
                        machine_prefs[machine_type] = profile_name
                    profile_overrides[machine_name] = machine_prefs
            
            scenarios.append(Scenario(
                name=scenario_data['name'],
                scenario_type=scenario_type,
                target_machines=scenario_data['target_machines'],
                estimated_runtime=scenario_data.get('estimated_runtime'),
                template=scenario_data.get('template'),
                description=scenario_data.get('description'),
                profile_overrides=profile_overrides
            ))
        
        return CombinedConfiguration(
            metadata=metadata,
            machines=machines,
            scenarios=scenarios
        )
    
class ScheduleExporter:
    """Export schedules to various formats"""
    
    @staticmethod
    def to_json(schedule: Schedule) -> str:
        """Export schedule to JSON format"""
        data = {
            'total_estimated_time': schedule.total_estimated_time,
            'stages': []
        }
        
        for stage in schedule.stages:
            stage_data = {
                'stage_id': stage.stage_id,
                'estimated_duration': stage.estimated_duration,
                'assignments': []
            }
            
            for assignment in stage.assignments:
                assignment_data = {
                    'scenario': assignment.scenario.name,
                    'queue_id': assignment.queue_id,
                    'machines': {mt.value: machine.name for mt, machine in assignment.machines.items()},
                    'estimated_runtime': assignment.scenario.estimated_runtime
                }
                stage_data['assignments'].append(assignment_data)
            
            data['stages'].append(stage_data)
        
        return json.dumps(data, indent=2)
    
    @staticmethod
    def to_yaml(schedule: Schedule) -> str:
        """Export schedule to YAML format"""
        data = json.loads(ScheduleExporter.to_json(schedule))
        return yaml.dump(data, default_flow_style=False, sort_keys=False)
    
    @staticmethod
    def to_csv(schedule: Schedule) -> str:
        """Export schedule to CSV format"""
        import io
        output = io.StringIO()
        
        fieldnames = ['stage_id', 'queue_id', 'scenario', 'sut_machine', 'load_machine', 
                     'db_machine', 'estimated_runtime', 'stage_duration']
        writer = csv.DictWriter(output, fieldnames=fieldnames)
        writer.writeheader()
        
        for stage in schedule.stages:
            for assignment in stage.assignments:
                row = {
                    'stage_id': stage.stage_id,
                    'queue_id': assignment.queue_id,
                    'scenario': assignment.scenario.name,
                    'estimated_runtime': assignment.scenario.estimated_runtime,
                    'stage_duration': stage.estimated_duration
                }
                
                # Add machine assignments
                for machine_type, machine in assignment.machines.items():
                    row[f"{machine_type.value}_machine"] = machine.name
                
                writer.writerow(row)
        
        return output.getvalue()
    
    @staticmethod
    def to_summary_table(schedule: Schedule) -> str:
        """Create a human-readable summary table"""
        lines = []
        lines.append("=" * 80)
        lines.append("CRANK SCHEDULER - EXECUTION PLAN")
        lines.append("=" * 80)
        lines.append(f"Total Estimated Time: {schedule.total_estimated_time:.1f} minutes")
        lines.append(f"Number of Stages: {len(schedule.stages)}")
        lines.append("")
        
        # Machine utilization
        utilization = schedule.get_machine_utilization()
        if utilization:
            lines.append("MACHINE UTILIZATION:")
            lines.append("-" * 30)
            for machine, util in sorted(utilization.items()):
                lines.append(f"{machine:20s} {util:6.1f}%")
            lines.append("")
        
        # Stage-by-stage breakdown
        lines.append("STAGE BREAKDOWN:")
        lines.append("-" * 50)
        
        for stage in schedule.stages:
            lines.append(f"\nStage {stage.stage_id} (Duration: {stage.estimated_duration:.1f} min)")
            lines.append("  Queue | Scenario                    | Runtime (min) | Machines")
            lines.append("  ------|-----------------------------|--------------|---------")
            
            for assignment in stage.assignments:
                machine_list = ", ".join(assignment.get_machine_names())
                sut_machine = assignment.machines.get(MachineType.SUT)
                runtime = assignment.scenario.estimated_runtime or 0.0
                
                if sut_machine is None:
                    raise ValueError(f"No SUT machine assigned for scenario: {assignment.scenario.name}")
                
                # Append SUT machine to scenario name
                scenario_name = assignment.scenario.name
                scenario_with_machine = f"{scenario_name}-{sut_machine.name}"
                
                lines.append(f"  {assignment.queue_id:4d}  | {scenario_with_machine:27s} | {runtime:12.1f} | {machine_list}")
        
        return "\n".join(lines)
    