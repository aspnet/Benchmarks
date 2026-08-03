"""Snapshot tests: regenerate the committed YAML files and compare.

Run from the ``scripts/pod-scheduler`` directory:

    python -m unittest discover tests
"""

import os
import tempfile
import unittest

import tests  # noqa: F401  # ensures sys.path is set up

from config_loader import load_config
from generator import generate_yamls
from scheduler import create_schedule, split_schedule


_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.abspath(os.path.join(_HERE, "..", "..", ".."))
_BUILD = os.path.join(_REPO, "build")


CASES = [
    ("benchmarks_ci_pods.json", "benchmarks-ci",
     ["benchmarks-ci-01.yml", "benchmarks-ci-02.yml"]),
    ("benchmarks_ci_azure_pods.json", "benchmarks-ci-azure",
     ["benchmarks-ci-azure.yml"]),
    ("benchmarks_ci_cobalt_pods.json", "benchmarks-ci-cobalt",
     ["benchmarks-ci-cobalt.yml"]),
]


class TestSnapshots(unittest.TestCase):
    def test_each_config_produces_committed_yaml(self):
        for config_name, base_name, expected_files in CASES:
            with self.subTest(config=config_name):
                config_path = os.path.join(_BUILD, config_name)
                self.assertTrue(
                    os.path.exists(config_path), f"missing {config_path}"
                )
                config = load_config(config_path)
                schedule = create_schedule(config)
                schedules = split_schedule(schedule, config.target_yaml_count)

                with tempfile.TemporaryDirectory() as tmp:
                    generate_yamls(
                        schedules, config, tmp, base_name=base_name,
                        source_config=f"./build/{config_name}",
                    )
                    for name in expected_files:
                        generated = os.path.join(tmp, name)
                        committed = os.path.join(_BUILD, name)
                        self.assertTrue(
                            os.path.exists(generated),
                            f"generator did not produce {generated}",
                        )
                        self.assertTrue(
                            os.path.exists(committed),
                            f"committed YAML missing: {committed}",
                        )
                        with open(generated, encoding="utf-8") as f:
                            gen_text = f.read()
                        with open(committed, encoding="utf-8") as f:
                            comm_text = f.read()
                        self.assertEqual(
                            gen_text,
                            comm_text,
                            f"{name} differs from committed snapshot. "
                            f"Run from repo root: python "
                            f"scripts/pod-scheduler/main.py "
                            f"--config build/{config_name} "
                            f"--yaml-output build "
                            f"--base-name {base_name}",
                        )


if __name__ == "__main__":
    unittest.main()
