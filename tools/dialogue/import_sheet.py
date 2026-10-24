"""Import the user's rewrite of the dialogue sheet back into the game.

The sheet (design/dialogue/monolith_dialogue.xlsx, written by export_sheet.py) has one row per line and two yellow columns for the user: NEW on-screen text and NEW spoken text,
and a Notes column. This reads them back and writes the new text into the mission files and radio.json by Line ID, and says exactly which lines changed.

  python -I tools/dialogue/import_sheet.py [--sheet FILE] [--dry-run]

The rules (the user's, 2026-10-24/25):
  * A row with NEW on-screen text changes that line. The spoken text is the same as the on-screen text unless the NEW spoken column says otherwise (it is ignored when empty).
    Spoken text that differs is kept apart in audio_src/voice/spoken.json (Line ID -> what the voice says); the on-screen text is the mission's.
  * A row's intensity (calm, tense, frantic) is kept unless Notes says "intensity: frantic" (or is just that word): that goes to audio_src/voice/intensity.json, which
    audio_src/voice/collect_lines.py reads over its own table.
  * Briefing pages, briefing answers and debrief lines (ids mission:briefing:N, mission:briefing:qN, mission:debrief:N) are read back the same way.
  * Only the text is touched: the mission files are edited as text (their comments and layout stay), then checked by comparing the parsed files before and after: nothing but the
    named lines may differ. An inline radio or callOut action that carries the same words as a changed message (Dead Water's "go dark" call-out) is changed with it.
  * Approving: a line the mission author marked as a draft ("linesPending": true on the mission, "pending": true on the line) is pending approval until the user says otherwise. Writing
    a NEW on-screen or NEW spoken text for it, or "yes" in the Approve column, approves it: its "pending": false is written into the mission file (and when no line of the mission is pending
    any more, the mission's linesPending and the explicit "pending": false flags are taken out). Approved lines are listed in changed_lines.json, so voice.ps1 -List voices them, and nothing
    else. A tick on a line that is already approved changes nothing. Briefing and debrief text has no flag of its own (see the sheet's How to use).
  * Call-outs (the radio and callOut actions with their own text inside an event) have Line IDs made from their event's name (line_ids.py): mission:callout:event-name.
  * It makes NO voice files. It writes design/dialogue/changed_lines.json (and .txt): the lines that need voicing. tools/audio/voice.ps1 -List voices exactly those (and nothing else).
    A row that only repeats the current text changes nothing.

Options: --missions DIR, --radio FILE, --state DIR (where spoken.json and intensity.json go), --changes FILE: for testing the import on a copy.
"""
import argparse
import copy
import datetime
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from line_ids import inline_lines  # noqa: E402
from openpyxl import load_workbook  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources")
INTENSITIES = ("calm", "tense", "frantic")


# ------------------------------------------------------------------------------------------------ a JSON reader that remembers where every string is

