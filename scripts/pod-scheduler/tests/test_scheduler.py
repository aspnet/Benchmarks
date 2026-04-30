import unittest

import tests  # noqa: F401  # ensures sys.path is set up

from models import (
    PipelineSettings,
    Pod,
    Scenario,
    ScenarioType,
    Schedule,
    ScheduleConfig,
    Stage,
)
from scheduler import (
    SchedulerError,
    create_schedule,
    expand_runs,
    split_schedule,
)


def _config(pods, scenarios, queues=("q1", "q2")):
    return ScheduleConfig(
        name="t",
        schedule="0 3/12 * * *",
        queues=list(queues),
        target_yaml_count=1,
        schedule_offset_hours=6,
        pods={p.name: p for p in pods},
        scenarios=list(scenarios),
        pipeline=PipelineSettings(),
    )


def _pod(name, sut, load=None, db=None):
    return Pod(
        name=name, sut=sut, load=load, db=db,
        sut_profile=f"{sut}-app",
        load_profile=f"{load}-load" if load else None,
        db_profile=f"{db}-db" if db else None,
    )


def _scn(name, type_, pods, runtime=10):
    return Scenario(
        name=name, template=f"{name}.yml",
        type=type_, pods=list(pods), estimated_runtime=runtime,
    )


class TestExpandRuns(unittest.TestCase):
    def test_strict_raises_on_unknown_pod(self):
        cfg = _config(
            pods=[_pod("p1", "m1")],
            scenarios=[_scn("S", ScenarioType.SINGLE, ["missing"])],
        )
        with self.assertRaises(SchedulerError):
            expand_runs(cfg, strict=True)

    def test_lenient_skips_unknown_pod(self):
        cfg = _config(
            pods=[_pod("p1", "m1")],
            scenarios=[_scn("S", ScenarioType.SINGLE, ["missing", "p1"])],
        )
        runs = expand_runs(cfg, strict=False)
        self.assertEqual([r.pod.name for r in runs], ["p1"])

    def test_strict_raises_on_invalid_type(self):
        cfg = _config(
            pods=[_pod("p1", "m1")],  # no load
            scenarios=[_scn("S", ScenarioType.DUAL, ["p1"])],
        )
        with self.assertRaises(SchedulerError):
            expand_runs(cfg, strict=True)


class TestCreateSchedule(unittest.TestCase):
    def test_collisions_split_into_stages(self):
        cfg = _config(
            pods=[_pod("p1", "m1")],
            scenarios=[
                _scn("A", ScenarioType.SINGLE, ["p1"], runtime=5),
                _scn("B", ScenarioType.SINGLE, ["p1"], runtime=10),
            ],
        )
        sched = create_schedule(cfg)
        self.assertEqual(len(sched.stages), 2)

    def test_independent_pods_share_a_stage(self):
        cfg = _config(
            pods=[_pod("p1", "m1"), _pod("p2", "m2")],
            scenarios=[
                _scn("A", ScenarioType.SINGLE, ["p1"]),
                _scn("B", ScenarioType.SINGLE, ["p2"]),
            ],
        )
        sched = create_schedule(cfg)
        self.assertEqual(len(sched.stages), 1)
        self.assertEqual(len(sched.stages[0].runs), 2)

    def test_queue_count_limits_stage(self):
        cfg = _config(
            pods=[_pod(f"p{i}", f"m{i}") for i in range(3)],
            scenarios=[
                _scn(f"S{i}", ScenarioType.SINGLE, [f"p{i}"]) for i in range(3)
            ],
            queues=("q1", "q2"),
        )
        sched = create_schedule(cfg)
        self.assertEqual(len(sched.stages), 2)

    def test_zero_queues_raises(self):
        cfg = _config(
            pods=[_pod("p1", "m1")],
            scenarios=[_scn("S", ScenarioType.SINGLE, ["p1"])],
            queues=(),
        )
        with self.assertRaises(SchedulerError):
            create_schedule(cfg)

    def test_deterministic_output(self):
        cfg = _config(
            pods=[_pod("p1", "m1"), _pod("p2", "m2")],
            scenarios=[
                _scn("A", ScenarioType.SINGLE, ["p1"], runtime=10),
                _scn("B", ScenarioType.SINGLE, ["p2"], runtime=10),
            ],
        )
        s1 = create_schedule(cfg)
        s2 = create_schedule(cfg)
        names_of = lambda s: [
            [r.name for r in stage.runs] for stage in s.stages
        ]
        self.assertEqual(names_of(s1), names_of(s2))


class TestSplitSchedule(unittest.TestCase):
    def test_single_target_returns_input(self):
        sched = Schedule(stages=[Stage(runs=[])])
        self.assertEqual(split_schedule(sched, 1), [sched])

    def test_balances_durations(self):
        cfg = _config(
            pods=[_pod("p1", "m1")],
            scenarios=[
                _scn("A", ScenarioType.SINGLE, ["p1"], runtime=30),
                _scn("B", ScenarioType.SINGLE, ["p1"], runtime=20),
                _scn("C", ScenarioType.SINGLE, ["p1"], runtime=10),
            ],
        )
        sched = create_schedule(cfg)
        self.assertEqual(len(sched.stages), 3)
        bins = split_schedule(sched, 2)
        self.assertEqual(len(bins), 2)
        durations = sorted(b.total_duration for b in bins)
        self.assertEqual(durations, [30, 30])


if __name__ == "__main__":
    unittest.main()
