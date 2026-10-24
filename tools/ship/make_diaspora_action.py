"""Writes the in-mission capture plans for the diaspora's ships: the dogfight mission with the AI flying the player's ship (invulnerable, HUD off, no cockpit dust) and a
spectator camera riding a ship (the capture-only DogfightSpectator): a Sable wingman fighting its way through the drones, and the Small Mercy under attack.

    python tools/ship/make_diaspora_action.py      -> gauntlet/shots/diaspora_action_<name>.json
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def plan(name, ship, offset, look, fov, t0, n, step, world=False, mission="dogfight"):
    stills = [{"name": f"{name}_{i:02d}", "follow": True, "fov": fov, "warmup": (t0 if i == 0 else step)} for i in range(n)]
    spectate = {"ship": ship, "offset": offset, "look": look}
    if world:
        spectate["world"] = True
    p = {"scene": "mission", "sceneOptions": {"mission": mission, "pilot": "ai", "invulnerable": True, "hud": False, "dust": False, "spectate": spectate},
         "defaults": {"width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 3.0}, "stills": stills, "perf": []}
    path = os.path.join(ROOT, "gauntlet", "shots", f"diaspora_action_{name}.json")
    with open(path, "w") as f:
        json.dump(p, f, indent=1)
    print("wrote", path)


def mercy_death():
    """The Mercy's staged death: a copy of the dogfight with her at 2 percent hull (gauntlet/missions/mercy_death_test.mission.json), a fixed camera (the spectator
    camera hands back to the cockpit when its ship dies), 28 stills from the first beam hit."""
    mission = os.path.join(ROOT, "gauntlet", "missions", "mercy_death_test.mission.json").replace("\\", "/")
    stills = [{"name": f"death_{i:02d}", "pos": [330.0, 70.0, -380.0], "lookAt": [0.0, -70.0, 40.0], "fov": 46, "warmup": (3.0 if i == 0 else 0.45)} for i in range(28)]
    p = {"scene": "mission", "sceneOptions": {"mission": mission, "pilot": "ai", "invulnerable": True, "hud": False, "dust": False},
         "defaults": {"width": 1920, "height": 1080, "warmup": 1.0, "perfWarmup": 3.0}, "stills": stills, "perf": []}
    path = os.path.join(ROOT, "gauntlet", "shots", "diaspora_mercy_death.json")
    with open(path, "w") as f:
        json.dump(p, f, indent=1)
    print("wrote", path)


if __name__ == "__main__":
    plan("wingman", "Sable 2", [9.0, 3.5, -30.0], "target", 52, 12.0, 10, 2.0)
    plan("wing3", "Sable 3", [-8.0, 3.0, -24.0], "target", 55, 12.0, 10, 2.0)
    plan("mercy", "Small Mercy", [160.0, 70.0, -230.0], "ship", 46, 60.0, 10, 8.0)
    plan("mercy_bombers", "Small Mercy", [-120.0, 40.0, 220.0], "ship", 50, 100.0, 10, 8.0)
    mercy_death()
