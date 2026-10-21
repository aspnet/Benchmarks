"""Validate CI cache profiles with Crank's configuration-only debug mode.

Run from the repo root: python -m unittest discover -s build/tests
Requires the existing crank CLI; no agents, builds, or network access are used.
"""

import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
CRANK = shutil.which("crank")
PROFILES = {
    "containers": ("application", "db"),
    "grpc": ("application", "load"),
    "httpclient": ("server", "client"),
    "signalr": ("application", "warmup", "load"),
    "websocket": ("application", "load"),
}


class TestTemplateWiring(unittest.TestCase):
    def test_all_messages_enable_run_scoped_reuse(self):
        for suite in PROFILES:
            with self.subTest(suite=suite):
                text = (ROOT / "build" / f"{suite}-scenarios.yml").read_text(
                    encoding="utf-8-sig"
                )
                messages = [
                    line for line in text.splitlines() if '"args": [' in line
                ]
                self.assertTrue(messages)
                for message in messages:
                    self.assertIn("$(buildReuseProfile)", message)
                    self.assertIn(f"--profile {suite}-build-reuse", message)
                    self.assertIn(
                        "--variable buildCacheId=$(Build.BuildId)", message
                    )

    def test_grpc_scenarios_identify_both_build_variants(self):
        text = (ROOT / "build" / "grpc-scenarios.yml").read_text()
        scenarios = re.findall(
            r"arguments: --scenario (grpc\S+)([^\n]+)", text
        )
        self.assertEqual(len(scenarios), 7)
        for scenario, arguments in scenarios:
            with self.subTest(scenario=scenario):
                server, client = scenario.split("-")
                server_key = "aspnetcore" if "aspnetcore" in server else "go"
                client_key = {
                    "grpcnetclient": "dotnet",
                    "grpcgoclient": "go",
                    "h2loadclient": "h2load",
                }[client]
                self.assertIn(f"serverBuildKey={server_key}", arguments)
                self.assertIn(f"clientBuildKey={client_key}", arguments)

    def test_container_dockerfile_variants_have_distinct_tags(self):
        text = (ROOT / "scenarios" / "containers.benchmarks.yml").read_text()
        variants = re.findall(
            r"dockerFile: (docker/container-matrix/\S+)\n"
            r"\s+dockerImageName: (\S+)", text
        )
        self.assertEqual(len(variants), 7)
        tags = {}
        for dockerfile, tag in variants:
            self.assertEqual(tags.setdefault(tag, dockerfile), dockerfile)
        self.assertEqual(len(tags), 4)