class Doc:
    """A JSON file (comments allowed) read once, with the character span of every string value, by path. Editing the text then touches nothing else."""

    def __init__(self, path):
        self.path = path
        with open(path, "rb") as f:
            raw = f.read().decode("utf-8")
        self.crlf = "\r\n" in raw
        self.text = raw.replace("\r\n", "\n")
        self._parse()

    def _parse(self):
        self.i = 0
        self.spans = {}      # path tuple -> (start, end) of the string literal including its quotes
        self.parents = {}    # path tuple -> the dict (or list) holding it
        self.members = {}    # path of a dict -> [(key, where the key starts, where its value starts, where its value ends)]
        self.root = self._value((), None)

    # tolerant recursive descent
    def _ws(self):
        t = self.text
        while self.i < len(t):
            c = t[self.i]
            if c in " \t\r\n":
                self.i += 1
            elif t.startswith("//", self.i):
                while self.i < len(t) and t[self.i] != "\n":
                    self.i += 1
            elif t.startswith("/*", self.i):
                j = t.find("*/", self.i + 2)
                self.i = len(t) if j < 0 else j + 2
            else:
                break

    def _string(self):
        t = self.text
        start = self.i
        self.i += 1
        while t[self.i] != '"':
            self.i += 2 if t[self.i] == "\\" else 1
        self.i += 1
        return json.loads(t[start:self.i]), (start, self.i)

    def _value(self, path, parent):
        self._ws()
        t = self.text
        c = t[self.i]
        if c == '"':
            v, span = self._string()
            self.spans[path] = span
            self.parents[path] = parent
            return v
        if c == "{":
            self.i += 1
            d = {}
            mem = self.members[path] = []
            while True:
                self._ws()
                if t[self.i] == "}":
                    self.i += 1
                    return d
                if t[self.i] == ",":
                    self.i += 1
                    continue
                kstart = self.i
                k, _ = self._string()
                self._ws()
                assert t[self.i] == ":", f"{self.path}: expected ':' at {self.i}"
                self.i += 1
                self._ws()
                vstart = self.i
                d[k] = self._value(path + (k,), d)
                mem.append((k, kstart, vstart, self.i))
        if c == "[":
            self.i += 1
            a = []
            while True:
                self._ws()
                if t[self.i] == "]":
                    self.i += 1
                    return a
                if t[self.i] == ",":
                    self.i += 1
                    continue
                a.append(self._value(path + (len(a),), a))
        j = self.i
        while j < len(t) and t[j] not in ",}] \t\r\n":
            j += 1
        tok = t[j - (j - self.i):j]
        self.i = j
        return json.loads(tok)

    def replace_string(self, path, new):
        s, e = self.spans[path]
        lit = json.dumps(new, ensure_ascii=False)
        self.text = self.text[:s] + lit + self.text[e:]
        delta = len(lit) - (e - s)
        for p, (a, b) in list(self.spans.items()):          # later spans move with the edit
            if a > s:
                self.spans[p] = (a + delta, b + delta)
        self.spans[path] = (s, s + len(lit))

    def set_bool(self, dpath, key, value):
        """Make "key": true/false a member of the dict at dpath: changed where it is, else put in after the dict's first member. The text is re-read first, so spans are fresh."""
        self._parse()
        mem = self.members[dpath]
        lit = "true" if value else "false"
        for k, ks, vs, ve in mem:
            if k == key:
                self.text = self.text[:vs] + lit + self.text[ve:]
                self._parse()
                return
        ve = mem[0][3]
        self.text = self.text[:ve] + f', "{key}": {lit}' + self.text[ve:]
        self._parse()

    def remove_member(self, dpath, key):
        """Take "key": ... out of the dict at dpath, with its comma. True if it was there."""
        self._parse()
        mem = self.members[dpath]
        for n, (k, ks, vs, ve) in enumerate(mem):
            if k == key:
                if n + 1 < len(mem):
                    self.text = self.text[:ks] + self.text[mem[n + 1][1]:]
                elif n > 0:
                    self.text = self.text[:mem[n - 1][3]] + self.text[ve:]
                else:
                    raise ValueError(f"{self.path}: cannot take the only member out of {dpath}")
                self._parse()
                return True
        return False

    def save(self):
        out = self.text.replace("\n", "\r\n") if self.crlf else self.text
        with open(self.path, "wb") as f:
            f.write(out.encode("utf-8"))


def strip_comments(text):
    return json.loads(re.sub(r"(?m)^\s*//.*$", "", text))


# ------------------------------------------------------------------------------------------------ the sheet

def read_sheet(path):
    wb = load_workbook(path, read_only=True, data_only=True)
    ws = wb["Dialogue"]
    rows = list(ws.iter_rows(values_only=True))
    head = [str(h or "").strip() for h in rows[0]]
    col = {h: i for i, h in enumerate(head)}
    need = ["Line ID", "Current on-screen text", "NEW on-screen text", "NEW spoken text", "Notes", "Intensity"]
    for n in need:
        if n not in col:
            sys.exit(f"the sheet has no '{n}' column (is this the export of tools/dialogue/export_sheet.py?)")
    out = []
    for r in rows[1:]:
        if not r or not r[col["Line ID"]]:
            continue

        def cell(name):
            v = r[col[name]] if col[name] < len(r) else None
            return "" if v is None else str(v).strip()
        out.append(dict(id=cell("Line ID"), kind=cell("Kind") if "Kind" in col else "", speaker=cell("Speaker") if "Speaker" in col else "",
                        current=cell("Current on-screen text"), intensity=cell("Intensity"),
                        new=cell("NEW on-screen text"), new_said=cell("NEW spoken text"), notes=cell("Notes"),
                        approve=truthy(cell("Approve")) if "Approve" in col else False))
    return out


def truthy(v):
    """An Approve cell: yes, y, x, a tick, true, approve, approved, 1 (anything else, including empty and no, is not an approval)."""
    return (v or "").strip().lower() in ("yes", "y", "x", "\u2713", "\u2714", "true", "approve", "approved", "ok", "1")


