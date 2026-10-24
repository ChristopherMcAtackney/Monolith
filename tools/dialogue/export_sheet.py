"""Export every line of Monolith's dialogue to a spreadsheet the user can rewrite and approve.

One row per line: radio messages, call-outs (the radio and callOut actions that carry their text inside an event), wingman and Earth radio, briefing pages and answers, debriefs.
The user fills the yellow columns and tools/dialogue/import_sheet.py reads them back into the mission and radio files:

  NEW on-screen text   a replacement for the line (writing one also approves a pending line)
  NEW spoken text      what the voice says, when that differs from the on-screen text
  Approve              "yes" approves a pending line as it stands (the dropdown has it)
  Notes                "intensity: frantic" (calm, tense, frantic) sets how the voice performs it

Status says "pending approval" for a line the mission author marked as a draft ("linesPending": true on the mission, or "pending": true on the line): no voice is made for it until the user
approves it. A briefing or debrief page has no flag of its own and shows its mission's status. Never generate voice files before the user approves the lines.

  python -I tools/dialogue/export_sheet.py [out.xlsx]
"""
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from line_ids import inline_lines  # noqa: E402
from openpyxl import Workbook  # noqa: E402
from openpyxl.styles import Alignment, Font, PatternFill  # noqa: E402
from openpyxl.worksheet.datavalidation import DataValidation  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RES = os.path.join(ROOT, "Game", "Assets", "Monolith", "Resources")
MISSIONS = os.path.join(RES, "Missions")
RADIO = os.path.join(RES, "AI", "radio.json")
VOICE = os.path.join(ROOT, "audio_src", "voice", "voice_lines.json")

APPROVED = "approved"
PENDING = "pending approval"


def load(p):
    with open(p, encoding="utf-8") as f:
        return json.loads(re.sub(r"(?m)^\s*//.*$", "", f.read()))


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


def event_of(mission, path):
    ev = mission["events"][path[1]]
    desc = ev.get("name", "")
    if ev.get("whenWhy"):
        desc += " - " + ev["whenWhy"]
    return desc


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
        file_pending = bool(m.get("linesPending", False))
        text_status = PENDING if file_pending else APPROVED          # briefing and debrief text has no flag of its own: it shows the mission's
        b = m.get("briefing") or {}
        for i, stage in enumerate(b.get("stages", [])):
            out.append(dict(id=f"{mid}:briefing:{i + 1}", mission=mname, kind="Briefing page",
                            speaker="(text)", when=f"Briefing, page {i + 1}", intensity="",
                            text=stage, said="", voiced="no", status=text_status))
        for i, qa in enumerate(b.get("questions", [])):
            out.append(dict(id=f"{mid}:briefing:q{i + 1}", mission=mname, kind="Briefing question",
                            speaker="(text)", when=f"Briefing question: {qa.get('q', '')}", intensity="",
                            text=qa.get("a", ""), said="", voiced="no", status=text_status))
        for i, d in enumerate(m.get("debriefing", [])):
            out.append(dict(id=f"{mid}:debrief:{i + 1}", mission=mname, kind="Debrief",
                            speaker="(text)", when="Debrief: " + json.dumps(d.get("when", {})),
                            intensity="", text=d.get("text", ""), said="", voiced="no", status=text_status))
        for msg in m.get("messages", []):
            key = (f"{mid}:{msg['id']}", msg["text"])
            v = vinfo.get(key, {})
            pending = bool(msg.get("pending", file_pending))
            out.append(dict(
                id=f"{mid}:{msg['id']}", mission=mname, kind="Radio",
                speaker=msg.get("from", "") + (f"  (voice: {', '.join(v['speakers'])})" if v.get("speakers") else ""),
                when="; ".join(sends.get(msg["id"], []) + sends.get("text:" + msg["text"], [])) or "(sent by an event)",
                intensity=msg.get("intensity") or ", ".join(v.get("intensity", [])), text=msg["text"],
                said=" | ".join(v.get("said", [])) if v.get("said") and v["said"] != [msg["text"]] else "",
                voiced="yes" if v and not pending else "no", status=PENDING if pending else APPROVED))
        # the call-outs (and any radio action with its own text): lines that live inside an event
        for line_id, path, act in inline_lines(m):
            src = f"{mid}:{'callOut' if act['do'] == 'callOut' else 'inline'}"
            v = vinfo.get((src, act["text"]), {})
            pending = bool(act.get("pending", file_pending))
            out.append(dict(
                id=line_id, mission=mname, kind="Call-out" if act["do"] == "callOut" else "Radio (inline)",
                speaker=(act.get("from") or "(any wingman)") + (f"  (voice: {', '.join(v['speakers'])})" if v.get("speakers") else ""),
                when=event_of(m, path) + (" - " + act["prompt"] if act.get("prompt") else ""),
                intensity=act.get("intensity") or ", ".join(v.get("intensity", [])), text=act["text"],
                said=" | ".join(v.get("said", [])) if v.get("said") and v["said"] != [act["text"]] else "",
                voiced="yes" if v and not pending else "no", status=PENDING if pending else APPROVED))
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
                voiced="yes" if v else "no", status=APPROVED))
    return out


