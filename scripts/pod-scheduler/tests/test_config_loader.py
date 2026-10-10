import json
import os
import tempfile
import unittest

import yaml

import tests  # noqa: F401  # ensures sys.path is set up

from config_loader import ConfigError, load_config


_BASE = {
    "metadata": {
        "name": "t",
        "schedule": "0 3/12 * * *",
        "queues": ["a", "b"],
    },
    "pods": [
        {
            "name": "p1",
            "machines": ["m1"],
            "profiles": ["m1-app"],
        }
    ],
    "scenarios": [
        {
            "name": "S",
            "template": "s.yml",
            "type": "single",
            "pods": ["p1"],
        }
    ],
}


def _write_yaml(tmp, payload):
    path = os.path.join(tmp, "cfg.yml")
    with open(path, "w", encoding="utf-8") as f:
        yaml.safe_dump(payload, f, sort_keys=False)
    return path


def _write_json(tmp, payload):
    path = os.path.join(tmp, "cfg.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f)
    return path


# Default fixture writer used by tests that don't care which format. Using
# YAML here also exercises the YAML path on every test so regressions in
# YAML loading show up immediately, not only in the snapshot test.
_write = _write_yaml


class TestLoadConfig(unittest.TestCase):
    def test_happy_path(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = _write(tmp, _BASE)
            cfg = load_config(path)
            self.assertIn("p1", cfg.pods)
            self.assertEqual(len(cfg.scenarios), 1)
            self.assertEqual(cfg.pipeline.pool, "server")

    def test_unsupported_cron_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["metadata"]["schedule"] = "0 * * * *"
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_empty_queues_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["metadata"]["queues"] = []
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_duplicate_pods_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["pods"].append(dict(payload["pods"][0]))
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_duplicate_pods_in_scenario_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["pods"] = ["p1", "p1"]
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_pipeline_settings_loaded(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["metadata"]["pipeline"] = {
                "pool": "custompool",
                "service_bus_namespace": "myns",
            }
            path = _write(tmp, payload)
            cfg = load_config(path)
            self.assertEqual(cfg.pipeline.pool, "custompool")
            self.assertEqual(cfg.pipeline.service_bus_namespace, "myns")
            self.assertEqual(
                cfg.pipeline.service_bus_connection,
                "ASPNET Benchmarks Service Bus",
            )

    def test_optional_timeout_loaded(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["timeout"] = 199
            path = _write(tmp, payload)
            cfg = load_config(path)
            self.assertEqual(cfg.scenarios[0].timeout, 199)

    def test_negative_timeout_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["timeout"] = 0
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_scenario_type_string_aliases(self):
        # All three string spellings + case-insensitivity must resolve.
        for raw in ["single", "DUAL", "Triple"]:
            with tempfile.TemporaryDirectory() as tmp:
                payload = json.loads(json.dumps(_BASE))
                payload["scenarios"][0]["type"] = raw
                if raw.lower() in ("dual", "triple"):
                    payload["pods"][0]["machines"].append("m2")
                    payload["pods"][0]["profiles"].append("m2-load")
                if raw.lower() == "triple":
                    payload["pods"][0]["machines"].append("m3")
                    payload["pods"][0]["profiles"].append("m3-db")
                path = _write(tmp, payload)
                cfg = load_config(path)
                self.assertEqual(
                    cfg.scenarios[0].type,
                    {"single": 1, "dual": 2, "triple": 3}[raw.lower()],
                    raw,
                )

    def test_scenario_type_integer_back_compat(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["type"] = 1
            path = _write(tmp, payload)
            cfg = load_config(path)
            self.assertEqual(cfg.scenarios[0].type, 1)

    def test_scenario_type_invalid_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["type"] = "quad"
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_scenario_type_bool_rejected(self):
        # YAML loaders coerce ``yes``/``no`` to bool; the loader must catch
        # those before they silently resolve to True/False (== 1/0).
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["type"] = True
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_timeout_bool_rejected(self):
        # ``timeout: yes`` would otherwise become 1 because bool subclasses int.
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["timeout"] = True
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_estimated_runtime_bool_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["estimated_runtime"] = True
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_target_yaml_count_bool_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["metadata"]["yaml_generation"] = {"target_yaml_count": True}
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_schedule_offset_hours_bool_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["metadata"]["yaml_generation"] = {
                "schedule_offset_hours": False,
                "target_yaml_count": 1,
            }
            # ``False`` is technically a falsy bool that would have coerced
            # to 0 silently. Make sure we reject it.
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_target_yaml_count_below_minimum_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["metadata"]["yaml_generation"] = {"target_yaml_count": 0}
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_estimated_runtime_negative_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["estimated_runtime"] = -5
            path = _write(tmp, payload)
            with self.assertRaises(ConfigError):
                load_config(path)

    def test_json_back_compat(self):
        # Existing .json configs must still load even though the project has
        # moved to YAML. Use the integer-typed fixture so this exercises the
        # legacy shape end-to-end.
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["type"] = 1
            path = _write_json(tmp, payload)
            cfg = load_config(path)
            self.assertEqual(cfg.scenarios[0].type, 1)

    def test_unknown_extension_loads_via_yaml(self):
        # The loader no longer dispatches on extension; YAML parses any
        # text file (including .txt) and JSON-as-YAML works transparently.
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            path = os.path.join(tmp, "cfg.txt")
            with open(path, "w", encoding="utf-8") as f:
                yaml.safe_dump(payload, f)
            cfg = load_config(path)
            self.assertIn("p1", cfg.pods)


class TestPodRoles(unittest.TestCase):
    """Parallel-array form for ``machines`` / ``profiles``."""

    def _payload(self, machines, profiles, scenario_type="triple"):
        return {
            "metadata": {
                "name": "t",
                "schedule": "0 3/12 * * *",
                "queues": ["a", "b"],
            },
            "pods": [
                {"name": "p1", "machines": machines, "profiles": profiles},
            ],
            "scenarios": [
                {
                    "name": "S",
                    "template": "s.yml",
                    "type": scenario_type,
                    "pods": ["p1"],
                }
            ],
        }

    def test_triple_happy_path(self):
        payload = self._payload(["m1", "m2", "m3"], ["m1-app", "m2-load", "m3-db"])
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, payload))
            pod = cfg.pods["p1"]
            self.assertEqual(pod.machines, ["m1", "m2", "m3"])
            self.assertEqual(pod.profiles, ["m1-app", "m2-load", "m3-db"])

    def test_dual_happy_path(self):
        payload = self._payload(["m1", "m2"], ["m1-app", "m2-load"], "dual")
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, payload))
            pod = cfg.pods["p1"]
            self.assertEqual(pod.machines, ["m1", "m2"])
            self.assertEqual(pod.profiles, ["m1-app", "m2-load"])

    def test_single_happy_path(self):
        payload = self._payload(["m1"], ["m1-app"], "single")
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, payload))
            pod = cfg.pods["p1"]
            self.assertEqual(pod.machines, ["m1"])
            self.assertEqual(pod.profiles, ["m1-app"])

    def test_length_mismatch_rejected(self):
        # 3 machines, 2 profiles — each machine must pair with one profile.
        payload = self._payload(["m1", "m2", "m3"], ["m1-app", "m2-load"])
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_machines_must_be_unique(self):
        # Same machine listed twice in one pod — ambiguous role mapping.
        payload = self._payload(["m1", "m1"], ["m1-app", "m1-load"], "dual")
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_profiles_must_be_unique(self):
        payload = self._payload(["m1", "m2"], ["m1-app", "m1-app"], "dual")
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_empty_list_rejected(self):
        payload = self._payload([], [])
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_too_long_list_rejected(self):
        payload = self._payload(
            ["m1", "m2", "m3", "m4"],
            ["p1", "p2", "p3", "p4"],
        )
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_dict_form_rejected(self):
        # Old named-key form is no longer accepted.
        payload = self._payload(
            {"sut": "m1", "load": "m2"},
            {"sut": "m1-app", "load": "m2-load"},
            "dual",
        )
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_bool_entry_in_list_rejected(self):
        # YAML's yes/no/true/false coerce to bool; reject in role lists.
        payload = self._payload(["m1", True], ["m1-app", "m2-load"], "dual")
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_global_machine_can_have_different_profiles_across_pods(self):
        # The same physical machine can legitimately serve different
        # profiles in different pods (e.g., 'azure2-db' is db for one pod
        # and load for another). The loader does NOT enforce a global
        # 1:1 machine->profile invariant; only per-pod pairing.
        payload = self._payload(["m1", "shared"], ["m1-app", "shared-db"], "dual")
        payload["pods"].append({
            "name": "p2",
            "machines": ["m9", "shared"],
            "profiles": ["m9-app", "shared-load"],
        })
        payload["scenarios"][0]["pods"] = ["p1", "p2"]
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, payload))
            self.assertEqual(cfg.pods["p1"].profiles[1], "shared-db")
            self.assertEqual(cfg.pods["p2"].profiles[1], "shared-load")


if __name__ == "__main__":
    unittest.main()
