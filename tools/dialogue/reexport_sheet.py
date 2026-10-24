"""Re-export the dialogue sheet after an import and a voicing: the new current text in the first columns and the NEW columns cleared.

  python -I tools/dialogue/reexport_sheet.py

The sheet may be open in Excel on the user's side, which locks it. This writes the new sheet to a temp file first and replaces design/dialogue/monolith_dialogue.xlsx only if that file is not
locked (no ~$ lock file beside it, and it opens for writing); otherwise it says so, leaves the original alone, and keeps the new sheet at design/dialogue/monolith_dialogue.NEW.xlsx for whoever
can replace it. Exit code 2 means "locked".
"""
import os
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEST = os.path.join(ROOT, "design", "dialogue", "monolith_dialogue.xlsx")


def locked(path):
    d, n = os.path.split(path)
    if os.path.exists(os.path.join(d, "~$" + n)):
        return "a ~$ lock file is beside it (Excel has it open)"
    try:
        with open(path, "r+b"):
            pass
    except PermissionError:
        return "it will not open for writing (another program has it open)"
    return None


def main():
    tmp = os.path.join(tempfile.gettempdir(), "monolith_dialogue_reexport.xlsx")
    r = subprocess.run([sys.executable, "-I", os.path.join(HERE, "export_sheet.py"), tmp], capture_output=True, text=True)
    print(r.stdout.strip())
    if r.returncode != 0:
        print(r.stderr)
        return 1
    why = locked(DEST) if os.path.exists(DEST) else None
    if why:
        keep = os.path.join(os.path.dirname(DEST), "monolith_dialogue.NEW.xlsx")
        shutil.copyfile(tmp, keep)
        print(f"NOT replaced: {why}. The new sheet is {keep}; replace {os.path.basename(DEST)} with it when the sheet is closed.")
        return 2
    os.replace(tmp, DEST)
    print(f"replaced {DEST}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
