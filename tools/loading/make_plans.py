"""Write the loading screen's capture plans: gauntlet/shots/loading_r1_<style>.json (one per HUD style).

Each plan photographs the same moments of the same screen at 16:9 (start, middle, nearly done), and the middle moment again at 21:9, 4:3 and 5:4, so the splash's fit
and the bar can be judged on any shape of screen. The scene steps through "keys" by the capture clock (see LoadingTestWorld); each still's warm-up is when it is taken.

  python tools/loading/make_plans.py
  powershell -File tools/capture.ps1 -Plan gauntlet/shots/loading_r1_halo.json -Out gauntlet/captures/loading/r1
"""
import json
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
STYLES = ['halo', 'meridian', 'cipher']

# (clock seconds the moment begins, progress, status line, seconds of settle at that moment)
KEYS = [
    [0.0, 0.08, 'Starting up', 0.35],
    [1.5, 0.46, 'Loading sound', 4.0],
    [3.5, 0.93, 'Warming up the sky', 12.0],
]

# (name, width, height, warmup seconds: from the previous shot)
SHOTS = [
    ('start_169', 1920, 1080, 0.5),
    ('middle_169', 1920, 1080, 2.0),
    ('late_169', 1920, 1080, 2.0),
]
ASPECTS = [('middle_219', 2560, 1080), ('middle_43', 1440, 1080), ('middle_54', 1350, 1080)]


def plan(style):
    stills = [{'name': f'{style}_{n}', 'width': w, 'height': h, 'warmup': wu} for n, w, h, wu in SHOTS]
    # back to the middle moment for the other shapes of screen: a key at 5.5 s repeats it, then the shots follow
    keys = KEYS + [[5.5, 0.46, 'Loading sound', 10.0]]
    for n, w, h in ASPECTS:
        stills.append({'name': f'{style}_{n}', 'width': w, 'height': h, 'warmup': 1.5 if n == ASPECTS[0][0] else 0.25})
    return {
        'scene': 'loading',
        'sceneOptions': {'style': style, 'keys': keys},
        'defaults': {'width': 1920, 'height': 1080, 'warmup': 0.5},
        'stills': stills,
    }


for s in STYLES:
    path = os.path.join(ROOT, 'gauntlet', 'shots', f'loading_r1_{s}.json')
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(plan(s), f, indent=1)
        f.write('\n')
    print('wrote', path)
