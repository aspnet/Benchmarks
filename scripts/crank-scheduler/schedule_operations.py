"""
Schedule operations for splitting and manipulating schedules
"""

from typing import List, Tuple
from models import Schedule, PartialSchedule, CombinedConfiguration, YamlGenerationConfig
from schedule_splitter import ScheduleSplitter
from template_generator import TemplateDataGenerator
from pathlib import Path
from schedule_splitter import MultiYamlSummary

class ScheduleOperations:
    """Operations for splitting and manipulating schedules"""
    
    @staticmethod
    def create_partial_schedules(schedule: Schedule, config: CombinedConfiguration) -> List[PartialSchedule]:
        """Create partial schedules - either split into multiple or single schedule
        
        Returns:
            List[PartialSchedule]: Always returns a list, even for single schedules
        """
        if not config.metadata.yaml_generation:
            # No split configuration, return single schedule wrapped as partial
            return [PartialSchedule(
                name="full",
                stages=schedule.stages.copy(),
                total_estimated_time=schedule.total_estimated_time
            )]
        
        target_count = config.metadata.yaml_generation.target_yaml_count
        if target_count <= 1:
            # Single schedule requested
            return [PartialSchedule(
                name="full", 
                stages=schedule.stages.copy(),
                total_estimated_time=schedule.total_estimated_time
            )]
        
        # Multiple schedules - use splitter
        splitter = ScheduleSplitter(config)
        return splitter.split_schedule(schedule)
    
    @staticmethod
    def generate_schedule_times(config: CombinedConfiguration, count: int) -> List[str]:
        """Generate schedule times based on configuration
        
        Args:
            config: Combined configuration with schedule settings
            count: Number of schedules to generate
            
        Returns:
            List[str]: List of cron schedule strings
        """
        base_schedule = config.metadata.schedule
        
        if count <= 1:
            return [base_schedule]
        
        if not config.metadata.yaml_generation:
            # No offset configuration, use base schedule for all
            return [base_schedule] * count
        
        # Generate with offsets
        splitter = ScheduleSplitter(config)
        return splitter.generate_schedules(base_schedule)
    
    @staticmethod
    def process_yaml_generation(args, partial_schedules: List[PartialSchedule], config: CombinedConfiguration) -> list:
        """
        Unified flow for YAML generation (single or multi)
        
        Returns:
            bool: True if YAML files were generated, False otherwise
        """        
        # Apply CLI overrides to config if provided
        if args.target_yamls:
            if config.metadata.yaml_generation is None:
                config.metadata.yaml_generation = YamlGenerationConfig()
            config.metadata.yaml_generation.target_yaml_count = args.target_yamls
        
        if args.schedule_offset:
            if config.metadata.yaml_generation is None:
                config.metadata.yaml_generation = YamlGenerationConfig()
            config.metadata.yaml_generation.schedule_offset_hours = args.schedule_offset
        
        schedule_times = ScheduleOperations.generate_schedule_times(config, len(partial_schedules))
        
        # Generate YAML prefix
        if args.yaml_prefix:
            yaml_prefix = args.yaml_prefix
        elif args.config:
            config_path = Path(args.config)
            yaml_prefix = config_path.stem.replace('.', '-').replace('_', '-')
        else:
            yaml_prefix = 'schedule'
        
        # Generate YAML files
        template_generator = TemplateDataGenerator(config)
        yaml_files = []
        
        for i, (partial_schedule, schedule_time) in enumerate(zip(partial_schedules, schedule_times)):
            # Generate template data for this partial schedule
            template_data = template_generator.generate_template_data(partial_schedule, schedule_time)
            
            # Generate output filename
            if len(partial_schedules) == 1:
                filename = f"{yaml_prefix}.yml"
                data_filename = f"{yaml_prefix}_data.json" if args.template_data else None
            else:
                filename = f"{yaml_prefix}-{i+1:02d}.yml"
                data_filename = f"{yaml_prefix}-{i+1:02d}_data.json" if args.template_data else None
            
            # Handle output directory if specified
            if args.yaml_output:
                output_dir = Path(args.yaml_output)
                output_path = str(output_dir.joinpath(filename))
                data_path = str(output_dir.joinpath(data_filename)) if data_filename else None
            else:
                output_path = filename
                data_path = data_filename
            # Save template data if requested
            if args.template_data and data_path:
                template_generator.save_template_data(template_data, data_path)
            
            # Generate YAML file
            success = template_generator.generate_yaml_from_template(template_data, args.template, output_path)
            
            if success:
                yaml_files.append({
                    'file': output_path,
                    'schedule': schedule_time,
                    'estimated_runtime': partial_schedule.total_estimated_time or 0,
                    'stage_count': len(partial_schedule.stages)
                })
        
        return yaml_files
    
    @staticmethod
    def show_partial_schedules(partial_schedules: List[PartialSchedule], config: CombinedConfiguration):
        """Display execution plans for partial schedules
        
        Args:
            partial_schedules: List of partial schedules to display
            config: Combined configuration (used for scenario count)
        """
        
        # Create a summary object to use its display methods
        # Mock yaml_files since we're just showing execution plans
        mock_yaml_files = []
        for i, partial_schedule in enumerate(partial_schedules):
            mock_yaml_files.append({
                'file': f'schedule_{i+1:02d}.yml',
                'schedule': 'N/A',
                'estimated_runtime': partial_schedule.total_estimated_time or 0,
                'stage_count': len(partial_schedule.stages)
            })
        
        summary = MultiYamlSummary(mock_yaml_files, len(config.scenarios), partial_schedules)
        summary.print_execution_plans()
