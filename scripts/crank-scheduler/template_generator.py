"""
Template generation for converting scheduler output to Liquid template format
"""

import json
from pathlib import Path
from typing import Any, Dict, Optional

from liquid import render
from models import CombinedConfiguration, MachineAssignment, PartialSchedule


class TemplateDataGenerator:
    """Converts scheduler output to data format expected by Liquid templates"""

    def __init__(self, config: CombinedConfiguration):
        self.config = config
        self.machine_map = {m.name: m for m in config.machines}
        self.scenario_map = {s.name: s for s in config.scenarios}

    def generate_template_data(self, partial_schedule: PartialSchedule, schedule_time: str) -> Dict[str, Any]:
        """Generate template data for a partial schedule with specific schedule time"""
        groups = []
        queues = self.config.metadata.queues or []

        for stage in partial_schedule.stages:
            group_jobs = []

            for assignment in stage.assignments:
                job_data = self._create_job_from_assignment(assignment)
                if job_data:
                    group_jobs.append(job_data)

            if group_jobs:
                groups.append({
                    "jobs": group_jobs
                })

        template_data = {
            "schedule": schedule_time,
            "queues": queues,
            "groups": groups
        }

        return template_data

    def _create_job_from_assignment(self, assignment: MachineAssignment) -> Optional[Dict[str, Any]]:
        """Create a job entry from a machine assignment"""
        scenario = assignment.scenario

        # Get the scenario template
        template = scenario.template
        if not template:
            print(f"Warning: No template defined for scenario {scenario.name}")
            return None

        # Generate job name based on target machine, not assigned machine
        target_machine_name = assignment.target_machine.replace(
            '-', ' ').title()
        job_name = f"{scenario.name} {target_machine_name}" if target_machine_name else scenario.name
        # Generate profiles from machines
        profiles = []
        for machine_type, _ in assignment.machines.items():
            profile_name = assignment.get_profile_for_machine_type(
                machine_type)
            if profile_name:
                profiles.append(profile_name)

        job_data = {
            "name": job_name,
            "template": template,
            "profiles": profiles
        }

        return job_data

    def save_template_data(self, template_data: Dict[str, Any], output_path: str):
        """Save template data to JSON file for use with Liquid template"""
        with open(output_path, 'w', encoding='utf-8') as f:
            json.dump(template_data, f, indent=2)

        print(f"Template data saved to: {output_path}")

    def generate_yaml_from_template(self, template_data: Dict[str, Any],
                                    template_path: str, output_path: str) -> None:
        """Generate YAML by processing template data through Liquid template"""
        # Read template file
        template_content = Path(template_path).read_text(encoding='utf-8')

        # Process template
        result = render(template_content, **template_data)

        # Save result
        Path(output_path).write_text(result, encoding='utf-8')
        print(f"Generated YAML saved to: {output_path}")


class TemplateCLI:
    """CLI integration for template generation"""

    @staticmethod
    def add_template_arguments(parser):
        """Add template-related arguments to argument parser"""
        template_group = parser.add_argument_group('Template Options')
        template_group.add_argument('--template', type=str,
                                    help='Path to Liquid template file')
        template_group.add_argument('--template-data', action='store_true',
                                    help='Save template data to JSON files (for debugging)')
        template_group.add_argument('--yaml-prefix', type=str,
                                    help='Prefix for generated YAML files (default: <config name>)')
        template_group.add_argument('--target-yamls', type=int,
                                    help='Number of YAML files to generate (overrides config)')
        template_group.add_argument('--schedule-offset', type=int,
                                    help='Hours between each YAML schedule (overrides config)')
