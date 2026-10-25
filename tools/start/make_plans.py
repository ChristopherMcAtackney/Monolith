"""Write the start screen's capture plans: gauntlet/shots/start_r1_<state>.json (one per cursor state; the HUD style comes from -mnHudStyle, as in the other HUD plans).

The states: the cursor on the first mission, the cursor in the difficulty list (Hard), on a sandbox (no difficulty), and on the quit row; and the first state again at 21:9, 4:3 and 5:4.

  python -I tools/start/make_plans.py
  powershell -File tools/capture.ps1 -Plan gauntlet/shots/start_r1_mission.json -Out gauntlet/captures/start/r1/halo -PlayerArgs "-mnHudStyle halo"
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

STATES = {
    'mission': {'scenario': 'dogfight', 'level': 'Medium'},
    'level': {'scenario': 'oort', 'level': 'Hard', 'focus': 'level'},
    'sandbox': {'scenario': 'range'},
    'quit': {'scenario': 'quit'},
}
ASPECTS = [('219', 2560, 1080), ('43', 1440, 1080), ('54', 1350, 1080)]


def plan(name, options):
    stills = [{'name': f'start_{name}', 'warmup': 0.5}]
    if name == 'mission':
        stills += [{'name': f'start_{name}_{a}', 'width': w, 'height': h, 'warmup': 0.25} for a, w, h in ASPECTS]
    return {
        'scene': 'start',
        'sceneOptions': dict(options, fade=1.0),
        'defaults': {'width': 1920, 'height': 1080, 'warmup': 0.5},
        'stills': stills,
    }


for name, opts in STATES.items():
    path = os.path.join(ROOT, 'gauntlet', 'shots', f'start_r1_{name}.json')
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(plan(name, opts), f, indent=1)
        f.write('\n')
    print('wrote', path)
