"""Read the flight fields of one ship class out of FS2's ships.tbl.

The table lives inside Root_fs2.vp (a Volition archive). It is read in place: the archive
is opened read-only, its directory is walked to find data/tables/ships.tbl, and only that
byte range is read. Nothing is extracted or modified.

VP layout: "VPVP", int version, int directory offset, int entry count. The directory is a
list of 44-byte entries (int offset, int size, char[32] name, int timestamp). An entry with
size 0 and no extension is a directory; the name ".." leaves one.

Run with `python -I`; this module has no third-party imports.
"""
from __future__ import annotations

import hashlib
import math
import os
import re
import struct
from dataclasses import asdict, dataclass

DEFAULT_VP = r"C:\Program Files (x86)\Steam\steamapps\common\Freespace 2\Root_fs2.vp"
SHIPS_TBL_PATH = "data/tables/ships.tbl"

# Classes this project keeps references for.
REFERENCE_CLASSES = ("GTF Ulysses", "GTF Myrmidon", "GTF Hercules", "GTB Artemis")


def vp_path(explicit: str | None = None) -> str:
    """Archive to read: explicit argument, else the FS2_VP environment variable, else the Steam default."""
    return explicit or os.environ.get("FS2_VP") or DEFAULT_VP


def read_vp_entry(vp_file: str, inner_path: str) -> bytes:
    """Return the bytes of one file inside a VP archive (case-insensitive path, '/' separators)."""
    want = inner_path.lower()
    with open(vp_file, "rb") as f:
        magic, _version, dir_offset, count = struct.unpack("<4siii", f.read(16))
        if magic != b"VPVP":
            raise ValueError(f"{vp_file} is not a VP archive (magic {magic!r})")
        f.seek(dir_offset)
        directory = f.read(44 * count)
        stack: list[str] = []
        for i in range(count):
            offset, size, raw_name, _stamp = struct.unpack_from("<ii32si", directory, 44 * i)
            name = raw_name.split(b"\0", 1)[0].decode("latin-1")
            if size == 0 and "." not in name:
                if name == "..":
                    if stack:
                        stack.pop()
                else:
                    stack.append(name.lower())
                continue
            if "/".join(stack + [name.lower()]) == want:
                f.seek(offset)
                return f.read(size)
    raise FileNotFoundError(f"{inner_path} not found in {vp_file}")


def read_ships_tbl(vp: str | None = None) -> bytes:
    return read_vp_entry(vp_path(vp), SHIPS_TBL_PATH)


# ---------------------------------------------------------------------------------------
# Table parsing. ships.tbl is a token file: "$Key: value" lines, ';' starts a comment.
# Only the flight fields are kept; everything else in a ship block is skipped.
# ---------------------------------------------------------------------------------------

_FIELD_RE = re.compile(r"^\s*([$+][^:]+):\s*(.*)$")

_WANTED = {
    "$name", "$damp", "$rotdamp", "$max velocity", "$rotation time", "$rear velocity",
    "$forward accel", "$forward decel", "$slide accel", "$slide decel",
    "$power output", "$max oclk speed", "$afterburner", "+aburn max vel", "+aburn for accel",
    "+aburn fuel", "+aburn burn rate", "+aburn rec rate",
}


def _numbers(text: str) -> list[float]:
    return [float(x) for x in re.split(r"[,\s]+", text.strip()) if x]


def parse_ships_tbl(text: str) -> dict[str, dict[str, str]]:
    """Return {class name: {lower-cased key: raw value text}} for the flight fields of every ship class."""
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    start = text.find("#Ship Classes")
    if start < 0:
        raise ValueError("no #Ship Classes section in ships.tbl")
    end = text.find("#End", start)
    section = text[start : end if end >= 0 else len(text)]

    ships: dict[str, dict[str, str]] = {}
    current: dict[str, str] | None = None
    for line in section.split("\n"):
        stripped = line.strip()
        if not stripped or stripped.startswith(";"):
            continue
        m = _FIELD_RE.match(line)
        if not m:
            continue
        key = re.sub(r"\s+", " ", m.group(1).strip().lower())
        if key not in _WANTED:
            continue
        value = m.group(2).split(";", 1)[0].strip()
        if key == "$name":
            current = {}
            ships.setdefault(value, current)  # first definition wins, as in the game
            if ships[value] is not current:
                current = None
            continue
        if current is not None and key not in current:
            current[key] = value
    return ships


