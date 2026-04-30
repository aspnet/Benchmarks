import unittest

import tests  # noqa: F401  # ensures sys.path is set up

from generator import GeneratorError, _job_timeout, _offset_cron
from models import Pod, Run, Scenario, ScenarioType


class TestOffsetCron(unittest.TestCase):
    def test_offset_h_field(self):
        self.assertEqual(_offset_cron("0 3 * * *", 6), "0 9 * * *")

    def test_offset_h_slash_n(self):
        self.assertEqual(_offset_cron("0 3/12 * * *", 6), "0 9/12 * * *")

    def test_offset_wraps_at_24(self):
        self.assertEqual(_offset_cron("0 22 * * *", 6), "0 4 * * *")

    def test_offset_zero_is_identity(self):
        self.assertEqual(_offset_cron("0 3/12 * * *", 0), "0 3/12 * * *")

    def test_unsupported_hour_field_raises(self):
        for cron in [
            "0 * * * *",
            "0 0,12 * * *",
            "0 1-5 * * *",
            "0 */6 * * *",
        ]:
            with self.assertRaises(GeneratorError, msg=cron):
                _offset_cron(cron, 6)

    def test_wrong_field_count_raises(self):
        with self.assertRaises(GeneratorError):
            _offset_cron("0 3 * *", 6)


class TestJobTimeout(unittest.TestCase):
    def _run(self, runtime, timeout=None):
        scenario = Scenario(
            name="s", template="s.yml", type=ScenarioType.SINGLE,
            pods=["p"], estimated_runtime=runtime, timeout=timeout,
        )
        pod = Pod(name="p", sut="sut", sut_profile="sut")
        return Run(scenario=scenario, pod=pod, estimated_runtime=runtime)

    def test_uses_explicit_timeout(self):
        self.assertEqual(_job_timeout(self._run(30, timeout=42)), 42)

    def test_short_runtime_floors_at_120(self):
        self.assertEqual(_job_timeout(self._run(5)), 120)

    def test_long_runtime_caps_at_240(self):
        self.assertEqual(_job_timeout(self._run(200)), 240)

    def test_mid_runtime_doubles(self):
        self.assertEqual(_job_timeout(self._run(90)), 180)


if __name__ == "__main__":
    unittest.main()
