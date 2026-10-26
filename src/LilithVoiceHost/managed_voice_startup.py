"""Owned VoiceHost entrypoint; embedded in the host, not a model-callable tool."""
import copy
import contextvars
import json
import math
import os
from pathlib import Path
import runpy
import stat
import sys
import threading


class VoiceDeviceError(RuntimeError):
    pass


MAX_DELIVERY_BYTES = 4096


def _delivery_object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise VoiceDeviceError("duplicate_speech_delivery_key")
        value[key] = item
    return value


def _reject_delivery_constant(_value):
    raise VoiceDeviceError("nonfinite_speech_delivery_value")


def _validate_delivery_speed(value):
    if (type(value) not in (int, float) or not 1.0 <= value <= 1.2
            or not math.isfinite(value)):
        raise VoiceDeviceError("invalid_chinese_speech_speed")
    return float(value)


def _regular_delivery_file(info):
    return (stat.S_ISREG(info.st_mode)
            and not getattr(info, "st_file_attributes", 0) & 0x400)


def read_delivery_speed(source, port):
    # Japanese must not even read an unrelated, possibly broken Chinese file.
    if source.stem in ("ja-cuda", "ja-cpu"):
        return 1.0
    if source.stem not in ("zh-cuda", "zh-cpu"):
        raise VoiceDeviceError("invalid_managed_voice_profile")
    path = source.with_name("speech-delivery.v1.json")
    try:
        before = path.lstat()
    except FileNotFoundError:
        return 1.0
    except OSError as error:
        raise VoiceDeviceError("unreadable_speech_delivery") from error
    if not _regular_delivery_file(before):
        raise VoiceDeviceError("unsafe_speech_delivery_file")
    if before.st_size > MAX_DELIVERY_BYTES:
        raise VoiceDeviceError("speech_delivery_too_large")
    try:
        flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
        with os.fdopen(os.open(path, flags), "rb") as stream:
            opened = os.fstat(stream.fileno())
            current = path.lstat()
            if (not _regular_delivery_file(opened)
                    or not _regular_delivery_file(current)
                    or (opened.st_dev, opened.st_ino) != (before.st_dev, before.st_ino)
                    or (current.st_dev, current.st_ino) != (opened.st_dev, opened.st_ino)):
                raise VoiceDeviceError("unsafe_speech_delivery_file")
            data = stream.read(MAX_DELIVERY_BYTES + 1)
        if len(data) > MAX_DELIVERY_BYTES:
            raise VoiceDeviceError("speech_delivery_too_large")
        value = json.loads(data.decode("utf-8-sig"), object_pairs_hook=_delivery_object,
                           parse_constant=_reject_delivery_constant)
    except (OSError, ValueError, RecursionError) as error:
        raise VoiceDeviceError("invalid_speech_delivery_file") from error
    if (type(value) is not dict
            or set(value) != {"schema_version", "chinese_speed_factor"}
            or type(value["schema_version"]) is not int or value["schema_version"] != 1):
        raise VoiceDeviceError("invalid_speech_delivery_schema")
    speed = _validate_delivery_speed(value["chinese_speed_factor"])
    if speed != 1.0 and int(port) != 9880:
        raise VoiceDeviceError("chinese_speech_speed_requires_owned_port")
    return speed


def install_chinese_delivery(pipeline, speed):
    speed = _validate_delivery_speed(speed)
    if speed == 1.0:
        return
    original_run = pipeline.run

    def run_with_delivery(inputs, *args, **kwargs):
        if isinstance(inputs, dict) and inputs.get("text_lang") in ("zh", "all_zh"):
            # Assign the owner's rate once; callers and later generations never
            # inherit a mutated dictionary or multiply a previous rate.
            inputs = dict(inputs)
            inputs["speed_factor"] = speed
        return original_run(inputs, *args, **kwargs)

    pipeline.run = run_with_delivery


def install_optional_japanese_cues(pipeline, source):
    # No bank is shipped/enabled by this source change. Chinese never probes it.
    if source.stem not in ("ja-cuda", "ja-cpu"):
        return None
    manifest = source.with_name("japanese-interjections.v1.json")
    if not manifest.exists():
        return {"enabled": False, "reason": "not_configured"}
    module_path = source.parent.parent / "japanese_interjections.py"
    try:
        if not _regular_delivery_file(module_path.lstat()):
            raise VoiceDeviceError("unsafe_japanese_cue_module")
        module = runpy.run_path(str(module_path), run_name="lilith_japanese_interjections")
        bank = module["load_bank"](manifest, pipeline.configs)
        return module["install"](pipeline, bank, lambda receipt: print(
            "LILITH_VOICE_CUE " + json.dumps(receipt, ensure_ascii=True), flush=True))
    except Exception:
        # Keep full-text ordinary synthesis available; never silently strip a cue
        # when an optional bank or its model identity cannot be validated.
        return {"enabled": False, "reason": "bank_unavailable_or_invalid"}


