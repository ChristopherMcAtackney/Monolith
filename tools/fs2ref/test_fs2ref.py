"""Self-checks for the FS2 flight reference tools.  Run: python -I tools/fs2ref/test_fs2ref.py

Tests that need the FS2 install skip themselves if Root_fs2.vp is not found.
"""
from __future__ import annotations

import math
import os
import sys
import tempfile
import unittest
from dataclasses import replace

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # python -I does not add the script folder

import compare as compare_mod  # noqa: E402
import make_reference  # noqa: E402
import ships  # noqa: E402
from fs2_flight import Controls, FlightSim, apply_physics, simulate, velocity_ramp  # noqa: E402
from metrics import allowed_error, compute_metrics, read_trace  # noqa: E402
from scenarios import Scenario, load_scenarios  # noqa: E402

HAVE_VP = os.path.isfile(ships.vp_path())
SCENARIOS = make_reference.DEFAULT_SCENARIOS


def tbl_ship(**over) -> ships.ShipFlight:
    base = ships.ShipFlight(
        name="Test", max_vel=(0.0, 0.0, 70.0), max_rear_vel=0.0, rotation_time=(3.0, 2.6, 5.0),
        damp=0.1, rotdamp=0.35, forward_accel=2.0, forward_decel=1.5, slide_accel=0.0, slide_decel=0.0,
        has_afterburner=True, aburn_max_vel=(0.0, 0.0, 150.0), aburn_forward_accel=0.7, aburn_fuel=300.0,
        aburn_burn_rate=50.0, aburn_recover_rate=25.0, max_overclocked_speed=94.0,
    )
    return replace(base, **over)


def run(ship, keys, duration, start_speed_fraction=0.0, start_throttle=0.0, dt=1 / 60):
    sc = Scenario("t", "", duration, start_speed_fraction, start_throttle, sorted(keys, key=lambda k: k["t"]))
    return simulate(ship, sc, dt=dt)


class Primitives(unittest.TestCase):
    def test_apply_physics_matches_fine_integration(self):
        v, x, dt, n = 10.0, 0.0, 1.0 / 100000, 100000
        for _ in range(n):
            x += v * dt
            v += (50.0 - v) / 0.35 * dt
        new_v, disp = apply_physics(0.35, 50.0, 10.0, 1.0)
        self.assertAlmostEqual(new_v, v, places=3)
        self.assertAlmostEqual(disp, x, places=2)

    def test_zero_damping_jumps_to_goal(self):
        self.assertEqual(apply_physics(0.0, 7.0, 1.0, 0.5), (7.0, 3.5))

    def test_velocity_ramp_is_exponential_then_snaps(self):
        v = velocity_ramp(0.0, 70.0, 2.0, 1.0)
        self.assertAlmostEqual(v, 70.0 * (1 - math.exp(-0.5)), places=9)
        v, ticks = 0.0, 0
        while v != 70.0 and ticks < 60 * 60:
            v = velocity_ramp(v, 70.0, 2.0, 1 / 60)
            ticks += 1
        self.assertEqual(v, 70.0)                      # the shortcut makes it arrive exactly, in finite time
        self.assertLess(ticks, 60 * 25)                # a pure exponential would still be creeping at 25 s

    def test_zero_dt_changes_nothing(self):
        self.assertEqual(velocity_ramp(3.0, 70.0, 2.0, 0.0), 3.0)


