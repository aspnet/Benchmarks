import re
import unittest
from pathlib import Path
from urllib.parse import urlsplit

import yaml


ROOT = Path(__file__).resolve().parents[2]
CONFIGS = {
    "aspnetcore": ("windows", ["aspnet-gold-lin", "aspnet-gold-win"]),
    "efcore": ("windows", ["aspnet-gold-win"]),
    "runtime.windows_x64": ("windows", ["aspnet-gold-win"]),
    "runtime.linux_x64": ("linux", ["aspnet-gold-lin"]),
}


def read_yaml(path):
    return yaml.safe_load(path.read_text(encoding="utf-8-sig"))


def repository_path(url):
    parsed = urlsplit(url)
    parts = parsed.path.strip("/").split("/")
    if (
        parsed.netloc == "raw.githubusercontent.com"
        and [part.lower() for part in parts[:3]] == ["aspnet", "benchmarks", "main"]
    ):
        return ROOT.joinpath(*parts[3:])
    return None


def imported_documents(path, seen=None):
    if seen is None:
        seen = set()
    if path in seen:
        return
    seen.add(path)
    document = read_yaml(path)
    for url in document.get("imports", []):
        imported = repository_path(url)
        if imported is not None:
            yield from imported_documents(imported, seen)
    yield document


class PrBenchmarksTests(unittest.TestCase):
    def test_supported_configs_and_default_profiles(self):
        self.assertEqual(
            {path.name for path in (ROOT / "build").glob("prbenchmarks.*.config.yml")},
            {f"prbenchmarks.{name}.config.yml" for name in CONFIGS},
        )
        for name, (_, profiles) in CONFIGS.items():
            with self.subTest(config=name):
                config = read_yaml(ROOT / "build" / f"prbenchmarks.{name}.config.yml")
                self.assertEqual(list(config["profiles"]), profiles)
                self.assertIn("--relay", config["defaults"].split())
                for profile, settings in config["profiles"].items():
                    self.assertEqual(settings["arguments"], f"--profile {profile}-relay")
                    self.assertIn("x64 56 Cores", settings["description"])
                if name.startswith("runtime."):
                    for component in config["components"].values():
                        self.assertIn("-arch x64", component["script"])
                        self.assertNotIn("arm64", component["arguments"])

    def test_every_scenario_imports_selected_gold_profiles(self):
        for name in CONFIGS:
            config = read_yaml(ROOT / "build" / f"prbenchmarks.{name}.config.yml")
            for benchmark, settings in config["benchmarks"].items():
                with self.subTest(config=name, benchmark=benchmark):
                    arguments = settings["arguments"].split()
                    scenario_path = repository_path(arguments[arguments.index("--config") + 1])
                    self.assertIsNotNone(scenario_path)
                    scenario = arguments[arguments.index("--scenario") + 1]
                    self.assertIn(scenario, read_yaml(scenario_path)["scenarios"])
                    profiles = {}
                    for document in imported_documents(scenario_path):
                        profiles.update(document.get("profiles", {}))
                    for profile in config["profiles"]:
                        relay = profiles[f"{profile}-relay"]
                        platform = "lin" if profile.endswith("-lin") else "win"
                        expected = {
                            "main": f"gold{platform}",
                            "secondary": "goldload" if platform == "lin" else "goldload2",
                            "extra": "golddb",
                        }
                        agents = {}
                        for key, agent in relay.get("agents", relay.get("jobs", {})).items():
                            agents[key] = agent
                            for alias in agent.get("aliases", []):
                                agents[alias] = agent
                        for agent, endpoint in expected.items():
                            self.assertEqual(
                                agents[agent]["endpoints"],
                                [f"https://aspnetperf.servicebus.windows.net/{endpoint}"],
                            )

    def test_pipeline_dispatches_each_config_once_on_correct_controller(self):
        pipeline = read_yaml(ROOT / "build" / "prbenchmarks.yml")
        dispatched = []
        labels = []
        jobs = pipeline["extends"]["parameters"]["stages"][0]["jobs"]
        for job in jobs:
            for step in job["steps"]:
                if step.get("task") != "AzureCLI@2":
                    continue
                script = step["inputs"]["inlineScript"]
                match = re.search(r"/build/prbenchmarks\.(\S+)\.config\.yml", script)
                self.assertIsNotNone(match)
                name = match.group(1)
                self.assertIn(name, CONFIGS)
                self.assertEqual(job["pool"]["os"], CONFIGS[name][0])
                self.assertEqual(
                    step["inputs"]["scriptType"],
                    "ps" if CONFIGS[name][0] == "windows" else "pscore",
                )
                repository = name.split(".")[0]
                self.assertIn(f"--repository https://github.com/dotnet/{repository}", script)
                self.assertIn("Gold", step["displayName"])
                self.assertIn("X64", step["displayName"])
                if repository != "aspnetcore":
                    self.assertIn(CONFIGS[name][0].capitalize(), step["displayName"])
                dispatched.append(name)
                labels.append(step["displayName"])
        self.assertCountEqual(dispatched, CONFIGS)
        self.assertEqual(len(labels), len(set(labels)))
        self.assertEqual([schedule["cron"] for schedule in pipeline["schedules"]],
                         ["0 * * * *", "30 * * * *"])


if __name__ == "__main__":
    unittest.main()
