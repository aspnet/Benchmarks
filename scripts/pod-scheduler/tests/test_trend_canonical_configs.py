import os
import re
import unittest

import tests  # noqa: F401  # ensures sys.path is set up


_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.abspath(os.path.join(_HERE, "..", "..", ".."))
_BUILD = os.path.join(_REPO, "build")
_TREND_CONFIGS = (
    "scenarios/platform.benchmarks.yml",
    "scenarios/plaintext.benchmarks.yml",
    "scenarios/json.benchmarks.yml",
    "scenarios/antiforgery.benchmarks.yml",
    "scenarios/tls.benchmarks.yml",
    "scenarios/rejection.benchmarks.yml",
    "scenarios/database.benchmarks.yml",
    "src/BenchmarksApps/TechEmpower/Minimal/minimal.benchmarks.yml",
    "src/BenchmarksApps/TechEmpower/BlazorSSR/blazorssr.benchmarks.yml",
    "src/BenchmarksApps/TechEmpower/RazorPages/razorpages.benchmarks.yml",
)
_SHARED_CONFIGS = (
    "build/ci.profile.yml",
    "scenarios/aspnet.profiles.yml",
    "scenarios/aspnet.profiles.standard.yml",
)
_PACKAGES_IMPORT = (
    "https://raw.githubusercontent.com/aspnet/Benchmarks/"
    "main/scenarios/packages.yml"
)
_SHARED_PROFILE_IMPORT_RE = re.compile(
    r"https://raw\.githubusercontent\.com/aspnet/Benchmarks/"
    r"main/scenarios/aspnet\.profiles(?:\.standard)?\.yml",
    re.IGNORECASE,
)
_BENCHMARKS_REPOSITORY_RE = re.compile(
    r"repository:\s*https://github\.com/aspnet/benchmarks(?:\.git)?",
    re.IGNORECASE,
)
_PINNED_BENCHMARKS_SOURCE_RE = re.compile(
    r"repository:\s*https://github\.com/aspnet/benchmarks(?:\.git)?\s*"
    r"\n\s*branchOrCommit:\s*[\"']?\{\{benchmarksCommit\}\}[\"']?",
    re.IGNORECASE,
)
_FLOATING_BENCHMARKS_RAW_RE = re.compile(
    r"https://raw\.githubusercontent\.com/aspnet/Benchmarks/"
    r"(?:refs/heads/)?main/(?P<path>[^\s]+)",
    re.IGNORECASE,
)
_PINNED_BENCHMARKS_RAW_RE = re.compile(
    r"https://raw\.githubusercontent\.com/aspnet/Benchmarks/"
    r"\{\{benchmarksCommit\}\}/",
    re.IGNORECASE,
)


def _read_repo(path):
    with open(
        os.path.join(_REPO, path.replace("/", os.sep)),
        encoding="utf-8-sig",
    ) as file:
        return file.read()


class TestTrendCanonicalConfigs(unittest.TestCase):
    def test_shared_benchmarks_commit_defaults_to_main(self):
        packages = _read_repo("scenarios/packages.yml")
        self.assertRegex(
            packages,
            r"(?m)^variables:\s*\n\s+benchmarksCommit:\s+main$",
        )
        for config in _SHARED_CONFIGS:
            with self.subTest(config=config):
                self.assertIn(_PACKAGES_IMPORT, _read_repo(config))

    def test_templates_reference_existing_canonical_configs(self):
        roots = {"build/ci.profile.yml"}
        for template in (
            "trend-scenarios.yml",
            "trend-database-scenarios.yml",
        ):
            roots.update(re.findall(
                r"\$\{\{ parameters\.benchmarksRawBaseUrl \}\}/"
                r"([^\s\"\\]+\.ya?ml)",
                _read_repo(f"build/{template}"),
            ))

        for root in roots:
            with self.subTest(root=root):
                self.assertNotIn("trend-configs", root)
                self.assertTrue(
                    os.path.isfile(
                        os.path.join(_REPO, root.replace("/", os.sep))
                    ),
                    root,
                )

        self.assertFalse(
            os.path.exists(os.path.join(_BUILD, "trend-configs"))
        )

    def test_workload_sources_and_assets_use_benchmarks_commit(self):
        source_count = 0
        asset_count = 0
        for config in _TREND_CONFIGS:
            with self.subTest(config=config):
                text = _read_repo(config)
                self.assertRegex(text, _SHARED_PROFILE_IMPORT_RE)
                repositories = _BENCHMARKS_REPOSITORY_RE.findall(text)
                pinned_sources = _PINNED_BENCHMARKS_SOURCE_RE.findall(text)
                self.assertEqual(
                    len(repositories),
                    len(pinned_sources),
                    config,
                )
                source_count += len(pinned_sources)

                for match in _FLOATING_BENCHMARKS_RAW_RE.finditer(text):
                    self.assertRegex(
                        match.group("path"),
                        r"^scenarios/aspnet\.profiles"
                        r"(?:\.standard)?\.yml$",
                    )

                pinned_assets = _PINNED_BENCHMARKS_RAW_RE.findall(text)
                asset_count += len(pinned_assets)

        self.assertGreater(source_count, 0)
        self.assertGreater(asset_count, 0)


if __name__ == "__main__":
    unittest.main()
