"""Load gauntlet/shots/flight_scenarios.json (format documented in that file and in README.md)."""
from __future__ import annotations

import json
from dataclasses import dataclass, field

from fs2_flight import Controls

FORMAT = "monolith-flight-scenarios/1"
_INPUT_FIELDS = ("pitch", "yaw", "roll", "throttle", "afterburner")


@dataclass
class Scenario:
    name: str
    description: str
    duration: float
    start_speed_fraction: float
    start_throttle: float
    keys: list[dict]                    # sorted by "t"; each holds from its time until the next key
    measure: list[str] = field(default_factory=list)

    def controls_at(self, t: float) -> Controls:
        """Inputs in force at time t: the latest key at or before t (step hold), later keys inherit earlier values."""
        ctl = Controls(throttle=self.start_throttle)
        for key in self.keys:
            if key["t"] > t + 1e-9:
                break
            for f in _INPUT_FIELDS:
                if f in key:
                    setattr(ctl, f, bool(key[f]) if f == "afterburner" else float(key[f]))
        return ctl


def parse_scenarios(doc: dict) -> list[Scenario]:
    if doc.get("format") != FORMAT:
        raise ValueError(f"unsupported scenario file format {doc.get('format')!r}, expected {FORMAT!r}")
    out = []
    for raw in doc["scenarios"]:
        init = raw.get("initial", {})
        keys = sorted(raw.get("keys", []), key=lambda k: k["t"])
        for k in keys:
            unknown = set(k) - set(_INPUT_FIELDS) - {"t"}
            if unknown:
                raise ValueError(f"scenario {raw['name']}: unknown key fields {sorted(unknown)}")
        out.append(
            Scenario(
                name=raw["name"],
                description=raw.get("description", ""),
                duration=float(raw["duration"]),
                start_speed_fraction=float(init.get("speed_fraction", 0.0)),
                start_throttle=float(init.get("throttle", 0.0)),
                keys=keys,
                measure=list(raw.get("measure", [])),
            )
        )
    names = [s.name for s in out]
    if len(set(names)) != len(names):
        raise ValueError("duplicate scenario names")
    return out


def load_scenarios(path: str) -> list[Scenario]:
    with open(path, encoding="utf-8") as f:
        return parse_scenarios(json.load(f))