@unittest.skipUnless(CRANK, "Requires the crank CLI")
class TestBuildReuseProfiles(unittest.TestCase):
    def configure(self, suite, run_id="123", variables=None, patches=None, error=None):
        roles = PROFILES[suite]
        if suite == "containers":
            roles = (*roles, "load")
        jobs = {
            role: {
                "source": {
                    "repository": "https://github.com/aspnet/Benchmarks.git",
                    "branchOrCommit": "main",
                    "project": f"src/{role}/App.csproj",
                },
                "framework": "net11.0",
            }
            for role in roles
        }
        if suite == "containers":
            for role in PROFILES[suite]:
                jobs[role]["source"] = {
                    "repository": "https://github.com/TechEmpower/FrameworkBenchmarks.git",
                    "branchOrCommit": "master",
                    "dockerFile": f"{role}/Dockerfile",
                    "dockerImageName": role,
                }
        if suite == "signalr":
            jobs["warmup"]["source"]["project"] = "src/load/App.csproj"
        for role, patch in (patches or {}).items():
            jobs[role].update(patch)

        scenario = {role: {"job": role} for role in roles}
        if suite == "httpclient":
            scenario["client"]["agent"] = "main"
            scenario["server"]["agent"] = "secondary"
        config = {
            "jobs": jobs,
            "profiles": {
                "machines": {
                    "agents": {
                        "application": {"endpoints": [], "aliases": ["main"]},
                        "load": {
                            "endpoints": [],
                            "aliases": ["warmup", "secondary"],
                        },
                        "db": {"endpoints": []},
                    }
                }
            },
            "scenarios": {"test": scenario},
        }
        values = {
            "compress": "false",
            "serverBuildKey": "aspnetcore",
            "clientBuildKey": "dotnet",
        }
        if run_id is not None:
            values["buildCacheId"] = run_id
        values.update(variables or {})
        with tempfile.TemporaryDirectory() as temp:
            source = Path(temp) / "scenario.json"
            output = Path(temp) / "debug.json"
            source.write_text(json.dumps(config), encoding="utf-8")
            command = [
                CRANK,
                "--config", str(source),
                "--config", str(ROOT / "build" / "build-reuse.profile.yml"),
                "--scenario", "test",
                "--profile", "machines",
                "--profile", f"{suite}-build-reuse",
                "--debug", str(output),
            ]
            for key, value in values.items():
                command.extend(("--variable", f"{key}={value}"))
            result = subprocess.run(
                command, capture_output=True, text=True, timeout=60
            )
            if run_id is None or not str(run_id).isdigit():
                error = "buildCacheId must be set"
            if error is not None:
                self.assertNotEqual(result.returncode, 0, result.stdout)
                self.assertIn(error, result.stdout + result.stderr)
                return None
            self.assertEqual(
                result.returncode, 0, result.stdout + result.stderr
            )
            return json.loads(output.read_text(encoding="utf-8"))["Jobs"]

    def assert_cached(self, jobs, suite):
        for role in PROFILES[suite]:
            with self.subTest(suite=suite, role=role):
                job = jobs[role]
                self.assertTrue(job["Options"]["ReuseBuild"])
                self.assertTrue(job["NoBuild"])
                self.assertTrue(job["BuildKey"].endswith("-123"))
                source_key = job["Source"]["SourceKey"]
                self.assertTrue(source_key.endswith("-123"))
                self.assertNotEqual(job["BuildKey"], source_key)

    def test_each_profile_invalidates_sources_and_builds_on_new_run(self):
        for suite in PROFILES:
            with self.subTest(suite=suite):
                first = self.configure(suite)
                self.assert_cached(first, suite)
                second = self.configure(suite, "124")
                for role in PROFILES[suite]:
                    self.assertNotEqual(
                        first[role]["BuildKey"], second[role]["BuildKey"]
                    )
                    self.assertNotEqual(
                        first[role]["Source"]["SourceKey"],
                        second[role]["Source"]["SourceKey"],
                    )

    def test_missing_or_invalid_run_id_is_rejected(self):
        for suite in PROFILES:
            for run_id in (None, "not-a-build"):
                with self.subTest(suite=suite, run_id=run_id):
                    self.configure(suite, run_id)

    def test_missing_or_invalid_build_variants_are_rejected(self):
        for key, value in (
            ("serverBuildKey", ""),
            ("clientBuildKey", "../invalid"),
        ):
            with self.subTest(key=key):
                self.configure(
                    "grpc",
                    variables={key: value},
                    error="serverBuildKey and clientBuildKey must identify",
                )
        self.configure(
            "websocket",
            variables={"compress": ""},
            error="compress must be true or false",
        )

    def test_container_runtime_limits_and_rate_do_not_change_cache(self):
        first = self.configure("containers", variables={"rate": "1000"})
        second = self.configure(
            "containers",
            variables={"rate": "10000"},
            patches={
                "application": {
                    "cpuLimitRatio": 0.25,
                    "memoryLimitInBytes": 256000000,
                    "cpuSet": "0",
                }
            },
        )
        for role in PROFILES["containers"]:
            self.assertEqual(first[role]["BuildKey"], second[role]["BuildKey"])
            self.assertEqual(
                first[role]["Source"]["SourceKey"],
                second[role]["Source"]["SourceKey"],
            )

    def test_container_image_variants_have_separate_caches(self):
        first = self.configure("containers")
        second = self.configure(
            "containers",
            patches={
                "application": {
                    "source": {
                        "repository": "https://github.com/aspnet/Benchmarks.git",
                        "branchOrCommit": "main",
                        "dockerFile": "docker/container-matrix/Middleware-alpine.dockerfile",
                        "dockerImageName": "app-alpine",
                    }
                }
            },
        )
        self.assertNotEqual(
            first["application"]["BuildKey"], second["application"]["BuildKey"]
        )
        self.assertNotEqual(
            first["application"]["Source"]["SourceKey"],
            second["application"]["Source"]["SourceKey"],
        )
        self.assertEqual(first["db"]["BuildKey"], second["db"]["BuildKey"])

    def test_websocket_compression_builds_are_separate(self):
        plain = self.configure("websocket")
        compressed = self.configure(
            "websocket",
            variables={"compress": "true"},
            patches={"application": {"buildArguments": ["/p:UseCompression=true"]}},
        )
        self.assertNotEqual(
            plain["application"]["BuildKey"],
            compressed["application"]["BuildKey"],
        )
        self.assertEqual(plain["load"]["BuildKey"], compressed["load"]["BuildKey"])

    def test_signalr_warmup_and_load_share_client_build(self):
        jobs = self.configure("signalr")
        self.assertEqual(jobs["warmup"]["BuildKey"], jobs["load"]["BuildKey"])
        self.assertNotEqual(jobs["application"]["BuildKey"], jobs["load"]["BuildKey"])

    def test_grpc_server_and_client_variants_are_separate(self):
        dotnet = self.configure("grpc")
        go = self.configure(
            "grpc", variables={"serverBuildKey": "go", "clientBuildKey": "go"}
        )
        h2load = self.configure("grpc", variables={"clientBuildKey": "h2load"})
        self.assertNotEqual(
            dotnet["application"]["BuildKey"], go["application"]["BuildKey"]
        )
        self.assertEqual(
            dotnet["application"]["BuildKey"], h2load["application"]["BuildKey"]
        )
        self.assertEqual(
            len({jobs["load"]["BuildKey"] for jobs in (dotnet, go, h2load)}), 3
        )

    def test_framework_versions_are_separate(self):
        first = self.configure("httpclient")
        second = self.configure(
            "httpclient",
            patches={"client": {"framework": "net10.0"}},
        )
        self.assertNotEqual(first["client"]["BuildKey"], second["client"]["BuildKey"])
        self.assertEqual(first["server"]["BuildKey"], second["server"]["BuildKey"])


if __name__ == "__main__":
    unittest.main()
