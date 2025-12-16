import re
import sys
import time
import subprocess
from pathlib import Path

import torch
import soundfile as sf
import imageio_ffmpeg
from transformers import pipeline


# -----------------------------
# Clip-slot behavior (your requirements)
# -----------------------------

MAX_CLIP_SEC = 7.0               # 7-second clip slots (last slot may be shorter)
LANGUAGE = "en"                  # set to None for auto-detect

# Important: helps prevent "last few words" being cut off due to Whisper timestamps ending early
SEG_END_PAD_SEC = 0.35           # pad segment end by ~350ms when mapping text -> slots

# If your downstream tool ignores empty subtitles / ends early, keep this non-empty.
# If you truly want blank lines, set EMPTY_SLOT_TEXT = "" (but some tools treat that as "missing").
EMPTY_SLOT_TEXT = "[no narration]"


# -----------------------------
# Utilities
# -----------------------------

def seconds_to_srt(ts: float) -> str:
    h = int(ts // 3600)
    m = int((ts % 3600) // 60)
    s = int(ts % 60)
    ms = int(round((ts - int(ts)) * 1000))
    if ms == 1000:
        s += 1
        ms = 0
    return f"{h:02}:{m:02}:{s:02},{ms:03}"


def clean_text(text: str) -> str:
    text = text.strip()
    text = re.sub(r"\s+", " ", text)
    text = re.sub(r"\s+([,.!?])", r"\1", text)
    return text


def word_tokenize(text: str) -> list[str]:
    return re.findall(r"\S+", text.strip())


def allocate_words_by_overlaps(words: list[str], overlaps: list[float]) -> list[list[str]]:
    """
    Split a list of words into N sequential chunks, proportionally to overlaps.
    Preserves word order. Never drops words: the last chunk gets the remainder.
    """
    n = len(overlaps)
    if n == 0:
        return []
    if not words:
        return [[] for _ in range(n)]

    total = sum(overlaps)
    if total <= 0:
        out = [[] for _ in range(n)]
        out[-1] = words[:]  # shove everything into the last slot if overlaps are weird
        return out

    # initial target counts via proportional rounding
    targets = [int(round(len(words) * (ov / total))) for ov in overlaps]

    # adjust so total matches exactly
    diff = len(words) - sum(targets)
    order = sorted(range(n), key=lambda i: overlaps[i], reverse=True)
    idx = 0
    while diff != 0 and n > 0:
        i = order[idx % n]
        if diff > 0:
            targets[i] += 1
            diff -= 1
        else:
            if targets[i] > 0:
                targets[i] -= 1
                diff += 1
        idx += 1

    # allocate sequentially; last slot always gets the remainder to guarantee no loss
    out: list[list[str]] = []
    pos = 0
    for i in range(n):
        if i == n - 1:
            chunk = words[pos:]
        else:
            take = targets[i]
            chunk = words[pos:pos + take]
            pos += take
        out.append(chunk)

    return out


# -----------------------------
# Audio conversion (any input -> 16k mono wav)
# -----------------------------

def convert_to_wav_16k_mono(input_path: Path, output_wav: Path) -> None:
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    cmd = [
        ffmpeg,
        "-y",
        "-i", str(input_path),
        "-ac", "1",
        "-ar", "16000",
        "-vn",
        str(output_wav),
    ]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError("ffmpeg conversion failed.\n" f"STDERR:\n{proc.stderr}\n")


def load_audio_16k(wav_path: Path):
    audio, sr = sf.read(str(wav_path), dtype="float32", always_2d=False)
    if sr != 16000:
        raise ValueError(f"Expected 16kHz WAV, got {sr} Hz")
    if hasattr(audio, "ndim") and audio.ndim > 1:
        audio = audio[:, 0]
    return audio, sr


# -----------------------------
# Build continuous 7s slots and pour transcript into them
# -----------------------------

def build_slots(audio_duration_sec: float, slot_sec: float):
    slots = []
    t = 0.0
    while t < audio_duration_sec - 1e-6:
        end = min(t + slot_sec, audio_duration_sec)
        slots.append({"start": t, "end": end, "texts": []})
        t = end
    return slots


def build_srt_slots_from_segments(segments, audio_duration_sec: float, slot_sec: float, seg_end_pad_sec: float):
    """
    - Creates slots: 0-7, 7-14, ...
    - Assigns text to slots based on timestamp overlap
    - Pads segment end times slightly so trailing words don't get cut off
    - Appends any untimed tail text to the final slot
    """
    slots = build_slots(audio_duration_sec, slot_sec)
    if not slots:
        return slots

    untimed_texts = []

    for seg in segments:
        ts = seg.get("timestamp", None)
        text = clean_text(seg.get("text", ""))

        if not text:
            continue

        if not ts or ts[0] is None or ts[1] is None:
            # Some models produce tail text with missing timestamps.
            untimed_texts.append(text)
            continue

        s0 = float(ts[0])
        s1 = float(ts[1])

        if s1 <= s0:
            untimed_texts.append(text)
            continue

        # Pad end slightly (but never past audio end)
        s1 = min(s1 + seg_end_pad_sec, audio_duration_sec)

        # Compute overlapping slot indices
        i0 = int(max(0.0, s0) // slot_sec)
        i1 = int(max(0.0, (s1 - 1e-6)) // slot_sec)
        i0 = max(0, min(i0, len(slots) - 1))
        i1 = max(0, min(i1, len(slots) - 1))

        slot_indices = list(range(i0, i1 + 1))
        overlaps = []
        for i in slot_indices:
            a = max(s0, slots[i]["start"])
            b = min(s1, slots[i]["end"])
            overlaps.append(max(0.0, b - a))

        words = word_tokenize(text)
        chunks = allocate_words_by_overlaps(words, overlaps)

        for i, chunk in zip(slot_indices, chunks):
            if chunk:
                slots[i]["texts"].append(" ".join(chunk))

    # Guarantee we never lose tail words: if we collected untimed text, append to last slot
    if untimed_texts:
        slots[-1]["texts"].append(" ".join(untimed_texts))

    return slots


# -----------------------------
# Keyword Generator (simple, broad, stock-footage-friendly)
# -----------------------------

def generate_keywords(text):
    t = text.lower()
    kws = []

    def add(*items):
        for x in items:
            if x not in kws:
                kws.append(x)

    if any(w in t for w in ["oil", "tanker", "pipeline", "barrel", "refinery"]):
        add("oil tanker at sea", "cargo ship ocean", "energy supply chain", "global shipping trade")

    if any(w in t for w in ["military", "missile", "jet", "airstrike", "troops"]):
        add("fighter jets in sky", "military aircraft runway", "soldiers training", "defense forces visuals")

    if any(w in t for w in ["economy", "inflation", "prices", "recession", "market"]):
        add("economic uncertainty", "financial charts abstract", "cost of living visuals", "city business district")

    if any(w in t for w in ["government", "sanctions", "policy", "law", "officials"]):
        add("government building exterior", "international relations visuals", "press conference podium", "capitol city skyline")

    if not kws:
        add("world news visuals", "serious documentary b-roll", "global affairs imagery", "city skyline night")

    return kws[:5]


# -----------------------------
# Main
# -----------------------------

def main():
    if len(sys.argv) < 2:
        print("Usage: pipeline.py <audio_file>")
        sys.exit(1)

    start_all = time.time()
    input_audio = Path(sys.argv[1]).resolve()

    base = input_audio.with_suffix("")
    out_srt = base.parent / f"{base.name}.srt"
    out_kw = base.parent / f"{base.name}_keywords.txt"
    tmp_wav = base.parent / f"{base.name}__tmp16k.wav"

    print("=" * 60)
    print(f"Input: {input_audio}")
    print(f"Python: {sys.version.split()[0]}")
    print(f"Torch: {torch.__version__}")
    print(f"CUDA available: {torch.cuda.is_available()}")
    if torch.cuda.is_available():
        print(f"GPU: {torch.cuda.get_device_name(0)}")
    print(f"Slot length: {MAX_CLIP_SEC:.2f}s (fixed)")
    print(f"Segment end pad: {SEG_END_PAD_SEC:.2f}s (prevents tail cut-offs)")
    print("=" * 60)

    # 1) Convert
    print("[1/4] Converting audio to 16kHz mono WAV...")
    t0 = time.time()
    convert_to_wav_16k_mono(input_audio, tmp_wav)
    print(f"    Done in {int(time.time() - t0)}s -> {tmp_wav.name}")

    # 2) Load audio
    print("[2/4] Loading WAV into memory...")
    t1 = time.time()
    audio, sr = load_audio_16k(tmp_wav)
    audio_duration = len(audio) / sr
    print(f"    Loaded {audio_duration:.3f}s at {sr} Hz in {int(time.time() - t1)}s")

    # 3) ASR (GPU if available)
    print("[3/4] Loading transcription model...")
    t2 = time.time()
    device = 0 if torch.cuda.is_available() else -1
    dtype = torch.float16 if torch.cuda.is_available() else torch.float32

    asr = pipeline(
        "automatic-speech-recognition",
        model="openai/whisper-large-v3",
        device=device,
        dtype=dtype,
        return_timestamps=True,
    )
    print(f"    Model ready in {int(time.time() - t2)}s (device={'cuda:0' if device == 0 else 'cpu'})")

    print("    Transcribing...")
    t3 = time.time()
    kwargs = {}
    if LANGUAGE:
        kwargs = {"generate_kwargs": {"language": LANGUAGE}}
    result = asr({"raw": audio, "sampling_rate": sr}, **kwargs)
    print(f"    Transcribed in {int(time.time() - t3)}s")

    # Build segments
    segments = []
    for ch in result.get("chunks", []):
        ts = ch.get("timestamp", None)
        segments.append({"timestamp": ts, "text": ch.get("text", "")})

    # 4) Build slots and write outputs
    print("[4/4] Building SRT + keyword prompts...")
    slots = build_srt_slots_from_segments(segments, audio_duration, MAX_CLIP_SEC, SEG_END_PAD_SEC)
    print(f"    Slots: {len(slots)} (covers 0.000s -> {audio_duration:.3f}s)")

    # Write SRT
    with open(out_srt, "w", encoding="utf-8") as f:
        for i, slot in enumerate(slots, 1):
            start = float(slot["start"])
            end = float(slot["end"])
            text = clean_text(" ".join(slot["texts"]))

            if not text:
                text = EMPTY_SLOT_TEXT

            f.write(f"{i}\n")
            f.write(f"{seconds_to_srt(start)} --> {seconds_to_srt(end)}\n")
            f.write(text + "\n\n")

    # Write keywords (always write something so block count stays aligned)
    with open(out_kw, "w", encoding="utf-8") as f:
        for i, slot in enumerate(slots, 1):
            text = clean_text(" ".join(slot["texts"]))
            f.write(f"[Block {i}]\n")
            if text:
                for k in generate_keywords(text):
                    f.write(k + "\n")
            else:
                # fallback keywords for silence
                f.write("ambient b-roll\n")
                f.write("city skyline\n")
                f.write("hands typing\n")
                f.write("soft abstract background\n")
                f.write("nature scenery\n")
            f.write("\n")

    # Cleanup
    try:
        tmp_wav.unlink(missing_ok=True)
    except Exception:
        pass

    print("=" * 60)
    print("DONE")
    print(f"Outputs:\n- {out_srt.name}\n- {out_kw.name}")
    print(f"Total time: {int(time.time() - start_all)}s")
    print("=" * 60)


if __name__ == "__main__":
    main()