HEAD = ["Line ID", "Mission", "Kind", "Speaker", "When it plays", "Intensity",
        "Current on-screen text", "Current spoken text (if different)", "Voiced now", "Status",
        "NEW on-screen text", "NEW spoken text", "Approve", "Notes"]
WIDTHS = [26, 16, 16, 24, 46, 12, 60, 40, 9, 16, 60, 60, 10, 30]
INPUT_COLUMNS = ("NEW on-screen text", "NEW spoken text", "Approve")


def main():
    out_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "design", "dialogue", "monolith_dialogue.xlsx")
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    data = rows()
    wb = Workbook()
    ws = wb.active
    ws.title = "Dialogue"
    ws.append(HEAD)
    for r in data:
        ws.append([r["id"], r["mission"], r["kind"], r["speaker"], r["when"], r["intensity"],
                   r["text"], r["said"], r["voiced"], r["status"], "", "", "", ""])
    bold = Font(bold=True, color="FFFFFF")
    fill = PatternFill("solid", fgColor="3A3F47")
    new_fill = PatternFill("solid", fgColor="FFF4D6")
    pending_fill = PatternFill("solid", fgColor="F8CBAD")
    pending_font = Font(bold=True, color="843C0C")
    for c, w in enumerate(WIDTHS, start=1):
        col = ws.cell(row=1, column=c).column_letter
        ws.column_dimensions[col].width = w
        ws.cell(row=1, column=c).font = bold
        ws.cell(row=1, column=c).fill = fill
    inputs = [HEAD.index(n) for n in INPUT_COLUMNS]
    status_col = HEAD.index("Status")
    for row in ws.iter_rows(min_row=2):
        for cell in row:
            cell.alignment = Alignment(wrap_text=True, vertical="top")
        for i in inputs:
            row[i].fill = new_fill
        if row[status_col].value == PENDING:
            row[status_col].fill = pending_fill
            row[status_col].font = pending_font
    approve_letter = ws.cell(row=1, column=HEAD.index("Approve") + 1).column_letter
    dv = DataValidation(type="list", formula1='"yes"', allow_blank=True, showErrorMessage=False)
    dv.promptTitle = "Approve"
    dv.prompt = "Pick yes to approve this pending line as it stands (or write a NEW on-screen text, which approves it too)."
    dv.showInputMessage = True
    ws.add_data_validation(dv)
    dv.add(f"{approve_letter}2:{approve_letter}{max(2, len(data) + 1)}")
    ws.freeze_panes = "B2"
    ws.auto_filter.ref = ws.dimensions

    pend = {}
    for r in data:
        if r["status"] == PENDING:
            pend[r["mission"]] = pend.get(r["mission"], 0) + 1
    how = wb.create_sheet("How to use")
    lines = [
        "Monolith dialogue sheet.",
        "",
        "Write your replacement in the yellow NEW columns. Leave a NEW cell empty to keep the current line.",
        "NEW on-screen text: what appears on screen (radio captions, briefings, debriefs).",
        "NEW spoken text: what the voice says, if different from the on-screen text (leave empty to speak the on-screen text).",
        "Intensity: calm, tense or frantic sets how the voice is performed. You can change it in Notes (write: intensity: frantic).",
        "Rows marked 'Voiced now: yes' already have recordings; changing them means re-recording only those lines.",
        "",
        "STATUS and APPROVE. A line whose Status is 'pending approval' is a draft the mission's author wrote: it is not voiced, and it plays as on-screen text with the radio's squelch, until you approve it.",
        "To approve a pending line as it is, pick 'yes' in its Approve cell. Writing a NEW on-screen text approves it too. Approved lines are voiced next, and only those.",
        "Briefing pages, answers and debriefs have no flag of their own: they show their mission's status, and the mission is cleared when all its radio lines are approved.",
        "Call-outs are the lines a mission speaks from inside an event (Kind: Call-out); their Line ID is made from the event's name.",
        "",
        "No voice files are generated until you have approved the lines.",
        "Line ID ties each row back to its mission or radio file, so please don't edit that column.",
    ]
    if pend:
        lines += ["", "Waiting for your approval now: " + "; ".join(f"{m}: {n} lines" for m, n in sorted(pend.items())) + "."]
    for line in lines:
        how.append([line])
    how.column_dimensions["A"].width = 150
    wb.save(out_path)
    npend = sum(pend.values())
    print(f"{len(data)} lines ({npend} pending approval) -> {out_path}")


if __name__ == "__main__":
    main()