def install_japanese_delivery_endpoint(state):
    """Owned extension, leaving bundled /tts and non-Japanese services unchanged."""
    from pydantic import create_model, StrictBool
    from typing import Literal
    request_type = create_model(
        "LilithJapaneseDeliveryRequest",
        __base__=state["TTS_Request"],
        lilith_emotion=(Literal["", "calm", "gentle", "playful", "affectionate", "excited", "relieved", "surprised"], ""),
        lilith_reference_cue=(Literal["", "laugh_fufu", "laugh_ehehe", "reveal", "exhale_soft", "exhale_long",
                                     "inhale", "response", "surprise", "breath_cycle"], ""),
        lilith_allow_native_cue=(StrictBool, True),
    )

    async def japanese_delivery(request):
        payload = request.dict()
        if payload.get("text_lang") not in ("ja", "all_ja"):
            from fastapi import HTTPException
            raise HTTPException(status_code=422, detail="Japanese delivery requires Japanese speech")
        return await state["tts_handle"](payload)

    japanese_delivery.__annotations__["request"] = request_type
    state["APP"].add_api_route("/lilith/tts", japanese_delivery, methods=["POST"])


def select_device(torch, requested, half):
    if requested not in ("auto", "cuda", "cpu"):
        raise VoiceDeviceError("invalid_device_preference")
    if requested == "cpu":
        return "cpu", "explicit_cpu"
    if not torch.cuda.is_available():
        if requested == "cuda":
            raise VoiceDeviceError("requested_cuda_unavailable; repair GPU runtime or explicitly select CPU")
        return "cpu", "cuda_unavailable"

    # is_available()/the GPU's name alone did not catch the RTX 50/cu124 fault.
    # Exercise the actual kernels/precision before any model can be loaded.
    try:
        with torch.inference_mode():
            dtype = torch.float16 if half else torch.float32
            sample = torch.ones((8, 8), device="cuda", dtype=dtype)
            result = sample @ sample
            convolution = torch.nn.functional.conv1d(
                torch.ones((1, 1, 16), device="cuda", dtype=dtype),
                torch.ones((1, 1, 3), device="cuda", dtype=dtype))
            torch.cuda.synchronize()
            if result.sum().item() != 512 or convolution.sum().item() != 42:
                raise VoiceDeviceError("cuda_probe_wrong_result")
        del sample, result, convolution
    except Exception as error:
        # Do not silently switch a detected but incompatible GPU to CPU.
        raise VoiceDeviceError("cuda_kernel_probe_failed; repair the GPU runtime") from error
    return "cuda", "cuda_kernel_probe_pass"


def prepare_config(config, device):
    if not isinstance(config, dict) or not isinstance(config.get("custom"), dict):
        raise VoiceDeviceError("missing_custom_voice_profile")
    profile = config["custom"]
    if not isinstance(profile.get("is_half", False), bool):
        raise VoiceDeviceError("invalid_voice_precision")
    if device not in ("cuda", "cpu"):
        raise VoiceDeviceError("invalid_effective_device")
    result = copy.deepcopy(config)
    result["custom"]["device"] = device
    if device == "cpu":
        result["custom"]["is_half"] = False
    return result


def validate_assets(config):
    # GPT-SoVITS otherwise substitutes default models when a path is missing.
    for key in ("t2s_weights_path", "vits_weights_path", "bert_base_path", "cnhuhbert_base_path"):
        value = config["custom"].get(key)
        if not isinstance(value, str) or not value or not Path(value).exists():
            raise VoiceDeviceError("configured_voice_asset_missing:" + key)


def verify_loaded_pipeline(pipeline, device):
    if str(pipeline.configs.device).split(":")[0] != device:
        raise VoiceDeviceError("model_device_differs_from_validated_device")
    devices = {}
    for name in ("t2s_model", "vits_model", "bert_model", "cnhuhbert_model"):
        observed = {parameter.device.type for parameter in getattr(pipeline, name).parameters()}
        if observed != {device}:
            raise VoiceDeviceError("model_device_mismatch:" + name)
        devices[name] = device
    return devices


