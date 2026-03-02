"""
JSON configuration loader for pod-based scheduling.
"""

import json
from typing import Any, Dict

from models import Pod, Scenario, ScenarioType, ScheduleConfig


def load_config(path: str) -> ScheduleConfig:
    """Load and validate a pod-scheduler JSON configuration file."""
    with open(path, "r") as f:
        data = json.load(f)

    metadata = data["metadata"]
    yaml_gen = metadata.get("yaml_generation", {})

    pods = {}
    for pod_data in data["pods"]:
        machines = pod_data["machines"]
        profiles = pod_data["profiles"]
        pod = Pod(
            name=pod_data["name"],
            sut=machines["sut"],
            load=machines.get("load"),
            db=machines.get("db"),
            sut_profile=profiles["sut"],
            load_profile=profiles.get("load"),
            db_profile=profiles.get("db"),
        )
        pods[pod.name] = pod

    scenarios = []
    for sc_data in data["scenarios"]:
        runtime = sc_data.get("estimated_runtime") or 0
        scenarios.append(Scenario(
            name=sc_data["name"],
            template=sc_data["template"],
            type=ScenarioType(sc_data["type"]),
            pods=sc_data["pods"],
            estimated_runtime=float(runtime) if runtime else 0,
        ))

    return ScheduleConfig(
        name=metadata.get("name", ""),
        schedule=metadata["schedule"],
        queues=metadata["queues"],
        target_yaml_count=yaml_gen.get("target_yaml_count", 1),
        schedule_offset_hours=yaml_gen.get("schedule_offset_hours", 6),
        pods=pods,
        scenarios=scenarios,
    )
