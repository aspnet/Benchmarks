import unittest

import tests  # noqa: F401  # ensures sys.path is set up

from models import (
    JOB_ID_RE,
    Pod,
    Run,
    SCENARIO_TYPE_DUAL,
    SCENARIO_TYPE_SINGLE,
    SCENARIO_TYPE_TRIPLE,
    Scenario,
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
    def _pod(self, machines=("sut",), profiles=("sut-app",)):
        return Pod(name="p", machines=list(machines), profiles=list(profiles))

    def test_single_only_pod_rejects_dual(self):
        pod = self._pod()
        self.assertIsNone(pod.validate(SCENARIO_TYPE_SINGLE))
        self.assertIsNotNone(pod.validate(SCENARIO_TYPE_DUAL))
        self.assertIsNotNone(pod.validate(SCENARIO_TYPE_TRIPLE))

    def test_dual_pod_rejects_triple(self):
        pod = self._pod(["sut", "load"], ["sut-app", "load-load"])
        self.assertIsNone(pod.validate(SCENARIO_TYPE_DUAL))
        self.assertIsNotNone(pod.validate(SCENARIO_TYPE_TRIPLE))

    def test_triple_pod_accepts_all(self):
        pod = self._pod(
            ["sut", "load", "db"],
            ["sut-app", "load-load", "db-db"],
        )
        for t in (SCENARIO_TYPE_SINGLE, SCENARIO_TYPE_DUAL, SCENARIO_TYPE_TRIPLE):
            self.assertIsNone(pod.validate(t), t)

    def test_machines_profiles_length_mismatch_rejected(self):
        with self.assertRaises(ValueError):
            Pod(name="p", machines=["a", "b"], profiles=["a-app"])

    def test_empty_machines_rejected(self):
        with self.assertRaises(ValueError):
            Pod(name="p", machines=[], profiles=[])


class TestStageCanAdd(unittest.TestCase):
    def _run(self, name, sut, load=None, db=None, runtime=10):
        machines = [sut]
        profiles = [f"{sut}-app"]
        if load:
            machines.append(load)
            profiles.append(f"{load}-load")
        if db:
            machines.append(db)
            profiles.append(f"{db}-db")
        scenario = Scenario(
            name=name,
            template=f"{name}.yml",
            type=len(machines),
            pods=["p"],
            estimated_runtime=runtime,
        )
        pod = Pod(name="p", machines=machines, profiles=profiles)
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