def intensity_from_notes(notes):
    if not notes:
        return None
    n = notes.lower().strip()
    if n in INTENSITIES:
        return n
    m = re.search(r"intensity\s*[:=\-]?\s*(calm|tense|frantic)\b", n)
    return m.group(1) if m else None


# ------------------------------------------------------------------------------------------------ where a Line ID lives

class Files:
    def __init__(self, missions_dir, radio_path):
        self.missions_dir = missions_dir
        self.radio_path = radio_path
        self.docs = {}

    def doc(self, path):
        if path not in self.docs:
            self.docs[path] = Doc(path)
        return self.docs[path]

    def mission_doc(self, mid):
        for fn in sorted(os.listdir(self.missions_dir)):
            if fn.endswith(".mission.json"):
                d = self.doc(os.path.join(self.missions_dir, fn))
                if (d.root.get("id") or fn.split(".")[0]) == mid:
                    return d
        return None

    def locate(self, line_id):
        """(doc, path of the string, current text) for a Line ID, or None."""
        parts = line_id.split(":")
        if parts[0] == "radio" and len(parts) == 3:
            d = self.doc(self.radio_path)
            cat, n = parts[1], int(parts[2])
            arr = d.root.get("lines", {}).get(cat)
            if isinstance(arr, list) and 1 <= n <= len(arr) and isinstance(arr[n - 1], str):
                return d, ("lines", cat, n - 1), arr[n - 1]
            return None
        d = self.mission_doc(parts[0])
        if d is None:
            return None
        if len(parts) >= 3 and parts[1] in ("callout", "inline"):
            for lid, p, act in inline_lines(d.root):
                if lid == line_id:
                    return d, p + ("text",), act["text"]
            return None
        if len(parts) == 2:
            for i, m in enumerate(d.root.get("messages", [])):
                if m.get("id") == parts[1]:
                    return d, ("messages", i, "text"), m["text"]
            return None
        if len(parts) == 3 and parts[1] == "briefing":
            b = d.root.get("briefing") or {}
            if parts[2].startswith("q"):
                i = int(parts[2][1:]) - 1
                qs = b.get("questions", [])
                if 0 <= i < len(qs):
                    return d, ("briefing", "questions", i, "a"), qs[i].get("a", "")
            else:
                i = int(parts[2]) - 1
                st = b.get("stages", [])
                if 0 <= i < len(st):
                    return d, ("briefing", "stages", i), st[i]
            return None
        if len(parts) == 3 and parts[1] == "debrief":
            i = int(parts[2]) - 1
            db = d.root.get("debriefing", [])
            if 0 <= i < len(db):
                return d, ("debriefing", i, "text"), db[i].get("text", "")
        return None

    @staticmethod
    def at(root, path):
        for k in path:
            root = root[k]
        return root

    def flag_path(self, doc, path):
        """The path of the dict that carries a radio line's pending flag (a message, or a radio or callOut action with its own text), or None for text that has no flag."""
        if len(path) == 3 and path[0] == "messages" and path[-1] == "text":
            return path[:2]
        if path[0] == "events" and path[-1] == "text":
            parent = doc.parents.get(path)
            if isinstance(parent, dict) and parent.get("do") in ("radio", "callOut"):
                return path[:-1]
        return None

    def pending_of(self, doc, objpath):
        return bool(self.at(doc.root, objpath).get("pending", doc.root.get("linesPending", False)))

    def radio_lines(self, doc):
        """The paths of every radio line in a mission that can carry a pending flag: its messages, and its call-outs."""
        out = [("messages", i) for i in range(len(doc.root.get("messages", [])))]
        out += [p for _, p, _ in inline_lines(doc.root)]
        return out

    def inline_twins(self, doc, old):
        """Inline radio or callOut actions in the same mission that say exactly the old words (they are the same line)."""
        out = []
        for path, (s, e) in doc.spans.items():
            if path and path[-1] == "text" and path[0] == "events":
                parent = doc.parents.get(path)
                if isinstance(parent, dict) and parent.get("do") in ("radio", "callOut") and parent.get("text") == old:
                    out.append(path)
        return out


# ------------------------------------------------------------------------------------------------ main