def _cuda_fatal_reason(error):
    # Closed CUDA runtime diagnoses only. Capacity/OOM, validation failures and
    # generic model errors do not establish that a process context is unusable.
    if not isinstance(error, RuntimeError):
        return ""
    try:
        message = str(error).split("\n", 1)[0].strip().lower()[:4096]
    except Exception:
        return ""
    if "out of memory" in message or type(error).__name__ == "OutOfMemoryError":
        return ""
    if not message.startswith(("cuda error:", "cuda driver error:")):
        return ""
    for token, reason in (
        ("invalid resource handle", "cuda_invalid_resource_handle"),
        ("device-side assert", "cuda_device_side_assert"),
        ("illegal memory access", "cuda_illegal_memory_access"),
        ("context destroyed", "cuda_context_destroyed"),
        ("context is destroyed", "cuda_context_destroyed"),
        ("context has been destroyed", "cuda_context_destroyed"),
    ):
        if token in message:
            return reason
    return ""


class CudaFaultGuard:
    """One process-local latch; no retry, GPU operation, reset or CPU fallback."""
    def __init__(self, report=None):
        self._lock = threading.RLock()
        self._reason = ""
        self._request = None
        self.request_context = contextvars.ContextVar("lilith_cuda_fault_request", default=None)
        self._report = report

    @property
    def failed(self):
        with self._lock:
            return bool(self._reason)

    def belongs_to(self, request):
        with self._lock:
            return bool(self._reason) and (self._request is None or self._request is request)

    def invoke(self, action, stage):
        with self._lock:
            if self._reason:
                raise VoiceDeviceError("cuda_context_unavailable_restart_required")
            try:
                return action()
            except Exception as error:
                reason = _cuda_fatal_reason(error)
                if reason:
                    self._reason = reason
                    self._request = self.request_context.get()
                    if self._report is not None:
                        try:
                            self._report({"reason": reason,
                                "exception_type": "RuntimeError" if type(error) is RuntimeError else "RuntimeErrorSubclass",
                                "stage": stage if stage in ("run_create", "iteration_next", "iteration_send",
                                                            "iteration_throw", "iteration_close") else "unknown"})
                        except Exception:
                            pass  # Diagnostics cannot suppress or replace the original failure.
                raise

    def close(self, iterator):
        with self._lock:
            # Do not explicitly resume a failed generator during retirement.
            # Upstream cleanup or Python GC may still finalize the generator;
            # this adapter does not claim to intercept those paths.
            if self._reason:
                return None
            close = getattr(iterator, "close", None)
            return self.invoke(close, "iteration_close") if close is not None else None


class _CudaGuardedIterator:
    def __init__(self, iterator, guard):
        self._iterator = iterator
        self._guard = guard

    def __iter__(self):
        return self

    def __next__(self):
        return self._guard.invoke(lambda: next(self._iterator), "iteration_next")

    def send(self, value):
        return self._guard.invoke(lambda: self._iterator.send(value), "iteration_send")

    def throw(self, *args):
        return self._guard.invoke(lambda: self._iterator.throw(*args), "iteration_throw")

    def close(self):
        return self._guard.close(self._iterator)


def install_cuda_fault_guard(pipeline, device, report=None):
    if device != "cuda":
        return None
    # Only failures propagated by the bundled generator are observable here.
    # Preserve any upstream yielded data/internal recovery; this outer wrapper
    # does not claim to intercept errors that upstream catches internally.
    original_run = pipeline.run
    guard = CudaFaultGuard(report)

    def run(*args, **kwargs):
        iterator = guard.invoke(lambda: iter(original_run(*args, **kwargs)), "run_create")
        return _CudaGuardedIterator(iterator, guard)

    pipeline.run = run
    return guard


class CudaFaultExitApplication:
    def __init__(self, app, guard, request_exit):
        self._app = app
        self._guard = guard
        self._request_exit = request_exit

    async def __call__(self, scope, receive, send):
        if scope.get("type") != "http":
            return await self._app(scope, receive, send)
        request = object()
        binding = self._guard.request_context.set(request)
        try:
            # Preserve the bundled handler's original HTTP 400 response. Awaiting
            # the full ASGI call lets its final body finish before requesting exit.
            return await self._app(scope, receive, send)
        finally:
            self._guard.request_context.reset(binding)
            if self._guard.belongs_to(request):
                # Also retire after disconnect/cancellation unwinds this request;
                # neither cancellation nor shutdown replays its failed speech.
                self._request_exit()