@dataclass(frozen=True)
class ShipFlight:
    """The ships.tbl fields that drive flight, in table units (metres, seconds)."""

    name: str
    max_vel: tuple[float, float, float]        # $Max Velocity (x, y, z)
    max_rear_vel: float                        # $Rear Velocity
    rotation_time: tuple[float, float, float]  # $Rotation time: seconds per full turn (pitch, yaw, bank)
    damp: float                                # $Damp: side-slip time constant, s
    rotdamp: float                             # $Rotdamp: turn-rate time constant, s
    forward_accel: float                       # $Forward accel: speed-up time constant, s
    forward_decel: float                       # $Forward decel: slow-down time constant, s
    slide_accel: float                         # $Slide accel (unused unless max_vel x/y > 0)
    slide_decel: float                         # $Slide decel
    has_afterburner: bool                      # $Afterburner
    aburn_max_vel: tuple[float, float, float]  # +Aburn Max Vel
    aburn_forward_accel: float                 # +Aburn For accel: afterburner speed-up time constant, s
    aburn_fuel: float                          # +Aburn Fuel: capacity
    aburn_burn_rate: float                     # +Aburn Burn Rate: fuel per second while burning
    aburn_recover_rate: float                  # +Aburn Rec Rate: fuel per second while recharging (table rate)
    max_overclocked_speed: float               # $Max Oclk Speed (only used by the ETS engine slider; not modelled)

    @property
    def max_speed(self) -> float:
        """Top speed: the magnitude of $Max Velocity (ship.cpp: max_speed = |max_vel|)."""
        return math.sqrt(sum(c * c for c in self.max_vel))

    @property
    def max_rotvel(self) -> tuple[float, float, float]:
        """Maximum turn rate per axis in rad/s: 2*pi / rotation time (ship.cpp)."""
        return tuple(2.0 * math.pi / t for t in self.rotation_time)  # type: ignore[return-value]

    @property
    def slide_enabled(self) -> bool:
        """True when the table gives the ship sideways/vertical velocity (never for fighters or bombers)."""
        return self.max_vel[0] > 1e-6 or self.max_vel[1] > 1e-6

    def to_dict(self) -> dict:
        d = asdict(self)
        d["max_speed"] = self.max_speed
        d["max_rotvel"] = list(self.max_rotvel)
        d["slide_enabled"] = self.slide_enabled
        return d


def _ship_from_fields(name: str, f: dict[str, str]) -> ShipFlight:
    def req(key: str) -> str:
        if key not in f:
            raise KeyError(f"ships.tbl entry '{name}' has no {key}")
        return f[key]

    def vec(key: str) -> tuple[float, float, float]:
        n = _numbers(req(key))
        if len(n) != 3:
            raise ValueError(f"'{name}' {key}: expected 3 numbers, got {n}")
        return (n[0], n[1], n[2])

    def one(key: str) -> float:
        return _numbers(req(key))[0]

    has_ab = req("$afterburner").strip().upper() in ("YES", "1", "TRUE")
    if has_ab:
        ab_vel, ab_acc = vec("+aburn max vel"), one("+aburn for accel")
        ab_fuel, ab_burn, ab_rec = one("+aburn fuel"), one("+aburn burn rate"), one("+aburn rec rate")
    else:
        ab_vel, ab_acc, ab_fuel, ab_burn, ab_rec = (0.0, 0.0, 0.0), 0.0, 0.0, 0.0, 0.0

    return ShipFlight(
        name=name,
        max_vel=vec("$max velocity"),
        max_rear_vel=one("$rear velocity"),
        rotation_time=vec("$rotation time"),
        damp=one("$damp"),
        rotdamp=one("$rotdamp"),
        forward_accel=one("$forward accel"),
        forward_decel=one("$forward decel"),
        slide_accel=one("$slide accel"),
        slide_decel=one("$slide decel"),
        has_afterburner=has_ab,
        aburn_max_vel=ab_vel,
        aburn_forward_accel=ab_acc,
        aburn_fuel=ab_fuel,
        aburn_burn_rate=ab_burn,
        aburn_recover_rate=ab_rec,
        max_overclocked_speed=one("$max oclk speed"),
    )


def list_ships(vp: str | None = None) -> list[str]:
    return list(parse_ships_tbl(read_ships_tbl(vp).decode("latin-1")))


def get_ship(name: str, vp: str | None = None) -> ShipFlight:
    """Flight fields for one ship class (name match ignores case), e.g. get_ship("GTF Ulysses")."""
    table = parse_ships_tbl(read_ships_tbl(vp).decode("latin-1"))
    for key, fields in table.items():
        if key.lower() == name.strip().lower():
            return _ship_from_fields(key, fields)
    raise KeyError(f"no ship class '{name}' in ships.tbl (have {len(table)} classes)")


def ships_tbl_sha256(vp: str | None = None) -> str:
    """Fingerprint of the table the numbers came from, stored beside every reference."""
    return hashlib.sha256(read_ships_tbl(vp)).hexdigest()


if __name__ == "__main__":
    import json
    import sys

    names = sys.argv[1:] or list(REFERENCE_CLASSES)
    for n in names:
        print(json.dumps(get_ship(n).to_dict(), indent=2))
