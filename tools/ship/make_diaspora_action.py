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


if __name__ == "__main__":
    plan("wingman", "Sable 2", [9.0, 3.5, -30.0], "target", 52, 12.0, 10, 2.0)
    plan("wing3", "Sable 3", [-8.0, 3.0, -24.0], "target", 55, 12.0, 10, 2.0)
    plan("mercy", "Small Mercy", [160.0, 70.0, -230.0], "ship", 46, 60.0, 10, 8.0)
    plan("mercy_bombers", "Small Mercy", [-120.0, 40.0, 220.0], "ship", 50, 100.0, 10, 8.0)
