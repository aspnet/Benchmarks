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
            "machines": {"sut": "m1"},
            "profiles": {"sut": "m1-app"},
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
                    payload["pods"][0]["machines"]["load"] = "m2"
                    payload["pods"][0]["profiles"]["load"] = "m2-load"
                if raw.lower() == "triple":
                    payload["pods"][0]["machines"]["db"] = "m3"
                    payload["pods"][0]["profiles"]["db"] = "m3-db"
                path = _write(tmp, payload)
                cfg = load_config(path)
                self.assertEqual(
                    cfg.scenarios[0].type.value,
                    {"single": 1, "dual": 2, "triple": 3}[raw.lower()],
                    raw,
                )

    def test_scenario_type_integer_back_compat(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            payload["scenarios"][0]["type"] = 1
            path = _write(tmp, payload)
            cfg = load_config(path)
            self.assertEqual(cfg.scenarios[0].type.value, 1)

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
            self.assertEqual(cfg.scenarios[0].type.value, 1)

    def test_unknown_extension_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            payload = json.loads(json.dumps(_BASE))
            path = os.path.join(tmp, "cfg.txt")
            with open(path, "w", encoding="utf-8") as f:
                yaml.safe_dump(payload, f)
            with self.assertRaises(ConfigError):
                load_config(path)


class TestRoleShorthand(unittest.TestCase):
    """Positional shorthand form for ``machines`` / ``profiles``."""

    def _payload(self):
        # Triple-pod payload using shorthand throughout.
        return {
            "metadata": {
                "name": "t",
                "schedule": "0 3/12 * * *",
                "queues": ["a", "b"],
            },
            "pods": [
                {
                    "name": "p1",
                    "machines": ["m1", "m2", "m3"],
                    "profiles": ["m1-app", "m2-load", "m3-db"],
                }
            ],
            "scenarios": [
                {
                    "name": "S",
                    "template": "s.yml",
                    "type": "triple",
                    "pods": ["p1"],
                }
            ],
        }

    def test_list_triple_happy_path(self):
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, self._payload()))
            pod = cfg.pods["p1"]
            self.assertEqual(pod.sut, "m1")
            self.assertEqual(pod.load, "m2")
            self.assertEqual(pod.db, "m3")
            self.assertEqual(pod.sut_profile, "m1-app")
            self.assertEqual(pod.load_profile, "m2-load")
            self.assertEqual(pod.db_profile, "m3-db")

    def test_list_dual_happy_path(self):
        payload = self._payload()
        payload["pods"][0]["machines"] = ["m1", "m2"]
        payload["pods"][0]["profiles"] = ["m1-app", "m2-load"]
        payload["scenarios"][0]["type"] = "dual"
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, payload))
            pod = cfg.pods["p1"]
            self.assertEqual((pod.sut, pod.load, pod.db), ("m1", "m2", None))

    def test_list_single_happy_path(self):
        payload = self._payload()
        payload["pods"][0]["machines"] = ["m1"]
        payload["pods"][0]["profiles"] = ["m1-app"]
        payload["scenarios"][0]["type"] = "single"
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, payload))
            pod = cfg.pods["p1"]
            self.assertEqual((pod.sut, pod.load, pod.db), ("m1", None, None))

    def test_dict_form_still_accepted(self):
        payload = self._payload()
        payload["pods"][0]["machines"] = {"sut": "m1", "load": "m2", "db": "m3"}
        payload["pods"][0]["profiles"] = {
            "sut": "m1-app", "load": "m2-load", "db": "m3-db",
        }
        with tempfile.TemporaryDirectory() as tmp:
            cfg = load_config(_write(tmp, payload))
            pod = cfg.pods["p1"]
            self.assertEqual(pod.db, "m3")
            self.assertEqual(pod.db_profile, "m3-db")

    def test_mixed_shape_rejected(self):
        # machines list, profiles dict — ambiguous role mapping.
        payload = self._payload()
        payload["pods"][0]["profiles"] = {
            "sut": "m1-app", "load": "m2-load", "db": "m3-db",
        }
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_length_mismatch_rejected(self):
        payload = self._payload()
        payload["pods"][0]["machines"] = ["m1", "m2", "m3"]
        payload["pods"][0]["profiles"] = ["m1-app", "m2-load"]
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_empty_list_rejected(self):
        payload = self._payload()
        payload["pods"][0]["machines"] = []
        payload["pods"][0]["profiles"] = []
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_too_long_list_rejected(self):
        payload = self._payload()
        payload["pods"][0]["machines"] = ["m1", "m2", "m3", "m4"]
        payload["pods"][0]["profiles"] = ["m1-app", "m2-load", "m3-db", "m4"]
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_bool_entry_in_shorthand_rejected(self):
        # YAML's yes/no/true/false coerce to bool; reject in role lists too.
        payload = self._payload()
        payload["pods"][0]["machines"] = ["m1", True, "m3"]
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))

    def test_unknown_named_role_rejected(self):
        payload = self._payload()
        payload["pods"][0]["machines"] = {
            "sut": "m1", "loadgen": "m2",  # 'loadgen' is not a valid role
        }
        payload["pods"][0]["profiles"] = {"sut": "m1-app"}
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(ConfigError):
                load_config(_write(tmp, payload))


if __name__ == "__main__":
    unittest.main()