class Rules(unittest.TestCase):
    def test_pitch_down_positive_and_angle_is_integral_of_rate(self):
        rows = run(tbl_ship(), [{"t": 0.0, "pitch": 1.0}], 0.6)
        last = rows[-1]
        self.assertLess(last["fwd_y"], 0.0)                                     # +1 pitch = nose down
        self.assertAlmostEqual(math.sqrt(last["fwd_x"] ** 2 + last["fwd_y"] ** 2 + last["fwd_z"] ** 2), 1.0, places=9)
        angle = sum(r["wx"] for r in rows[1:]) / 60.0                           # rotation uses each tick's NEW rate
        self.assertAlmostEqual(math.atan2(-last["fwd_y"], last["fwd_z"]), angle, places=6)

    def test_yaw_right_banks_right_at_half_the_yaw_rate(self):
        rows = run(tbl_ship(), [{"t": 0.0, "yaw": 1.0}], 4.0)
        last = rows[-1]
        self.assertGreater(last["wy"], 0.0)
        self.assertAlmostEqual(last["wz"] / last["wy"], -0.5, places=9)
        mid = rows[60]                                                          # 1 s in: banked into the turn
        self.assertGreater(mid["up_x"], 0.0)

    def test_roll_left_positive(self):
        rows = run(tbl_ship(), [{"t": 0.0, "roll": 1.0}], 0.5)
        self.assertLess(rows[-1]["up_x"], 0.0)                                  # left wing down: up vector leans left

    def test_forward_speed_never_reverses_and_follows_throttle(self):
        rows = run(tbl_ship(), [{"t": 0.0, "throttle": 0.5}], 20.0)
        self.assertAlmostEqual(rows[-1]["vz"], 35.0, places=3)
        self.assertGreaterEqual(min(r["vz"] for r in rows), 0.0)

    def test_velocity_swings_round_to_the_nose(self):
        rows = run(tbl_ship(), [{"t": 0.0, "throttle": 1.0}, {"t": 0.5, "yaw": 1.0}, {"t": 1.0, "yaw": 0.0}],
                   6.0, start_speed_fraction=1.0, start_throttle=1.0)
        peak = max(abs(r["vx"]) for r in rows)
        self.assertGreater(peak, 5.0)                                           # it does slip sideways in the turn
        self.assertLess(abs(rows[-1]["vx"]), 0.01)                              # and then lines up again
        self.assertAlmostEqual(rows[-1]["speed"], 70.0, places=2)

    def test_afterburner_lockout_and_empty(self):
        ship = tbl_ship()
        # press at 1.0, release 1.5, press again at 2.0 (only 1.0 s after the first start): refused.
        keys = [{"t": 1.0, "afterburner": True}, {"t": 1.5, "afterburner": False},
                {"t": 2.0, "afterburner": True}, {"t": 2.5, "afterburner": False},
                {"t": 3.0, "afterburner": True}]
        sim = FlightSim(ship, start_speed=70.0, start_throttle=1.0)
        sc = Scenario("t", "", 4.0, 1.0, 1.0, keys)
        flags = {}
        for i in range(int(4.0 * 60)):
            t = i / 60
            sim.step(sc.controls_at(t))
            flags[round(t, 3)] = sim.aburn_on
        self.assertTrue(flags[1.2])
        self.assertFalse(flags[2.2])        # second press 1.0 s after the first start: inside the 1.3 s lockout
        self.assertTrue(flags[3.2])         # third press 2.0 s after the first start: allowed

    def test_afterburner_runs_dry_with_key_held_and_falls_back(self):
        rows = run(tbl_ship(), [{"t": 1.0, "afterburner": True}], 30.0, 1.0, 1.0)
        empty_at = next(r["time"] for r in rows if r["fuel"] <= 0.0)
        self.assertAlmostEqual(empty_at - 1.0, 6.0, delta=0.05)                 # 300 fuel at 50/s
        after = [r for r in rows if r["time"] > empty_at + 12.0]
        self.assertLess(max(r["vz"] for r in after), 70.01)                     # key still held, but no relight: normal top speed
        self.assertAlmostEqual(rows[-1]["fuel"], 300.0, places=3)               # and the tank refills while the key is held

    def test_slide_ships_are_refused(self):
        with self.assertRaises(NotImplementedError):
            FlightSim(tbl_ship(max_vel=(10.0, 10.0, 70.0)))


class TableParsing(unittest.TestCase):
    def test_comments_and_first_definition(self):
        text = """#Ship Classes
$Name:   Alpha ;; a comment
$Damp:   0.1
$Rotdamp: 0.35
$Max Velocity: 0.0, 0.0, 70.0   ;; z only
$Rotation time: 3.0, 2.6, 5
$Rear Velocity: 0.0
$Forward accel: 2.0
$Forward decel: 1.5
$Slide accel: 0.0
$Slide decel: 0.0
$Power Output: 2.0
$Max Oclk Speed: 94.0
$Afterburner: NO
$Name:   Alpha
$Damp:   9.9
#End
"""
        s = ships._ship_from_fields("Alpha", ships.parse_ships_tbl(text)["Alpha"])
        self.assertEqual((s.damp, s.max_speed, s.rotation_time, s.has_afterburner), (0.1, 70.0, (3.0, 2.6, 5.0), False))


