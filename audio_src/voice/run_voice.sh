#!/usr/bin/env bash
# Run gen_voice.py inside WSL with Trap Door's Breeze venv (set up once by its tools/tts/01..03 scripts; we only read from it).
# Extra arguments pass through (--plan, --force, --only mercy, --limit 3, --fake-synth). HF_HUB_OFFLINE: no downloads at generation time.
export HF_HUB_OFFLINE=1
export PYTHONPATH="$HOME/breeze/breeze-tts"
cd "$HOME/breeze/breeze-tts"
exec "$HOME/breeze/.venv/bin/python" -u "$(dirname "$(readlink -f "$0")")/gen_voice.py" "$@"
