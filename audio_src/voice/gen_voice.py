"""Generate every radio line in voice_lines.json with Breeze-TTS-2 (Trap Door's pipeline, copied here; Trap Door itself is untouched).

Run inside WSL through run_voice.sh (extra arguments pass through):
  --plan           show what would be generated, reused or deleted; needs no GPU
  --force          regenerate everything
  --only mercy     only one voice
  --limit N        at most N new clips (a smoke test)
  --out DIR        raw clip folder (default audio_src/voice/raw, not committed)
  --fake-synth     no model: short tones (tests the re-run logic)

The raw clips are 24 kHz mono, dry, straight from the model. tools/audio (C#) turns them into radio: band-limiting, saturation, squelch,
the reverb of Earth's voices, the alien texture; then ffmpeg encodes OGG into Game/Assets/StreamingAssets/Audio/vo.

Voices are DESIGNED from the text descriptions in cast.json (voice design only: no real person is named or cloned). manifest.json (next
to the clips) records per clip the spoken text, the voice and a hash of its settings, so a re-run only generates new or changed lines.
Each line is generated as up to three takes (seeds 42, 43, 44); a take that is silent, clipped off at the length cap, or implausibly
slow or fast for its words is rejected and the next seed tried; the best of the takes is kept. The Breeze weights and everything made
with them are for non-commercial use only (see AUDIO_SOURCES.md).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
CAST = json.loads((HERE / 'cast.json').read_text(encoding='utf-8'))
LINES = HERE / 'voice_lines.json'
MAX_NEW_TOKENS = 1500
MAX_SEQ_LEN = 2048
REPETITION_PENALTY = 1.1
SEEDS = [42, 43, 44]
OOM_GIVE_UP_SECONDS = 30 * 60


def log(msg):
    print(f"[{time.strftime('%H:%M:%S')}] {msg}", flush=True)


def voice_hash(v):
    return hashlib.sha1(json.dumps({k: v[k] for k in ('instruction', 'cfg', 'seed')}, sort_keys=True).encode()).hexdigest()[:12]


class FakeSynth:
    def synth(self, voice, text, out, seed, max_seconds):
        import numpy as np
        import soundfile as sf
        sr = 24000
        dur = min(max_seconds, 0.3 + 0.12 * len(text.split()) + 0.1 * (seed % 3))
        t = np.arange(int(sr * dur)) / sr
        sf.write(out, (0.2 * np.sin(2 * np.pi * 150 * t)).astype('float32'), sr, subtype='PCM_16')
        return dur

    def reset(self):
        pass


class BreezeSynth:
    def __init__(self, model_dir):
        import torch
        self.torch = torch
        sys.path.insert(0, str(Path.home() / 'breeze/breeze-tts'))
        from breeze_infer.runtime import load_runtime, resolve_device, set_all_seeds, update_generation_config_for_breeze
        from breeze_infer.templates import get_template, prepare_inputs, select_template_name
        from models.fast_streaming import FastBreezeStreamingRuntime, FastStreamingConfig
        self._set_all_seeds, self._get_template = set_all_seeds, get_template
        self._prepare_inputs, self._select_template_name = prepare_inputs, select_template_name
        self._Runtime, self._Config = FastBreezeStreamingRuntime, FastStreamingConfig
        device = resolve_device()
        self.tokenizer, self.model, self.audio_tokenizer = retry_oom(
            'model load', lambda: load_runtime(model_dir, device=device, attn_implementation='eager'))
        update_generation_config_for_breeze(self.model)
        self.runtime = None
        self.reset()

    def reset(self):
        cfg = self._Config(max_new_tokens=MAX_NEW_TOKENS, max_seq_len=MAX_SEQ_LEN, fast_all=None, fast_text_encoder=False,
                           fast_backbone_prefill=False, fast_backbone_decode=False, fast_depth_decoder=False, fast_codec=False,
                           repetition_penalty=REPETITION_PENALTY)
        self.runtime = self._Runtime(self.model, self.audio_tokenizer, cfg, tokenizer=self.tokenizer)

    def synth(self, voice, text, out, seed, max_seconds):
        import soundfile as sf
        request = {'id': 'single-request', 'text': text, 'speaker': 'S0', 'instruction': voice['instruction']}
        template = self._select_template_name(request)
        self._set_all_seeds(seed)
        inputs = self._prepare_inputs(self.tokenizer, self.audio_tokenizer, self.model, [request], self._get_template(template),
                                      guidance_scale=voice['cfg'], guidance_scale_ref=None, guidance_scale_ins=None)
        n = 0
        with sf.SoundFile(out, mode='w', samplerate=self.runtime.sample_rate, channels=1, subtype='PCM_16') as f:
            gen = self.runtime.iter_audio_chunks(inputs, request_id='single-request', seed=seed)
            for chunk in gen:
                f.write(chunk.audio)
                n += len(chunk.audio)
                if max_seconds and n / self.runtime.sample_rate >= max_seconds:
                    gen.close()
                    break
        return n / self.runtime.sample_rate


def is_oom(e):
    return 'out of memory' in str(e).lower() or type(e).__name__ == 'OutOfMemoryError'


def retry_oom(what, fn, on_oom=None):
    """Another process (Unity) may be using the card: wait and retry instead of failing."""
    import gc
    t0, attempt = time.time(), 0
    while True:
        try:
            return fn()
        except Exception as e:  # noqa: BLE001
            if not is_oom(e):
                raise
            attempt += 1
            waited = time.time() - t0
            if waited > OOM_GIVE_UP_SECONDS:
                raise
            wait = min(15 * attempt, 90)
            log(f'CUDA out of memory during {what} (attempt {attempt}, waited {waited:.0f}s); waiting {wait}s then retrying')
            gc.collect()
            try:
                import torch
                torch.cuda.empty_cache()
            except Exception:  # noqa: BLE001
                pass
            time.sleep(wait)
            if on_oom:
                on_oom()


def speech_stats(path):
    """(seconds of speech between the first and last sound, total seconds, clipped fraction)"""
    import numpy as np
    import soundfile as sf
    x, sr = sf.read(path, dtype='float32')
    if x.ndim > 1:
        x = x.mean(axis=1)
    win = max(1, sr // 100)
    n = len(x) // win
    if n == 0:
        return 0.0, 0.0, 0.0
    r = np.sqrt((x[:n * win].reshape(n, win) ** 2).mean(axis=1))
    peak = float(np.max(np.abs(x))) or 1.0
    idx = np.nonzero(r > max(0.004, 0.03 * peak))[0]
    if len(idx) == 0:
        return 0.0, len(x) / sr, 0.0
    return float((idx[-1] - idx[0] + 1) * win / sr), float(len(x) / sr), float((np.abs(x) > 0.999).mean())


def budget(text):
    """(floor, accept, cap) seconds of speech for a line: these designed voices run about 0.35 to 0.7 s a word."""
    words = max(1, len(re.findall(r"[A-Za-z']+", text)))
    return 0.14 * words + 0.15, 0.85 * words + 1.0, 1.5 * (0.85 * words + 1.0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', type=Path, default=HERE / 'raw')
    ap.add_argument('--model', type=Path, default=Path.home() / 'breeze/breeze-tts-2')
    ap.add_argument('--only')
    ap.add_argument('--limit', type=int)
    ap.add_argument('--force', action='store_true')
    ap.add_argument('--plan', action='store_true')
    ap.add_argument('--fake-synth', action='store_true')
    args = ap.parse_args()

    out = args.out
    out.mkdir(parents=True, exist_ok=True)
    for left in list(out.glob('_take*.wav')):
        left.unlink()
    lines = json.loads(LINES.read_text(encoding='utf-8'))['lines']
    if args.only:
        lines = [l for l in lines if l['voice'] == args.only]
    mp = out / 'manifest.json'
    manifest = json.loads(mp.read_text(encoding='utf-8')) if mp.exists() else {'files': {}}
    vh = {k: voice_hash(v) for k, v in CAST['voices'].items()}

    todo, ok = [], 0
    for l in lines:
        e = manifest['files'].get(l['file'])
        if not args.force and e and (out / l['file']).exists() and e['say'] == l['say'] and e['voice_hash'] == vh[l['voice']]:
            ok += 1
        else:
            todo.append(l)
    stale = [f for f in manifest['files'] if f not in {l['file'] for l in json.loads(LINES.read_text(encoding='utf-8'))['lines']}]
    log(f'{len(lines)} clips wanted | up to date {ok} | to generate {len(todo)} | stale {len(stale)}')
    if args.plan:
        for l in todo:
            log(f"  GENERATE {l['file']}: {l['say']}")
        return 0
    if args.limit:
        todo = todo[:args.limit]

    synth = None
    if todo:
        log('fake synth' if args.fake_synth else 'loading model')
        synth = FakeSynth() if args.fake_synth else BreezeSynth(args.model)
    failures = []
    t0 = time.time()
    total_audio = 0.0
    for i, l in enumerate(todo, 1):
        voice = CAST['voices'][l['voice']]
        path = out / l['file']
        path.parent.mkdir(parents=True, exist_ok=True)
        floor, accept, cap = budget(l['say'])
        best = None
        t1 = time.time()
        try:
            for seed in SEEDS:
                take = path.parent / f'_take{seed}_{path.name}'
                retry_oom(l['file'], lambda: synth.synth(voice, l['say'], take, seed + (voice['seed'] - 42), cap), on_oom=synth.reset)
                speech, total, clipped = speech_stats(take)
                good = floor <= speech <= accept and clipped < 0.002 and total < cap - 0.05
                log(f"    {l['file']} seed {seed + voice['seed'] - 42}: {speech:.2f}s speech of {total:.2f}s (budget {floor:.2f}-{accept:.2f}, cap {cap:.2f}) {'ok' if good else 'REJECT'}")
                score = 0.0 if good else 1.0 + abs(speech - (floor + accept) / 2)
                if best is None or score < best[0]:
                    if best is not None:
                        best[2].unlink(missing_ok=True)
                    best = (score, seed, take, speech, total)
                else:
                    take.unlink(missing_ok=True)
                if good:
                    break
            best[2].replace(path)
        except Exception as e:  # noqa: BLE001
            failures.append((l['file'], f'{type(e).__name__}: {str(e)[:200]}'))
            log(f"FAILED {l['file']}: {type(e).__name__}: {str(e)[:200]}")
            continue
        total_audio += best[4]
        manifest['files'][l['file']] = {'voice': l['voice'], 'say': l['say'], 'voice_hash': vh[l['voice']], 'seed': best[1],
                                        'speech_seconds': round(best[3], 2), 'seconds': round(best[4], 2), 'plausible': bool(best[0] == 0)}
        mp.write_text(json.dumps(manifest, indent=1, sort_keys=True), encoding='utf-8')
        log(f"[{i}/{len(todo)}] {l['file']} {time.time() - t1:.1f}s for {best[4]:.1f}s | {l['say']}")
    for f in stale:
        (out / f).unlink(missing_ok=True)
        manifest['files'].pop(f, None)
    mp.write_text(json.dumps(manifest, indent=1, sort_keys=True), encoding='utf-8')
    log(f'DONE in {(time.time() - t0) / 60:.1f} min: {len(todo) - len(failures)} generated ({total_audio:.0f}s audio), {len(failures)} failed')
    for f, why in failures:
        log(f'  failed: {f}: {why}')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