def run_service(state, device, port):
    uvicorn = state["uvicorn"]
    guard = install_cuda_fault_guard(state["tts_pipeline"], device, lambda receipt: print(
        "LILITH_VOICE_CUDA_FATAL " + json.dumps(receipt, ensure_ascii=True), flush=True))
    if guard is None:
        return uvicorn.run(app=state["APP"], host="127.0.0.1", port=int(port), workers=1)

    def request_exit():
        server.should_exit = True

    app = CudaFaultExitApplication(state["APP"], guard, request_exit)
    server = uvicorn.Server(uvicorn.Config(app=app, host="127.0.0.1", port=int(port),
                                         workers=1, timeout_graceful_shutdown=5))
    try:
        server.run()
    finally:
        if guard.failed:
            raise VoiceDeviceError("cuda_context_failed_restart_required") from None


def launch(api, port, config_path, requested):
    import torch
    import yaml

    source = Path(config_path)
    if source.stem not in ("ja-cuda", "zh-cuda", "ja-cpu", "zh-cpu"):
        raise VoiceDeviceError("invalid_managed_voice_profile")
    if not 1024 <= int(port) <= 65535:
        raise VoiceDeviceError("invalid_loopback_port")
    delivery_speed = read_delivery_speed(source, port)
    if source.stat().st_size > 256 * 1024:
        raise VoiceDeviceError("voice_profile_too_large")
    with source.open(encoding="utf-8-sig") as stream:
        config = yaml.safe_load(stream)
    # Validate the shape before trusting precision or selecting hardware.
    prepare_config(config, "cpu")
    validate_assets(config)
    device, reason = select_device(torch, requested, config["custom"].get("is_half", False))
    effective = prepare_config(config, device)
    receipt = {
        "requested": requested, "effective": device, "reason": reason,
        "torch": torch.__version__, "cuda": torch.version.cuda,
        "is_half": effective["custom"].get("is_half", False),
    }
    if device == "cuda":
        receipt["gpu"] = torch.cuda.get_device_name(torch.cuda.current_device())
        receipt["capability"] = list(torch.cuda.get_device_capability())
    print("LILITH_VOICE_DEVICE " + json.dumps(receipt, ensure_ascii=True), flush=True)

    # At most two derived files. Original per-language model/precision profiles
    # are never rewritten by GPT-SoVITS' save_configs(). No speech text is stored.
    derived = source.with_name(".effective-" + source.stem[:2] + ".yaml")
    if derived.is_symlink():
        raise VoiceDeviceError("unsafe_derived_voice_profile")
    with derived.open("w", encoding="utf-8", newline="\n") as stream:
        yaml.safe_dump(effective, stream, allow_unicode=False)
    sys.argv = [api, "-a", "127.0.0.1", "-p", str(port), "-c", str(derived)]
    sys.path.insert(0, str(Path(api).resolve().parent))
    # Load the bundled API/model once, then validate the loaded models before
    # binding a port. This prevents GPT-SoVITS' own CPU fallback being labelled
    # as GPU success. HTTP handlers/transport remain the bundled API's handlers.
    state = runpy.run_path(api, run_name="lilith_managed_voice_api")
    devices = verify_loaded_pipeline(state["tts_pipeline"], device)
    print("LILITH_VOICE_MODELS " + json.dumps(devices), flush=True)
    if delivery_speed != 1.0:
        install_chinese_delivery(state["tts_pipeline"], delivery_speed)
        print("LILITH_VOICE_DELIVERY " + json.dumps({
            "locale": "zh", "speed_factor": delivery_speed,
        }), flush=True)
    cues = install_optional_japanese_cues(state["tts_pipeline"], source)
    if cues is not None:
        print("LILITH_VOICE_CUES " + json.dumps(cues), flush=True)
        install_japanese_delivery_endpoint(state)
        print("LILITH_JAPANESE_DELIVERY_PROTOCOL " + json.dumps({"schema_version": 1, "locale": "ja"}), flush=True)
    # The guard is outermost, covering ordinary, rate-adjusted and native-cue
    # synthesis alike, including lazy work executed by the first next().
    run_service(state, device, port)


if __name__ == "__main__":
    try:
        if len(sys.argv) != 5:
            raise VoiceDeviceError("invalid_voice_startup_arguments")
        launch(sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4])
    except VoiceDeviceError as error:
        print("LILITH_VOICE_DEVICE_ERROR " + str(error), file=sys.stderr, flush=True)
        sys.exit(78)
