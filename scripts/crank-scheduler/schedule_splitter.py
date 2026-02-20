"""
Schedule splitting functionality for multi-YAML generation
"""

from typing import List, Optional

from models import CombinedConfiguration, PartialSchedule, Schedule
from utils import ScheduleExporter


class ScheduleSplitter:
    """Splits a schedule across multiple YAML files with balanced runtimes"""

    def __init__(self, config: CombinedConfiguration):
        self.config = config
        self.yaml_config = config.metadata.yaml_generation

    def split_schedule(self, schedule: Schedule) -> List[PartialSchedule]:
        """Split schedule using balanced runtime strategy"""
        if not self.yaml_config:
            # No split configuration, return single schedule
            return [PartialSchedule(
                name="full",
                stages=schedule.stages.copy(),
                total_estimated_time=schedule.total_estimated_time
            )]

        return self._split_by_balanced_runtime(schedule)

    def _split_by_balanced_runtime(self, schedule: Schedule) -> List[PartialSchedule]:
        """Use bin-packing to balance runtime across multiple schedules"""
        if not self.yaml_config:
            raise ValueError("No YAML generation configuration provided")

        target_count = self.yaml_config.target_yaml_count

        if target_count <= 1:
            return [PartialSchedule(
                name="full",
                stages=schedule.stages.copy(),
                total_estimated_time=schedule.total_estimated_time
            )]

        # Sort stages by runtime (largest first for better bin-packing)
        stages_with_runtime = [(stage, stage.estimated_duration or 0)
                               for stage in schedule.stages]
        stages_with_runtime.sort(key=lambda x: x[1], reverse=True)

        # Initialize bins (partial schedules)
        bins = [PartialSchedule(f"part_{i+1:02d}", [])
                for i in range(target_count)]
        bin_runtimes = [0.0] * target_count

        # Assign each stage to the bin with least current runtime
        for stage, runtime in stages_with_runtime:
            min_bin_idx = bin_runtimes.index(min(bin_runtimes))
            bins[min_bin_idx].add_stage(stage)
            bin_runtimes[min_bin_idx] += runtime

        return bins

    def generate_schedules(self, base_schedule: str) -> List[str]:
        """Generate multiple cron schedules with time offsets"""
        if not self.yaml_config:
            return [base_schedule]

        target_count = self.yaml_config.target_yaml_count
        offset_hours = self.yaml_config.schedule_offset_hours

        schedules = []

        for i in range(target_count):
            if i == 0:
                # First schedule uses base schedule
                schedules.append(base_schedule)
            else:
                # Generate offset schedule
                offset_schedule = self._offset_cron_schedule(
                    base_schedule, i * offset_hours)
                schedules.append(offset_schedule)

        return schedules

    def _offset_cron_schedule(self, cron_schedule: str, offset_hours: int) -> str:
        """Apply hour offset to a cron schedule"""
        parts = cron_schedule.split()
        if len(parts) != 5:
            # Invalid cron format, return as-is
            return cron_schedule

        minute, hour, day, month, weekday = parts

        # Handle hour patterns like "9/12" (every 12 hours starting at 9)
        if '/' in hour:
            start_hour, interval = hour.split('/')
            start_hour = int(start_hour)
            interval = int(interval)

            new_start_hour = (start_hour + offset_hours) % 24
            new_hour = f"{new_start_hour}/{interval}"
        else:
            # Handle simple hour like "9" or hour list like "9,21"
            if ',' in hour:
                # Hour list
                hours = [int(h.strip()) for h in hour.split(',')]
                new_hours = [(h + offset_hours) % 24 for h in hours]
                new_hour = ','.join(str(h) for h in sorted(new_hours))
            else:
                # Single hour
                try:
                    single_hour = int(hour)
                    new_hour = str((single_hour + offset_hours) % 24)
                except ValueError:
                    # Complex hour pattern, return as-is
                    new_hour = hour

        return f"{minute} {new_hour} {day} {month} {weekday}"


class MultiYamlSummary:
    """Summary information for multi-YAML generation"""

    def __init__(self, yaml_files: List[dict], total_scenarios: int,
                 partial_schedules: Optional[List[PartialSchedule]] = None):
        self.yaml_files = yaml_files
        self.total_scenarios = total_scenarios
        self.total_runtime = sum(f['estimated_runtime'] for f in yaml_files)
        self.partial_schedules = partial_schedules or []

    def print_summary(self):
        """Print a summary of the generated YAML files"""
        print("=" * 60)
        print("MULTI-YAML GENERATION SUMMARY")
        print("=" * 60)
        print(f"Total scenarios: {self.total_scenarios}")
        print(f"Total runtime: {self.total_runtime:.1f} minutes")
        print(f"Generated files: {len(self.yaml_files)}")
        print()

        print("Generated YAML files:")
        print("-" * 50)
        for yaml_info in self.yaml_files:
            print(f"File: {yaml_info['file']}")
            print(f"  Schedule: {yaml_info['schedule']}")
            print(f"  Runtime: {yaml_info['estimated_runtime']:.1f} minutes")
            print(f"  Stages: {yaml_info['stage_count']}")
            print()

        # Show balance statistics
        runtimes = [f['estimated_runtime'] for f in self.yaml_files]
        avg_runtime = sum(runtimes) / len(runtimes)
        max_runtime = max(runtimes)
        min_runtime = min(runtimes)
        balance_ratio = (max_runtime - min_runtime) / \
            avg_runtime * 100 if avg_runtime > 0 else 0

        print("Runtime balance:")
        print(f"  Average: {avg_runtime:.1f} minutes")
        print(f"  Range: {min_runtime:.1f} - {max_runtime:.1f} minutes")
        print(f"  Balance ratio: {balance_ratio:.1f}% (lower is better)")

    def print_execution_plans(self):
        """Print execution plans for each split schedule"""
        if not self.partial_schedules:
            print("No partial schedules available for execution plan display.")
            return

        for (yaml_info, partial_schedule) in zip(self.yaml_files, self.partial_schedules):
            print("\n" + "=" * 80)
            print(f"EXECUTION PLAN FOR {yaml_info['file'].upper()}")
            print("=" * 80)
            print(f"Schedule: {yaml_info['schedule']}")
            print(
                f"Estimated Runtime: {yaml_info['estimated_runtime']:.1f} minutes")
            print(f"Stages: {yaml_info['stage_count']}")
            print()

            # Convert PartialSchedule to Schedule for display
            display_schedule = Schedule(
                stages=partial_schedule.stages,
                total_estimated_time=partial_schedule.total_estimated_time
            )

            # Use the existing ScheduleExporter to format the execution plan
            execution_plan = ScheduleExporter.to_summary_table(
                display_schedule)
            print(execution_plan)
