"""
Configuration loader for pod-based scheduling.

Accepts YAML (`.yml`/`.yaml`) and JSON (`.json`) files. YAML is preferred so
configs can carry inline comments; JSON is supported for back-compat. The
file format is dispatched purely on extension.
"""

import json
import os
import re
from typing import Any, Dict, Optional, Union

import yaml

from models import (
    PipelineSettings,
    Pod,
    Scenario,
    ScenarioType,
    ScheduleConfig,
)


class ConfigError(ValueError):
    """Raised when a config is malformed or self-inconsistent."""


_CRON_HOUR_RE = re.compile(r"^\d+(/\d+)?$")

# Accepted spellings for ``scenario.type``. Strings (case-insensitive) are the
# preferred form; integers stay supported so legacy configs keep loading.
_SCENARIO_TYPE_ALIASES: Dict[Union[str, int], ScenarioType] = {
    "single": ScenarioType.SINGLE,
    "dual":   ScenarioType.DUAL,
    "triple": ScenarioType.TRIPLE,
    1:        ScenarioType.SINGLE,
    2:        ScenarioType.DUAL,
    3:        ScenarioType.TRIPLE,
}


def _reject_bool(raw: Any, context: str) -> None:
    """Reject Python bools in numeric fields.

    PyYAML's ``safe_load`` happily turns ``yes``/``no``/``true``/``false``/
    ``on``/``off`` into Python bools, and because ``bool`` is a subclass of
    ``int`` those values would otherwise sail through ``int(...)`` /
    ``float(...)`` coercions and silently resolve to ``1`` or ``0``.
    """
    if isinstance(raw, bool):
        raise ConfigError(
            f"{context} must not be a boolean (YAML coerces "
            f"yes/no/true/false into bools); got {raw!r}"
        )


def _coerce_int(raw: Any, *, context: str, minimum: Optional[int] = None) -> int:
    """Parse a value as an integer with bool/range checking."""
    _reject_bool(raw, context)
    if isinstance(raw, int):
        value = raw
    else:
        try:
            value = int(raw)
        except (TypeError, ValueError):
            raise ConfigError(f"{context} must be an integer, got {raw!r}")
    if minimum is not None and value < minimum:
        raise ConfigError(f"{context} must be >= {minimum}, got {value}")
    return value


def _coerce_float(raw: Any, *, context: str, minimum: Optional[float] = None) -> float:
    """Parse a value as a float with bool/range checking."""
    _reject_bool(raw, context)
    if not isinstance(raw, (int, float)):
        raise ConfigError(f"{context} must be a number, got {raw!r}")
    value = float(raw)
    if minimum is not None and value < minimum:
        raise ConfigError(f"{context} must be >= {minimum}, got {value}")
    return value


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


def _parse_scenario_type(raw: Any, scenario_name: str) -> ScenarioType:
    """Resolve a scenario type from string, int, or ScenarioType.

    Accepts ``single``/``dual``/``triple`` (case-insensitive) or ``1``/``2``/``3``.
    Bools are rejected explicitly because YAML happily turns ``yes``/``no``
    into bools, which would otherwise quietly resolve to ``1``/``0``.
    """
    if isinstance(raw, ScenarioType):
        return raw
    if isinstance(raw, bool):
        raise ConfigError(
            f"scenario '{scenario_name}' has invalid type {raw!r}; "
            f"use 'single', 'dual', or 'triple' (or 1/2/3)"
        )
    key: Any = raw
    if isinstance(raw, str):
        key = raw.strip().lower()
    if key not in _SCENARIO_TYPE_ALIASES:
        raise ConfigError(
            f"scenario '{scenario_name}' has invalid type {raw!r}; "
            f"use 'single', 'dual', or 'triple' (or 1/2/3)"
        )
    return _SCENARIO_TYPE_ALIASES[key]


def _load_raw(path: str) -> Any:
    """Read the config file as a Python dict, dispatching on extension."""
    ext = os.path.splitext(path)[1].lower()
    with open(path, "r", encoding="utf-8") as f:
        text = f.read()
    if ext in (".yml", ".yaml"):
        try:
            return yaml.safe_load(text)
        except yaml.YAMLError as exc:
            raise ConfigError(f"Failed to parse YAML config {path}: {exc}")
    if ext == ".json":
        try:
            return json.loads(text)
        except json.JSONDecodeError as exc:
            raise ConfigError(f"Failed to parse JSON config {path}: {exc}")
    raise ConfigError(
        f"Unsupported config extension {ext!r}; expected .yml, .yaml, or .json"
    )


def load_config(path: str) -> ScheduleConfig:
    """Load and validate a pod-scheduler configuration file (YAML or JSON)."""
    data = _load_raw(path)
    if not isinstance(data, dict):
        raise ConfigError(
            f"Config root must be a mapping, got {type(data).__name__}"
        )

    metadata = _require(data, "metadata", "config root")
    schedule = _require(metadata, "schedule", "metadata")
    _validate_cron(schedule)

    queues = _require(metadata, "queues", "metadata")
    if not isinstance(queues, list) or not queues:
        raise ConfigError("metadata.queues must be a non-empty list")
    if len(queues) != len(set(queues)):
        raise ConfigError(f"metadata.queues contains duplicates: {queues}")

    yaml_gen = metadata.get("yaml_generation", {}) or {}
    target_yaml_raw = yaml_gen.get("target_yaml_count", 1)
    target_yaml_count = _coerce_int(
        target_yaml_raw,
        context="metadata.yaml_generation.target_yaml_count",
        minimum=1,
    )
    offset_raw = yaml_gen.get("schedule_offset_hours", 6)
    schedule_offset_hours = _coerce_int(
        offset_raw,
        context="metadata.yaml_generation.schedule_offset_hours",
        minimum=0,
    )

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
        runtime_raw = sc_data.get("estimated_runtime")
        if runtime_raw is None or runtime_raw == 0:
            estimated_runtime = 0.0
        else:
            estimated_runtime = _coerce_float(
                runtime_raw,
                context=f"scenario '{name}'.estimated_runtime",
                minimum=0.0,
            )
        timeout_raw = sc_data.get("timeout")
        if timeout_raw is None:
            timeout = None
        else:
            timeout = _coerce_int(
                timeout_raw,
                context=f"scenario '{name}'.timeout",
                minimum=1,
            )
        raw_type = _require(sc_data, "type", f"scenario '{name}'")
        scenarios.append(Scenario(
            name=name,
            template=_require(sc_data, "template", f"scenario '{name}'"),
            type=_parse_scenario_type(raw_type, name),
            pods=list(scenario_pods),
            estimated_runtime=estimated_runtime,
            timeout=timeout,
        ))

    return ScheduleConfig(
        name=metadata.get("name", ""),
        schedule=schedule,
        queues=list(queues),
        target_yaml_count=target_yaml_count,
        schedule_offset_hours=schedule_offset_hours,
        pods=pods,
        scenarios=scenarios,
        pipeline=pipeline,
    )
