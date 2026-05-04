"""
JSON configuration loader for pod-based scheduling.
"""

import json
import re
from typing import Any, Dict

from models import (
    PipelineSettings,
    Pod,
    Scenario,
    ScenarioType,
    ScheduleConfig,
)


class ConfigError(ValueError):
    """Raised when a JSON config is malformed or self-inconsistent."""


_CRON_HOUR_RE = re.compile(r"^\d+(/\d+)?$")


def _require(node: Dict[str, Any], key: str, context: str) -> Any:
    if key not in node:
        raise ConfigError(f"Missing required field '{key}' in {context}")
    return node[key]


def _validate_cron(schedule: str) -> None:
    """Confirm we can later offset the cron's hour field deterministically."""
    parts = schedule.split()
    if len(parts) != 5:
        raise ConfigError(
            f"Schedule {schedule!r} is not a 5-field cron expression"
        )
    if not _CRON_HOUR_RE.match(parts[1]):
        raise ConfigError(
            f"Schedule {schedule!r} uses an unsupported hour field "
            f"{parts[1]!r}. Pod-scheduler only supports 'H' or 'H/N' here so "
            f"it can offset the hour for split YAMLs without ambiguity."
        )


def load_config(path: str) -> ScheduleConfig:
    """Load and validate a pod-scheduler JSON configuration file."""
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    metadata = _require(data, "metadata", "config root")
    schedule = _require(metadata, "schedule", "metadata")
    _validate_cron(schedule)

    queues = _require(metadata, "queues", "metadata")
    if not isinstance(queues, list) or not queues:
        raise ConfigError("metadata.queues must be a non-empty list")
    if len(queues) != len(set(queues)):
        raise ConfigError(f"metadata.queues contains duplicates: {queues}")

    yaml_gen = metadata.get("yaml_generation", {})

    pipeline_meta = metadata.get("pipeline", {})
    pipeline = PipelineSettings(
        pool=pipeline_meta.get("pool", PipelineSettings.pool),
        service_bus_connection=pipeline_meta.get(
            "service_bus_connection",
            PipelineSettings.service_bus_connection,
        ),
        service_bus_namespace=pipeline_meta.get(
            "service_bus_namespace",
            PipelineSettings.service_bus_namespace,
        ),
    )

    pods: Dict[str, Pod] = {}
    raw_pods = _require(data, "pods", "config root")
    for pod_data in raw_pods:
        pod_name = _require(pod_data, "name", "pod entry")
        if pod_name in pods:
            raise ConfigError(f"Duplicate pod name: {pod_name!r}")
        machines = _require(pod_data, "machines", f"pod '{pod_name}'")
        profiles = _require(pod_data, "profiles", f"pod '{pod_name}'")
        pods[pod_name] = Pod(
            name=pod_name,
            sut=_require(machines, "sut", f"pod '{pod_name}'.machines"),
            load=machines.get("load"),
            db=machines.get("db"),
            sut_profile=_require(profiles, "sut", f"pod '{pod_name}'.profiles"),
            load_profile=profiles.get("load"),
            db_profile=profiles.get("db"),
        )

    scenarios = []
    raw_scenarios = _require(data, "scenarios", "config root")
    for sc_data in raw_scenarios:
        name = _require(sc_data, "name", "scenario entry")
        scenario_pods = _require(sc_data, "pods", f"scenario '{name}'")
        if not scenario_pods:
            raise ConfigError(f"scenario '{name}' has empty pods list")
        if len(scenario_pods) != len(set(scenario_pods)):
            dupes = sorted({
                p for p in scenario_pods if scenario_pods.count(p) > 1
            })
            raise ConfigError(
                f"scenario '{name}' lists duplicate pods: {dupes}"
            )
        runtime_raw = sc_data.get("estimated_runtime") or 0
        timeout = sc_data.get("timeout")
        if timeout is not None:
            timeout = int(timeout)
            if timeout <= 0:
                raise ConfigError(
                    f"scenario '{name}' has non-positive timeout {timeout}"
                )
        scenarios.append(Scenario(
            name=name,
            template=_require(sc_data, "template", f"scenario '{name}'"),
            type=ScenarioType(_require(sc_data, "type", f"scenario '{name}'")),
            pods=list(scenario_pods),
            estimated_runtime=float(runtime_raw) if runtime_raw else 0.0,
            timeout=timeout,
        ))

    return ScheduleConfig(
        name=metadata.get("name", ""),
        schedule=schedule,
        queues=list(queues),
        target_yaml_count=yaml_gen.get("target_yaml_count", 1),
        schedule_offset_hours=yaml_gen.get("schedule_offset_hours", 6),
        pods=pods,
        scenarios=scenarios,
        pipeline=pipeline,
    )
