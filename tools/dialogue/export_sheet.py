"""Export every line of Monolith's dialogue to a spreadsheet the user can rewrite.

One row per line (radio messages, wingman and Earth radio, briefings, debriefs). The user fills the two
NEW columns; a later import step reads them back into the mission and radio files, and only approved,
changed lines are sent for voicing (never generate voice files before the user approves the lines).

  python -I tools/dialogue/export_sheet.py [out.xlsx]
"""
import json
import os
import sys

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.worksheet.datavalidation import DataValidation  # noqa: F401 (kept for later columns)

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources")
MISSIONS = os.path.join(RES, "Missions")
RADIO = os.path.join(RES, "AI", "radio.json")
VOICE = os.path.join(ROOT, "audio_src", "voice", "voice_lines.json")


def load(p):
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def voice_info():
    """source key -> {speakers, intensities, said} from the voice batch."""
    out = {}
    if not os.path.exists(VOICE):
        return out
    for line in load(VOICE)["lines"]:
        for src in line["sources"]:
            key = (src, line["text"])
            e = out.setdefault(key, {"speakers": [], "intensity": [], "said": []})
            if line["voice"] not in e["speakers"]:
                e["speakers"].append(line["voice"])
            if line["intensity"] not in e["intensity"]:
                e["intensity"].append(line["intensity"])
            if line.get("say") and line["say"] not in e["said"]:
                e["said"].append(line["say"])
    return out


def events_sending(mission):
    """message id -> short description of the event(s) that send it."""
    found = {}
    for ev in mission.get("events", []):
        for act in ev.get("do", []):
            if isinstance(act, dict) and act.get("do") in ("radio", "callOut"):
                desc = ev.get("name", "")
                if ev.get("whenWhy"):
                    desc += " - " + ev["whenWhy"]
                mid = act.get("message") or act.get("id")
                if mid:
                    found.setdefault(mid, []).append(desc)
                if act.get("text"):  # an inline line (callOut) is matched by its text
                    found.setdefault("text:" + act["text"], []).append(desc)
    return found


def rows():
    vinfo = voice_info()
    out = []
    for fn in sorted(os.listdir(MISSIONS)):
        if not fn.endswith(".mission.json"):
            continue
        m = load(os.path.join(MISSIONS, fn))
        mid = m.get("id") or fn.split(".")[0]
        mname = m.get("name", mid)
        sends = events_sending(m)
        b = m.get("briefing") or {}
        for i, stage in enumerate(b.get("stages", [])):
            out.append(dict(id=f"{mid}:briefing:{i + 1}", mission=mname, kind="Briefing page",
                            speaker="(text)", when=f"Briefing, page {i + 1}", intensity="",
                            text=stage, said="", voiced="no"))
        for i, qa in enumerate(b.get("questions", [])):
            out.append(dict(id=f"{mid}:briefing:q{i + 1}", mission=mname, kind="Briefing question",
                            speaker="(text)", when=f"Briefing question: {qa.get('q', '')}", intensity="",
                            text=qa.get("a", ""), said="", voiced="no"))
        for i, d in enumerate(m.get("debriefing", [])):
            out.append(dict(id=f"{mid}:debrief:{i + 1}", mission=mname, kind="Debrief",
                            speaker="(text)", when="Debrief: " + json.dumps(d.get("when", {})),
                            intensity="", text=d.get("text", ""), said="", voiced="no"))
        for msg in m.get("messages", []):
            key = (f"{mid}:{msg['id']}", msg["text"])
            v = vinfo.get(key, {})
            out.append(dict(
                id=f"{mid}:{msg['id']}", mission=mname, kind="Radio",
                speaker=msg.get("from", "") + (f"  (voice: {', '.join(v['speakers'])})" if v.get("speakers") else ""),
                when="; ".join(sends.get(msg["id"], []) + sends.get("text:" + msg["text"], [])) or "(sent by an event)",
                intensity=", ".join(v.get("intensity", [])), text=msg["text"],
                said=" | ".join(v.get("said", [])) if v.get("said") and v["said"] != [msg["text"]] else "",
                voiced="yes" if v else "no"))
    radio = load(RADIO)
    for cat, variants in radio.get("lines", {}).items():
        if not isinstance(variants, list):
            continue
        for i, text in enumerate(variants):
            key = (f"radio.json:{cat}", text)
            v = vinfo.get(key, {})
            out.append(dict(
                id=f"radio:{cat}:{i + 1}", mission="(all missions)",
                kind="Earth radio" if cat.startswith("earth") else "Wingman / crew radio",
                speaker=", ".join(v.get("speakers", [])) or "(any)", when=f"Radio category '{cat}', variant {i + 1}",
                intensity=", ".join(v.get("intensity", [])), text=text,
                said=" | ".join(v.get("said", [])) if v.get("said") and v["said"] != [text] else "",
                voiced="yes" if v else "no"))
    return out


def main():
    out_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "design", "dialogue", "monolith_dialogue.xlsx")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    data = rows()
    wb = Workbook()
    ws = wb.active
    ws.title = "Dialogue"
    head = ["Line ID", "Mission", "Kind", "Speaker", "When it plays", "Intensity",
            "Current on-screen text", "Current spoken text (if different)", "Voiced now",
            "NEW on-screen text", "NEW spoken text", "Notes"]
    widths = [26, 16, 16, 24, 46, 12, 60, 40, 9, 60, 60, 30]
    ws.append(head)
    for r in data:
        ws.append([r["id"], r["mission"], r["kind"], r["speaker"], r["when"], r["intensity"],
                   r["text"], r["said"], r["voiced"], "", "", ""])
    bold = Font(bold=True, color="FFFFFF")
    fill = PatternFill("solid", fgColor="3A3F47")
    new_fill = PatternFill("solid", fgColor="FFF4D6")
    for c, w in enumerate(widths, start=1):
        col = ws.cell(row=1, column=c).column_letter
        ws.column_dimensions[col].width = w
        ws.cell(row=1, column=c).font = bold
        ws.cell(row=1, column=c).fill = fill
    for row in ws.iter_rows(min_row=2):
        for cell in row:
            cell.alignment = Alignment(wrap_text=True, vertical="top")
        row[9].fill = new_fill
        row[10].fill = new_fill
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = ws.dimensions

    how = wb.create_sheet("How to use")
    for line in [
        "Monolith dialogue sheet.",
        "",
        "Write your replacement in the yellow NEW columns. Leave a NEW cell empty to keep the current line.",
        "NEW on-screen text: what appears on screen (radio captions, briefings, debriefs).",
        "NEW spoken text: what the voice says, if different from the on-screen text (leave empty to speak the on-screen text).",
        "Intensity: calm, tense or frantic sets how the voice is performed. You can change it in Notes.",
        "Rows marked 'Voiced now: yes' already have recordings; changing them means re-recording only those lines.",
        "No voice files are generated until you have approved the lines.",
        "Line ID ties each row back to its mission or radio file, so please don't edit that column.",
    ]:
        how.append([line])
    how.column_dimensions["A"].width = 120
    wb.save(out_path)
    print(f"{len(data)} lines -> {out_path}")


if __name__ == "__main__":
    main()
