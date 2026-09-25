"""
JSON configuration loader for pod-based scheduling.
"""

import json
import os
import re
from typing import Any, Dict

from models import (
    PerfLabLane,
    PipelineSettings,
    Pod,
    Scenario,
    ScenarioType,
    ScheduleConfig,
)


class ConfigError(ValueError):
    """Raised when a JSON config is malformed or self-inconsistent."""


_CRON_HOUR_RE = re.compile(r"^\d+(/\d+)?$")
_TREND_TEMPLATES = {
    "trend-scenarios.yml",
    "trend-database-scenarios.yml",
}


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


def _load_trend_lanes(
    config_path: str,
    metadata: Dict[str, Any],
) -> Dict[str, PerfLabLane]:
    registry_name = metadata.get("trend_lane_registry")
    if not registry_name:
        return {}

    registry_path = os.path.join(os.path.dirname(config_path), registry_name)
    try:
        with open(registry_path, "r", encoding="utf-8") as f:
            registry = json.load(f)
    except (OSError, json.JSONDecodeError) as error:
        raise ConfigError(
            f"Could not load Trend lane registry {registry_path!r}: {error}"
        ) from error

    if registry.get("schemaVersion") != 1:
        raise ConfigError(
            f"Trend lane registry {registry_path!r} must use schemaVersion 1"
        )

    lanes: Dict[str, PerfLabLane] = {}
    raw_lanes = _require(registry, "lanes", "Trend lane registry")
    for pod_name, lane_data in raw_lanes.items():
        if not isinstance(lane_data, dict):
            raise ConfigError(
                f"Trend lane registry entry {pod_name!r} must be an object"
            )
        try:
            cores = int(
                _require(lane_data, "cores", f"Trend lane '{pod_name}'")
            )
        except (TypeError, ValueError) as error:
            raise ConfigError(
                f"Trend lane '{pod_name}' cores must be an integer"
            ) from error
        if cores <= 0:
            raise ConfigError(
                f"Trend lane '{pod_name}' has non-positive cores {cores}"
            )
        lanes[pod_name] = PerfLabLane(
            name=_require(lane_data, "name", f"Trend lane '{pod_name}'"),
            queue=_require(lane_data, "queue", f"Trend lane '{pod_name}'"),
            os=_require(lane_data, "os", f"Trend lane '{pod_name}'"),
            architecture=_require(
                lane_data, "architecture", f"Trend lane '{pod_name}'"
            ),
            locale=_require(lane_data, "locale", f"Trend lane '{pod_name}'"),
            cores=cores,
            hardware=_require(
                lane_data, "hardware", f"Trend lane '{pod_name}'"
            ),
        )
        if any(
            not str(value).strip()
            for value in (
                lanes[pod_name].name,
                lanes[pod_name].queue,
                lanes[pod_name].os,
                lanes[pod_name].architecture,
                lanes[pod_name].locale,
                lanes[pod_name].hardware,
            )
        ):
            raise ConfigError(
                f"Trend lane '{pod_name}' contains an empty identity value"
            )

    return lanes


def load_config(path: str) -> ScheduleConfig:
    """Load and validate a pod-scheduler JSON configuration file."""
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)

    metadata = _require(data, "metadata", "config root")
    trend_lanes = _load_trend_lanes(path, metadata)
    schedule = _require(metadata, "schedule", "metadata")
    _validate_cron(schedule)

    queues = _require(metadata, "queues", "metadata")
    if not isinstance(queues, list) or not queues:
        raise ConfigError("metadata.queues must be a non-empty list")
    if len(queues) != len(set(queues)):
        raise ConfigError(f"metadata.queues contains duplicates: {queues}")
    routing_lane_overlap = sorted({
        lane.queue for lane in trend_lanes.values() if lane.queue in queues
    })
    if routing_lane_overlap:
        raise ConfigError(
            "Service Bus routing queues cannot be used as PerfLab queues: "
            f"{routing_lane_overlap}"
        )

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
        trend_benchmarks_raw_base_url=pipeline_meta.get(
            "trend_benchmarks_raw_base_url",
            PipelineSettings.trend_benchmarks_raw_base_url,
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
            perf_lab_lane=trend_lanes.get(pod_name),
        )

    scenarios = []
    raw_scenarios = _require(data, "scenarios", "config root")
    for sc_data in raw_scenarios:
        name = _require(sc_data, "name", "scenario entry")
        template = _require(sc_data, "template", f"scenario '{name}'")
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
        if (
            template in _TREND_TEMPLATES
            and "enable_perf_lab_publication" not in sc_data
        ):
            raise ConfigError(
                f"Trend scenario '{name}' must explicitly set "
                "enable_perf_lab_publication"
            )
        enable_publication = sc_data.get(
            "enable_perf_lab_publication",
            False,
        )
        if not isinstance(enable_publication, bool):
            raise ConfigError(
                f"scenario '{name}' enable_perf_lab_publication must be "
                "a boolean"
            )
        if enable_publication and (
            template not in _TREND_TEMPLATES or len(scenario_pods) != 1
        ):
            raise ConfigError(
                "PerfLab publication may only be enabled for an explicit "
                "single-pod Trend canary"
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
            template=template,
            type=ScenarioType(_require(sc_data, "type", f"scenario '{name}'")),
            pods=list(scenario_pods),
            estimated_runtime=float(runtime_raw) if runtime_raw else 0.0,
            enable_perf_lab_publication=enable_publication,
            timeout=timeout,
        ))

    for scenario in scenarios:
        if scenario.template not in _TREND_TEMPLATES:
            continue
        raw_base_url = pipeline.trend_benchmarks_raw_base_url
        if not raw_base_url:
            raise ConfigError(
                "Trend scenarios require "
                "metadata.pipeline.trend_benchmarks_raw_base_url"
            )
        if (
            "raw.githubusercontent.com/aspnet/Benchmarks/main"
            in raw_base_url
            or "$(Build.SourceVersion)" not in raw_base_url
        ):
            raise ConfigError(
                "Trend Benchmarks raw base URL must be pinned to "
                "$(Build.SourceVersion)"
            )
        for pod_name in scenario.pods:
            if pod_name in pods and pods[pod_name].perf_lab_lane is None:
                raise ConfigError(
                    f"Trend scenario '{scenario.name}' pod '{pod_name}' has "
                    "no entry in metadata.trend_lane_registry"
                )

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
