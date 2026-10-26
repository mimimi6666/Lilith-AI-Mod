"""Optional local Japanese cue bank; native cue first, synthesized body second.

No model text selects paths, enables the bank, or changes the game speech queue.
Missing/disabled banks leave the original pipeline.run object untouched.
"""
import hashlib
import io
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import stat
import wave

import numpy as np
from scipy.signal import resample_poly

MANIFEST_NAME = "japanese-interjections.v1.json"
RATE = 48000
MAX_MANIFEST = 65536
MAX_ASSET = 1200000
MODEL_KEYS = ("t2s_weights_path", "vits_weights_path")
CATEGORIES = {
    "laugh_fufu": "laughter", "laugh_ehehe": "laughter", "reveal": "reveal",
    "exhale_soft": "exhale", "exhale_long": "exhale", "inhale": "inhale",
    "breath_cycle": "inhale", "response": "response", "surprise": "surprise",
}
CUE_EMOTIONS = {
    "laugh_fufu": "playful", "laugh_ehehe": "affectionate", "reveal": "excited",
    "exhale_soft": "relieved", "exhale_long": "calm", "inhale": "calm",
    "breath_cycle": "calm", "response": "gentle", "surprise": "surprised",
}
EMOTION_CUES = {"playful": "laugh_fufu", "affectionate": "laugh_ehehe", "excited": "reveal",
                "relieved": "exhale_soft", "calm": "exhale_long", "gentle": "response", "surprised": "surprise"}
# Require a real boundary: えっと, んーと and quoted descriptions are not effects.
SEP = r"[\s、。，,.!！?？…⋯~〜～ー―—]"
PATTERNS = [
    ("breath_cycle", re.compile(r"^す[ぅう]" + SEP + r"*は[ぁあ](?=" + SEP + r"|$)")),
    ("laugh_ehehe", re.compile(r"^えへへっ?(?=" + SEP + r"|$)")),
    ("laugh_fufu", re.compile(r"^ふふっ?(?=" + SEP + r"|$)")),
    ("reveal", re.compile(r"^(?:ジャジャ|じゃじゃ)[ー~〜～―—]{0,3}[ンん](?=" + SEP + r"|$)")),
    ("exhale_soft", re.compile(r"^ふ[ぅう](?=" + SEP + r"|$)")),
    ("exhale_long", re.compile(r"^は[ぁあ](?=" + SEP + r"|$)")),
    ("inhale", re.compile(r"^す[ぅう](?=" + SEP + r"|$)")),
    ("response", re.compile(r"^ん(?=" + SEP + r"|$)")),
    ("surprise", re.compile(r"^えっ(?=" + SEP + r"|$)")),
]
SEPARATORS = re.compile(r"^" + SEP + r"*")


class CueBankError(RuntimeError):
    """Errors contain fixed diagnostic codes, never text or local paths."""


def _require(condition, code):
    if not condition:
        raise CueBankError(code)


def _object(pairs):
    result = {}
    for key, value in pairs:
        _require(key not in result, "duplicate_bank_key")
        result[key] = value
    return result


def _number(value, lower, upper):
    return type(value) in (float, int) and lower <= value <= upper and math.isfinite(value)


