#!/usr/bin/env bash
# Run gen_voice.py inside WSL with Trap Door's Breeze venv (set up once by its tools/tts/01..03 scripts; we only read from it).
# Extra arguments pass through (--plan, --force, --only mercy, --limit 3, --fake-synth). HF_HUB_OFFLINE: no downloads at generation time.
export HF_HUB_OFFLINE=1
export PYTHONPATH="$HOME/breeze/breeze-tts"
cd "$HOME/breeze/breeze-tts"
here="$(dirname "$(readlink -f "$0")")"
# a CUDA context error (exit 3) is not recoverable in-process but the run is resumable: start again, up to six times
for attempt in 1 2 3 4 5 6; do
  "$HOME/breeze/.venv/bin/python" -u "$here/gen_voice.py" "$@"
  code=$?
  [ "$code" -ne 3 ] && exit "$code"
  echo "run_voice: CUDA error, restarting (attempt $attempt)"
  sleep 8
done
exit 3
