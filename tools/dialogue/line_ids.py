"""Line IDs for the dialogue that has no id of its own: the radio and callOut actions that carry their text inline (Dead Water's and The Oculus' call-outs).

A message has an id in its mission file (mission:message_id) and a radio.json variant has radio:category:n. A call-out is an action inside an event, so its Line ID is made from the
event's name: mission:callout:event-name (a radio action with its own text: mission:inline:event-name), with :2, :3 for a second one in the same event. Renaming an event renames its
call-out's ID, which is the one thing to watch; export_sheet.py, import_sheet.py and audio_src/voice/collect_lines.py all use this one function, so they always agree.
"""
import re


def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", (s or "").lower()).strip("-") or "event"


def inline_lines(mission):
    """[(line_id, path, action)] for every radio or callOut action with its own text, in file order. `path` is the action's path in the parsed mission (('events', 3, 'do', 0))."""
    mid = mission.get("id") or "mission"
    seen = {}
    out = []

    def walk(o, path, event):
        if isinstance(o, dict):
            if o.get("do") in ("radio", "callOut") and isinstance(o.get("text"), str):
                kind = "callout" if o["do"] == "callOut" else "inline"
                base = f"{mid}:{kind}:{slug(event)}"
                seen[base] = seen.get(base, 0) + 1
                out.append((base if seen[base] == 1 else f"{base}:{seen[base]}", path, o))
            for k, v in o.items():
                walk(v, path + (k,), event)
        elif isinstance(o, list):
            for i, x in enumerate(o):
                walk(x, path + (i,), event)

    for i, ev in enumerate(mission.get("events", [])):
        walk(ev, ("events", i), ev.get("name") or f"event{i + 1}")
    return out