@unittest.skipUnless(HAVE_VP, "FS2 install not found")
class RealData(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.ul = ships.get_ship("GTF Ulysses")
        cls.scenarios = {s.name: s for s in load_scenarios(SCENARIOS)}

    def test_ulysses_matches_the_design_appendix(self):
        u = self.ul
        self.assertEqual(u.max_speed, 70.0)
        self.assertEqual(u.rotation_time[0], 3.0)                               # 3.0 s per pitch revolution
        self.assertAlmostEqual(u.max_rotvel[0], 2 * math.pi / 3.0)
        self.assertEqual((u.forward_accel, u.forward_decel, u.rotdamp, u.damp), (2.0, 1.5, 0.35, 0.1))
        self.assertEqual((u.aburn_max_vel[2], u.aburn_forward_accel, u.aburn_fuel, u.aburn_burn_rate, u.aburn_recover_rate),
                         (150.0, 0.7, 300.0, 50.0, 25.0))

    def test_all_reference_scenarios_load_and_measure(self):
        for name, sc in self.scenarios.items():
            rows = simulate(self.ul, sc)
            trace = {k: [r[k] for r in rows] for k in rows[0]}
            m = compute_metrics(trace, sc.measure)
            self.assertTrue(all(v is not None for v in m.values()), (name, m))

    def test_tick_rate_independence(self):
        """Every metric but slide_peak is within tolerance at 30..240 Hz of the 60 Hz result (slide_peak: 10%)."""
        for hz in (30, 50, 120, 240):
            for name, sc in self.scenarios.items():
                ref = compute_metrics(self._trace(sc, 60), sc.measure)
                ours = compute_metrics(self._trace(sc, hz), sc.measure)
                for k, rv in ref.items():
                    self.assertLessEqual(abs(ours[k] - rv), allowed_error(k, rv), (hz, name, k, rv, ours[k]))

    def _trace(self, sc, hz):
        rows = simulate(self.ul, sc, dt=1.0 / hz)
        return {k: [r[k] for r in rows] for k in rows[0]}

    def test_compare_self_passes_and_other_ship_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_reference.build(["GTF Ulysses", "GTF Myrmidon"], SCENARIOS, tmp, 60.0, 1, None, quiet=True)
            uly = os.path.join(tmp, "GTF_Ulysses")
            myr = os.path.join(tmp, "GTF_Myrmidon")
            rows, _ = compare_mod.compare(uly, uly)
            self.assertTrue(rows and all(r["status"] == "PASS" for r in rows))
            rows, _ = compare_mod.compare(myr, uly)
            failed = {r["scenario"] for r in rows if r["status"] == "FAIL"}
            self.assertTrue({"throttle_up", "pitch_pulse", "yaw_pulse", "afterburner_burst"} <= failed, failed)
            rows, _ = compare_mod.compare(os.path.join(tmp, "nope"), uly)         # missing traces fail loudly
            self.assertTrue(all(r["status"] == "FAIL" for r in rows))

    def test_reference_on_disk_is_current(self):
        """The committed Ulysses reference equals a fresh run (the tools and the data have not drifted apart)."""
        ref_dir = os.path.join(make_reference.DEFAULT_OUT, "GTF_Ulysses")
        if not os.path.isdir(ref_dir):
            self.skipTest("no committed reference")
        with tempfile.TemporaryDirectory() as tmp:
            make_reference.build(["GTF Ulysses"], SCENARIOS, tmp, 60.0, 1, None, quiet=True)
            for name in self.scenarios:
                a = read_trace(os.path.join(tmp, "GTF_Ulysses", name, "trace.csv"))
                b = read_trace(os.path.join(ref_dir, name, "trace.csv"))
                self.assertEqual(a, b, name)


if __name__ == "__main__":
    unittest.main(verbosity=1)