def _regular(info):
    return stat.S_ISREG(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 0x400


def _directory(path):
    info = path.lstat()
    _require(stat.S_ISDIR(info.st_mode) and not getattr(info, "st_file_attributes", 0) & 0x400,
             "unsafe_bank_directory")


def _relative(root, name):
    _require(isinstance(name, str) and 0 < len(name) <= 160 and
             re.fullmatch(r"[A-Za-z0-9_./-]+", name) is not None, "invalid_bank_path")
    parts = name.split("/")
    _require(all(part not in ("", ".", "..") for part in parts) and not PurePosixPath(name).is_absolute(),
             "invalid_bank_path")
    _directory(root)
    for index in range(1, len(parts)):
        _directory(root.joinpath(*parts[:index]))
    return root.joinpath(*parts)


def _read(path, limit):
    before = path.lstat()
    _require(_regular(before) and before.st_size <= limit, "unsafe_or_oversized_bank_file")
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    with os.fdopen(os.open(path, flags), "rb") as stream:
        opened = os.fstat(stream.fileno())
        current = path.lstat()
        _require(_regular(opened) and _regular(current) and
                 (before.st_dev, before.st_ino) == (opened.st_dev, opened.st_ino) ==
                 (current.st_dev, current.st_ino), "bank_file_changed_during_open")
        data = stream.read(limit + 1)
    _require(len(data) <= limit and len(data) == opened.st_size, "bank_file_size_changed")
    return data


def _digest(value):
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def _audio(root, record, low_seconds, high_seconds):
    _require(type(record) is dict and set(record) == {"file", "sha256"} and
             _digest(record["sha256"]), "invalid_bank_audio_record")
    path = _relative(root, record["file"])
    _require(path.suffix == ".wav", "invalid_bank_audio_extension")
    data = _read(path, MAX_ASSET)
    _require(hashlib.sha256(data).hexdigest() == record["sha256"], "bank_audio_hash_mismatch")
    try:
        with wave.open(io.BytesIO(data), "rb") as stream:
            _require(stream.getnchannels() == 1 and stream.getsampwidth() == 2 and
                     stream.getframerate() == RATE and stream.getcomptype() == "NONE",
                     "unsupported_bank_audio")
            frames = stream.getnframes()
            _require(low_seconds <= frames / RATE <= high_seconds, "bank_audio_duration")
            pcm = stream.readframes(frames)
            _require(len(pcm) == frames * 2, "truncated_bank_audio")
    except (wave.Error, EOFError) as error:
        raise CueBankError("invalid_bank_wave") from error
    result = np.frombuffer(pcm, dtype="<i2").copy()
    _require(np.any(result) and np.max(np.abs(result.astype(np.int32))) < 32767,
             "silent_or_clipped_bank_audio")
    result.setflags(write=False)
    return path, result


def _stamp(path):
    info = path.stat()
    return (str(path.resolve()), info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns)


def _hash_model(path):
    _require(_regular(path.lstat()), "unsafe_cue_model")
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


class CueBank:
    def __init__(self, root, value, configs):
        self.root = root
        _require(getattr(configs, "version", None) == "v2" and getattr(configs, "is_half", None) is False,
                 "cue_model_profile_mismatch")
        self.model_stamps = {}
        for key in MODEL_KEYS:
            path = Path(getattr(configs, key, ""))
            before = _stamp(path)
            _require(_hash_model(path) == value["model_sha256"][key], "cue_model_mismatch")
            _require(before == _stamp(path), "cue_model_changed_during_load")
            self.model_stamps[key] = before
        self.entries = {}
        for item in value["entries"]:
            _require(type(item) is dict and set(item) == {
                "cue", "category", "emotion", "clip", "reference", "reference_text", "native_gain_db"},
                "invalid_cue_entry")
            cue = item["cue"]
            _require(isinstance(cue, str) and cue in CATEGORIES and cue not in self.entries and
                     item["category"] == CATEGORIES[cue], "invalid_or_duplicate_cue")
            _require(item["emotion"] == CUE_EMOTIONS[cue],
                     "invalid_cue_emotion")
            _require(isinstance(item["reference_text"], str) and 1 <= len(item["reference_text"]) <= 256 and
                     not any(ord(c) < 32 for c in item["reference_text"]), "invalid_reference_text")
            _require(_number(item["native_gain_db"], -35, 12), "invalid_native_gain")
            _, pcm = _audio(root, item["clip"], .1, 5)
            reference, _ = _audio(root, item["reference"], 3, 10)
            native = np.rint(pcm.astype(np.float64) * 10**(item["native_gain_db"] / 20))
            _require(np.max(np.abs(native)) <= 32768 * 10**(-1/20), "native_gain_clips")
            # A quiet actual cut, not a forced fade or deletion of the vocal tail.
            _require(np.max(np.abs(native[-round(.005 * RATE):])) / 32768 <= .004,
                     "native_cut_not_quiet")
            native = native.astype(np.int16)
            native.setflags(write=False)
            self.entries[cue] = {**item, "pcm": native, "reference_path": str(reference)}

    def models_current(self, configs):
        try:
            return getattr(configs, "version", None) == "v2" and getattr(configs, "is_half", None) is False and all(_stamp(Path(getattr(configs, key, ""))) == value
                       for key, value in self.model_stamps.items())
        except (OSError, ValueError, TypeError):
            return False

    def reference_current(self, entry):
        path = _relative(self.root, entry["reference"]["file"])
        return hashlib.sha256(_read(path, MAX_ASSET)).hexdigest() == entry["reference"]["sha256"]


def load_bank(manifest, configs):
    """None means off. Malformed opt-in data raises a code for visible disablement."""
    manifest = Path(manifest)
    try:
        _directory(manifest.parent)
        data = _read(manifest, MAX_MANIFEST)
    except FileNotFoundError:
        return None
    try:
        value = json.loads(data.decode("utf-8-sig"), object_pairs_hook=_object,
                           parse_constant=lambda _: (_ for _ in ()).throw(CueBankError("nonfinite_bank_value")))
        _require(type(value) is dict and set(value) == {"schema_version", "enabled", "locale", "model_sha256", "entries"},
                 "invalid_bank_schema")
        _require(type(value["schema_version"]) is int and value["schema_version"] == 1 and
                 type(value["enabled"]) is bool and value["locale"] == "ja", "invalid_bank_version_or_locale")
        if not value["enabled"]:
            return None
        _require(type(value["model_sha256"]) is dict and set(value["model_sha256"]) == set(MODEL_KEYS) and
                 all(_digest(d) for d in value["model_sha256"].values()), "invalid_cue_model_identity")
        _require(type(value["entries"]) is list and 1 <= len(value["entries"]) <= len(CATEGORIES), "invalid_cue_count")
        return CueBank(manifest.parent, value, configs)
    except CueBankError:
        raise
    except (OSError, ValueError, TypeError, KeyError, RecursionError) as error:
        raise CueBankError("invalid_or_unreadable_cue_bank") from error


def leading_cue(text):
    if not isinstance(text, str) or not 1 <= len(text) <= 4000:
        return None
    text = text.lstrip()
    for cue, pattern in PATTERNS:
        match = pattern.match(text)
        if match:
            body = SEPARATORS.sub("", text[match.end():], count=1)
            # Avoid turning thinking phrases or talk about a sound into a cue.
            if cue == "response" and (text[match.end():].startswith(("ー", "〜", "~", "～")) or body.startswith("と")):
                return None
            if body.startswith(("とは", "という", "っていう")):
                return None
            if any(other.match(body) for _, other in PATTERNS):
                return None  # Multiple independent cues need a separate utterance plan.
            return cue, body
    return None


def _active_rms(audio):
    frame = round(RATE * .02)
    blocks = audio[:len(audio)//frame*frame].reshape(-1, frame)
    _require(len(blocks) > 0, "empty_generated_body")
    levels = np.sqrt(np.mean(blocks * blocks, axis=1))
    selected = levels >= max(.001, float(levels.max()) * 10**(-35/20))
    _require(selected.any(), "silent_generated_body")
    return float(np.sqrt(np.mean(blocks[selected]**2)))


def assemble(entry, raw_rate, raw_pcm):
    _require(type(raw_rate) is int and 8000 <= raw_rate <= 96000 and
             isinstance(raw_pcm, np.ndarray) and raw_pcm.ndim == 1 and raw_pcm.dtype == np.int16 and
             0 < len(raw_pcm) <= raw_rate * 90, "invalid_generated_body")
    body = raw_pcm.astype(np.float64) / 32768
    if raw_rate != RATE:
        divisor = math.gcd(raw_rate, RATE)
        body = resample_poly(body, RATE // divisor, raw_rate // divisor)
    gain = min(10**(-23/20) / _active_rms(body), 10**(-1/20) / float(np.max(np.abs(body))))
    body_pcm = np.rint(body * gain * 32768).astype(np.int16)
    prefix = entry["pcm"]
    jump = abs(int(prefix[-1]) - int(body_pcm[0])) / 32768
    _require(jump <= .004, "generated_body_join_not_quiet")
    return np.concatenate([prefix, body_pcm]), {
        "cue": entry["cue"], "prefix_samples": len(prefix), "body_samples": len(body_pcm),
        "body_gain_db": float(20*np.log10(gain)), "sample_rate": RATE,
        "trim_samples": 0, "overlap_samples": 0, "fade_samples": 0,
    }


def install(pipeline, bank, report=None):
    if bank is None:
        return {"enabled": False, "reason": "not_configured"}
    _require(not getattr(pipeline, "_lilith_japanese_interjections_installed", False), "cue_adapter_already_installed")
    original_run = pipeline.run

    def emit(value):
        if report is not None:
            try:
                report(value)
            except Exception:
                pass  # Diagnostics do not alter synthesis outcome.

    def body_request(entry, text, inputs):
        request = {key: value for key, value in inputs.items()
                   if key not in ("lilith_emotion", "lilith_reference_cue", "lilith_allow_native_cue")}
        request.update(text=text, ref_audio_path=entry["reference_path"], prompt_text=entry["reference_text"],
                       prompt_lang="ja", aux_ref_audio_paths=[], text_split_method="cut0", batch_size=1,
                       seed=43, top_k=5, top_p=1.0, temperature=1.0, repetition_penalty=1.35,
                       speed_factor=1.0, parallel_infer=True)
        return request

    def hybrid(entry, body, inputs, args, kwargs):
        if not body:
            # Mirror upstream's reset at the start of a new request; an old stop
            # must not permanently latch future utterances off.
            pipeline.stop_flag = False
            emit({"cue": entry["cue"], "native_only": True, "prefix_samples": len(entry["pcm"])})
            yield RATE, entry["pcm"].copy()
            return
        request = body_request(entry, body, inputs)
        generator = iter(original_run(request, *args, **kwargs))
        try:
            first = next(generator, None)
            _require(first is not None, "missing_generated_body")
            _require(next(generator, None) is None, "unexpected_generated_fragments")
        finally:
            close = getattr(generator, "close", None)
            if close:
                close()
        # No native prefix can escape before body success/cancellation is known.
        if getattr(pipeline, "stop_flag", False):
            return
        audio, receipt = assemble(entry, *first)
        if getattr(pipeline, "stop_flag", False):
            return
        emit(receipt)
        yield RATE, audio

    def run(inputs, *args, **kwargs):
        if (not isinstance(inputs, dict) or inputs.get("text_lang") not in ("ja", "all_ja") or
                any(inputs.get(key, False) for key in ("streaming_mode", "return_fragment", "fixed_length_chunk"))):
            return original_run(inputs, *args, **kwargs)
        match = leading_cue(inputs.get("text")) if inputs.get("lilith_allow_native_cue", True) is True else None
        hint = inputs.get("lilith_emotion", "")
        inherited = inputs.get("lilith_reference_cue", "")
        if not isinstance(hint, str) or not isinstance(inherited, str):
            return original_run(inputs, *args, **kwargs)
        if match:
            cue = match[0]  # Actual vocal text wins over conflicting advisory metadata.
        elif inherited in CUE_EMOTIONS and CUE_EMOTIONS[inherited] == hint:
            cue = inherited
        elif inherited == "" and hint in EMOTION_CUES:
            cue = EMOTION_CUES[hint]
        else:
            return original_run(inputs, *args, **kwargs)
        if cue not in bank.entries:
            return original_run(inputs, *args, **kwargs)
        entry = bank.entries[cue]
        try:
            _require(bank.models_current(pipeline.configs), "cue_models_changed")
            _require(bank.reference_current(entry), "cue_reference_changed")
        except (CueBankError, OSError, ValueError, TypeError):
            emit({"enabled_for_request": False, "reason": "cue_inputs_changed"})
            return original_run(inputs, *args, **kwargs)  # Original full text, not stripped.
        emit({"delivery_selected": True, "emotion": entry["emotion"], "reference_cue": cue,
              "source": "explicit_cue" if match else "cue_continuation" if inherited else "body_hint"})
        if match:
            return hybrid(entry, match[1], inputs, args, kwargs)
        # No native sound is inserted for a delivery hint or continuation. Never
        # keep current emotion on pipeline: every request carries its own bound choice.
        return original_run(body_request(entry, inputs.get("text", ""), inputs), *args, **kwargs)

    pipeline.run = run
    pipeline._lilith_japanese_interjections_installed = True
    return {"enabled": True, "cue_count": len(bank.entries), "locale": "ja"}
