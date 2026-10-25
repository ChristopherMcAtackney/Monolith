"""Write the capture plans for the Jupiter Skirmish (gauntlet/shots/jupiter_r1_battle_*.json): the Dogfight's fight in front of Jupiter, the AI flying the player's ship.

Run:  python -I tools/jupiter/make_battle_plans.py [--times 6,12,...]  [--rig name ...]

A plan is the mission scene ("scene": "mission", mission "jupiter", HUD off for looks, no cockpit dust) with stills at absolute game times (the plan's warm-ups are the
differences between them). Each rig is one way of looking at the fight:
  cockpit        the player's own view (the game's camera, 55 degrees: follow shots need an explicit fov, the sky's warm-up leaves 140 in the first frames)
  wing           a wingman's shoulder: the camera rides Sable 2, behind and above, looking at the Warden
  mercy          the Small Mercy seen from off her bow, looking at the Warden
  warden         the Warden seen from beyond, looking back at the Mercy
  mercy_side     a wide shot from the side of the two capital ships
"""
import json
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SHOTS = os.path.join(ROOT, "gauntlet", "shots")

RIGS = {
    "cockpit": {"follow": True, "fov": 55},
    "wing": {"follow": True, "fov": 50, "spectate": {"ship": "Sable 2", "offset": [14, 7, -38], "world": False, "look": "Warden"}},
    "mercy": {"follow": True, "fov": 45, "spectate": {"ship": "Small Mercy", "offset": [-170, 90, 220], "world": True, "look": "Warden"}},
    "warden": {"follow": True, "fov": 45, "spectate": {"ship": "Warden", "offset": [120, 80, 420], "world": True, "look": "Small Mercy"}},
    # Jupiter behind the subject: the camera is back along the line from the planet through the ship (the planet's direction is (0.40, -0.16, 0.90) in the mission's frame), so the hull
    # the camera sees is the Sun-lit one (the Sun is 57 degrees from that line) and the planet fills the background
    "warden_jupiter": {"follow": True, "fov": 36, "spectate": {"ship": "Warden", "offset": [-250, 170, -560], "world": True, "look": "ship"}},
    "warden_jupiter_close": {"follow": True, "fov": 30, "spectate": {"ship": "Warden", "offset": [-120, 90, -300], "world": True, "look": "ship"}},
    "mercy_jupiter": {"follow": True, "fov": 40, "spectate": {"ship": "Small Mercy", "offset": [-230, 150, -520], "world": True, "look": "ship"}},
    "mercy_jupiter_close": {"follow": True, "fov": 36, "spectate": {"ship": "Small Mercy", "offset": [-160, 70, -330], "world": True, "look": "ship"}},
    "fighter_jupiter": {"follow": True, "fov": 40, "spectate": {"ship": "Choir 1", "offset": [-60, 30, -150], "world": True, "look": "ship"}},
    "mercy_side": {"follow": True, "fov": 50, "spectate": {"ship": "Small Mercy", "offset": [-900, 150, 1500], "world": True, "look": "Warden"}},
}

DEFAULT_TIMES = [6, 12, 18, 24, 30, 36, 42, 48, 54, 60, 70, 80, 90, 100, 110, 120]


def plan_for(rig_name, times, tag="", extra_options=None, width=1920, height=1080):
    rig = RIGS[rig_name]
    opts = {"mission": "jupiter", "pilot": "ai", "invulnerable": True, "hud": False, "dust": False}
    if "spectate" in rig:
        opts["spectate"] = rig["spectate"]
    if extra_options:
        opts.update(extra_options)
    stills = []
    prev = 0.0
    for t in times:
        stills.append({"name": f"{rig_name}_t{int(t):03d}{tag}", "follow": True, "fov": rig["fov"], "warmup": round(t - prev, 3)})
        prev = t
    return {"scene": "mission", "sceneOptions": opts, "defaults": {"width": width, "height": height, "warmup": 0.0, "perfWarmup": 3.0}, "stills": stills}


def main():
    times = DEFAULT_TIMES
    rigs = list(RIGS)
    if "--times" in sys.argv:
        times = [float(x) for x in sys.argv[sys.argv.index("--times") + 1].split(",")]
    if "--rig" in sys.argv:
        rigs = sys.argv[sys.argv.index("--rig") + 1].split(",")
    for r in rigs:
        path = os.path.join(SHOTS, f"jupiter_r1_battle_{r}.json")
        with open(path, "w") as f:
            json.dump(plan_for(r, times), f, indent=1)
        print("wrote", path)

    # the perf run: the battle as the player sees it, the stress option on (the first three waves and the bomber wing at once) is the dogfight scene's: this is the mission as it plays
    perf = {"scene": "mission", "sceneOptions": {"mission": "jupiter", "pilot": "ai", "invulnerable": True, "hud": True, "dust": True},
            "defaults": {"width": 1920, "height": 1080, "warmup": 0.0, "perfWarmup": 3.0},
            "perf": [{"name": "jupiter_battle_" + part, "seconds": 40, "follow": True, "fov": 55} for part in ("opening", "middle", "late")]}
    with open(os.path.join(SHOTS, "jupiter_r1_perf_battle.json"), "w") as f:
        json.dump(perf, f, indent=1)
    perf_off = json.loads(json.dumps(perf))
    perf_off["sceneOptions"]["mission"] = "dogfight"
    for part, run in zip(("opening", "middle", "late"), perf_off["perf"]):
        run["name"] = "dogfight_battle_" + part
    with open(os.path.join(SHOTS, "jupiter_r1_perf_dogfight.json"), "w") as f:
        json.dump(perf_off, f, indent=1)


if __name__ == "__main__":
    main()
