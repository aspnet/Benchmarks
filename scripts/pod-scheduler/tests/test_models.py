import unittest

import tests  # noqa: F401  # ensures sys.path is set up

from models import (
    JOB_ID_RE,
    Pod,
    Run,
    Scenario,
    ScenarioType,
    Stage,
    sanitize_job_id,
)


class TestSanitizeJobId(unittest.TestCase):
    def test_replaces_spaces_and_hyphens(self):
        self.assertEqual(sanitize_job_id("Proxies gold-lin"), "Proxies_gold_lin")

    def test_collapses_multiple_separators(self):
        self.assertEqual(
            sanitize_job_id("Foo  --bar..baz"), "Foo_bar_baz"
        )

    def test_prefixes_leading_digit(self):
        self.assertEqual(sanitize_job_id("01-stage"), "_01_stage")

    def test_handles_unicode_and_punctuation(self):
        result = sanitize_job_id("Frënch (test)/v2")
        self.assertRegex(result, JOB_ID_RE)
        self.assertNotIn(" ", result)
        self.assertNotIn("/", result)
        self.assertNotIn("(", result)

    def test_truncates_to_100(self):
        long_name = "a" * 250
        result = sanitize_job_id(long_name)
        self.assertEqual(len(result), 100)

    def test_result_always_matches_pattern(self):
        for sample in ["x.y", "1abc", " hello ", "---", "a/b/c"]:
            self.assertRegex(sanitize_job_id(sample), JOB_ID_RE, sample)


class TestPodValidation(unittest.TestCase):
    def _pod(self, **kwargs):
        defaults = dict(
            name="p", sut="sut",
            sut_profile="sut-app",
        )
        defaults.update(kwargs)
        return Pod(**defaults)

    def test_single_only_pod_rejects_dual(self):
        pod = self._pod()
        self.assertIsNone(pod.validate(ScenarioType.SINGLE))
        self.assertIsNotNone(pod.validate(ScenarioType.DUAL))
        self.assertIsNotNone(pod.validate(ScenarioType.TRIPLE))

    def test_dual_pod_rejects_triple(self):
        pod = self._pod(load="load", load_profile="load-load")
        self.assertIsNone(pod.validate(ScenarioType.DUAL))
        self.assertIsNotNone(pod.validate(ScenarioType.TRIPLE))

    def test_triple_pod_accepts_all(self):
        pod = self._pod(
            load="load", load_profile="load-load",
            db="db", db_profile="db-db",
        )
        for t in ScenarioType:
            self.assertIsNone(pod.validate(t), t)


class TestStageCanAdd(unittest.TestCase):
    def _run(self, name, sut, load=None, db=None, runtime=10):
        scenario = Scenario(
            name=name,
            template=f"{name}.yml",
            type=ScenarioType.TRIPLE if db else (
                ScenarioType.DUAL if load else ScenarioType.SINGLE
            ),
            pods=["p"],
            estimated_runtime=runtime,
        )
        pod = Pod(
            name="p", sut=sut, load=load, db=db,
            sut_profile="sut", load_profile="load", db_profile="db",
        )
        return Run(scenario=scenario, pod=pod, estimated_runtime=runtime)

    def test_collision_blocks(self):
        stage = Stage(runs=[self._run("a", sut="m1")])
        self.assertFalse(stage.can_add(self._run("b", sut="m1"), 10))

    def test_no_collision_allows(self):
        stage = Stage(runs=[self._run("a", sut="m1")])
        self.assertTrue(stage.can_add(self._run("b", sut="m2"), 10))

    def test_queue_limit_enforced(self):
        stage = Stage(runs=[self._run(f"a{i}", sut=f"m{i}") for i in range(3)])
        self.assertFalse(stage.can_add(self._run("a3", sut="m3"), queue_count=3))
        self.assertTrue(stage.can_add(self._run("a3", sut="m3"), queue_count=4))


if __name__ == "__main__":
    unittest.main()