def diff_paths(a, b, path=()):
    """Every path at which two parsed JSON values differ."""
    out = []
    if isinstance(a, dict) and isinstance(b, dict):
        for k in sorted(set(a) | set(b)):
            if k not in a or k not in b:
                out.append(path + (k,))
            else:
                out += diff_paths(a[k], b[k], path + (k,))
    elif isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            out.append(path)
        else:
            for i, (x, y) in enumerate(zip(a, b)):
                out += diff_paths(x, y, path + (i,))
    elif a != b:
        out.append(path)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sheet", default=os.path.join(ROOT, "design", "dialogue", "monolith_dialogue.xlsx"))
    ap.add_argument("--missions", default=os.path.join(RES, "Missions"))
    ap.add_argument("--radio", default=os.path.join(RES, "AI", "radio.json"))
    ap.add_argument("--state", default=os.path.join(ROOT, "audio_src", "voice"))
    ap.add_argument("--changes", default=os.path.join(ROOT, "design", "dialogue", "changed_lines.json"))
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    rows = read_sheet(a.sheet)
    files = Files(a.missions, a.radio)
    before = {}
    changes, notes_ignored, stale, errors = [], [], [], []
    spoken_path = os.path.join(a.state, "spoken.json")
    inten_path = os.path.join(a.state, "intensity.json")
    spoken = json.load(open(spoken_path, encoding="utf-8")) if os.path.exists(spoken_path) else {}
    inten = json.load(open(inten_path, encoding="utf-8")) if os.path.exists(inten_path) else {}
    spoken.pop("_about", None)
    inten.pop("_about", None)
    edits = []   # (doc, path, new)
    approvals = []   # (doc, path of the dict holding the flag, line id)
    approve_ignored = []
    expected = {}   # path of doc -> set of paths

    for r in rows:
        want_text = r["new"]
        want_said = r["new_said"]
        want_int = intensity_from_notes(r["notes"])
        if r["notes"] and want_int is None:
            notes_ignored.append((r["id"], r["notes"]))
        if not (want_text or want_said or want_int or r["approve"]):
            continue
        loc = files.locate(r["id"])
        if loc is None:
            errors.append(f"{r['id']}: no such line in the mission files or radio.json (the Line ID is how a row finds its line: do not edit that column)")
            continue
        doc, path, cur = loc
        before.setdefault(doc.path, copy.deepcopy(doc.root))
        if r["current"] and r["current"] != cur:
            stale.append((r["id"], r["current"], cur))
        entry = dict(id=r["id"], kind=r["kind"], speaker=r["speaker"], old=cur, new=cur, said=None, intensity=None, what=[])
        flag = files.flag_path(doc, path)
        if flag is not None and files.pending_of(doc, flag) and (r["approve"] or want_text or want_said):
            approvals.append((doc, flag, r["id"]))              # a line the author left as a draft: the user's text, or a tick, approves it
            entry["what"].append("approved")
        elif r["approve"] and flag is None:
            approve_ignored.append(r["id"])
        if want_text and want_text != cur:
            entry["new"] = want_text
            entry["what"].append("text")
            edits.append((doc, path, want_text, cur))
        new_said = want_said if want_said and want_said != entry["new"] else None
        old_said = spoken.get(r["id"])
        if want_text and not want_said:
            new_said = None                      # the user's rule: spoken equals on-screen unless the NEW spoken column says otherwise
        if new_said != old_said and (want_said or want_text):
            if new_said:
                spoken[r["id"]] = new_said
            else:
                spoken.pop(r["id"], None)
            entry["said"] = new_said
            entry["what"].append("spoken")
        if want_int and want_int != inten.get(r["id"]) and want_int != r["intensity"].split(",")[0].strip():
            inten[r["id"]] = want_int
            entry["intensity"] = want_int
            entry["what"].append("intensity")
        if entry["what"]:
            changes.append(entry)

    if errors:
        print("\n".join("ERROR " + e for e in errors))
        sys.exit(1)

    # apply, longest paths first inside a file does not matter: spans move with each edit
    twins = []
    own = {(doc.path, path) for doc, path, new, old in edits}          # a call-out with a row of its own is edited by that row, not as a twin
    for doc, path, new, old in edits:
        doc.replace_string(path, new)
        for tp in files.inline_twins(doc, old):
            if (doc.path, tp) in own or tp == path:
                continue
            doc.replace_string(tp, new)
            twins.append((os.path.basename(doc.path), tp, old, new))
        expected.setdefault(doc.path, set()).add(path)
    for doc, path, new, old in edits:
        for tp in [t for t in twins if t[0] == os.path.basename(doc.path)]:
            expected[doc.path].add(tp[1])

    # approvals: "pending": false on each approved line; a mission with nothing pending left loses its flags
    cleared = []
    for doc, flag, line_id in approvals:
        doc.set_bool(flag, "pending", False)
        expected.setdefault(doc.path, set()).add(flag + ("pending",))
    for doc in {a[0] for a in approvals}:
        if not any(files.pending_of(doc, p) for p in files.radio_lines(doc)):
            for p in files.radio_lines(doc):
                if files.at(doc.root, p).get("pending") is False:
                    doc.remove_member(p, "pending")
                    expected[doc.path].add(p + ("pending",))
            for key in ("linesPending", "linesPendingWhy"):
                if doc.remove_member((), key):
                    expected[doc.path].add((key,))
            cleared.append(os.path.basename(doc.path))

    # nothing but the named lines may differ
    problems = []
    for dpath, doc in ((p, files.docs[p]) for p in files.docs):
        if dpath not in before:
            continue
        try:
            after = strip_comments(doc.text) if True else None
        except Exception as e:
            problems.append(f"{os.path.basename(dpath)}: no longer valid JSON after the edit: {e}")
            continue
        for p in diff_paths(before[dpath], after):
            if p not in expected.get(dpath, set()):
                problems.append(f"{os.path.basename(dpath)}: {p} changed and should not have")
        for p in expected.get(dpath, set()):
            pass
    if problems:
        print("\n".join("ERROR " + p for p in problems))
        sys.exit(1)

    # report
    print(f"{len(changes)} rows changed ({len(approvals)} approved):")
    for c in changes:
        print(f"  {c['id']:28s} [{', '.join(c['what'])}]")
        if "text" in c["what"]:
            print(f"      was: {c['old']}")
            print(f"      now: {c['new']}")
        if c["said"]:
            print(f"      spoken as: {c['said']}")
        if c["intensity"]:
            print(f"      intensity: {c['intensity']}")
    for t in twins:
        print(f"  (also changed with it: the same words in {t[0]} {'/'.join(str(x) for x in t[1])})")
    for c in cleared:
        print(f"  every radio line of {c} is approved now: its linesPending flag is gone")
    for i in approve_ignored:
        print(f"  {i}: ticked Approve, but a briefing or debrief page has no flag of its own (the mission is cleared when all its radio lines are approved)")
    for (i, n) in notes_ignored:
        print(f"  note on {i} left alone (not an intensity): {n}")
    for (i, was, now) in stale:
        print(f"  WARNING {i}: the sheet's 'current' text is not the file's now (the line was edited after the sheet was exported):\n      sheet: {was}\n      file : {now}")
    if a.dry_run:
        print("dry run: nothing written")
        return 0

    for p in files.docs:
        if p in before and expected.get(p):
            files.docs[p].save()
    os.makedirs(a.state, exist_ok=True)
    note = "Written by tools/dialogue/import_sheet.py from the user's sheet. "
    with open(spoken_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"_about": note + "Line ID -> what the voice says, where it differs from the on-screen text. Read by audio_src/voice/collect_lines.py.", **spoken}, f, indent=1, ensure_ascii=False)
        f.write("\n")
    with open(inten_path, "w", encoding="utf-8", newline="\n") as f:
        json.dump({"_about": note + "Line ID -> calm, tense or frantic, over the table in collect_lines.py.", **inten}, f, indent=1, ensure_ascii=False)
        f.write("\n")
    out = dict(format="monolith-dialogue-changes/1", imported=datetime.datetime.now().strftime("%Y-%m-%d %H:%M"), sheet=os.path.basename(a.sheet),
               lines=[dict(id=c["id"], what=c["what"], speaker=c["speaker"], old=c["old"], new=c["new"], spoken=c["said"], intensity=c["intensity"], approved="approved" in c["what"]) for c in changes])
    os.makedirs(os.path.dirname(a.changes), exist_ok=True)
    with open(a.changes, "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, indent=1, ensure_ascii=False)
        f.write("\n")
    with open(os.path.splitext(a.changes)[0] + ".txt", "w", encoding="utf-8", newline="\n") as f:
        f.write(f"Lines changed by the import of {os.path.basename(a.sheet)} ({out['imported']}): these need (re-)voicing.\n\n")
        for c in changes:
            f.write(f"{c['id']}  [{', '.join(c['what'])}]\n  was: {c['old']}\n  now: {c['new']}\n" + (f"  spoken: {c['said']}\n" if c["said"] else "") + (f"  intensity: {c['intensity']}\n" if c["intensity"] else "") + "\n")
    print(f"wrote {a.changes} and {os.path.basename(spoken_path)}, {os.path.basename(inten_path)}; no voice file was made")
    return 0


if __name__ == "__main__":
    sys.exit(main())
