"""Persistent, minimal Argos/OPUS-MT translation host for Valtrans.

The host speaks newline-delimited JSON over stdin/stdout.  Keeping one process
alive lets CTranslate2 reuse loaded models instead of paying startup cost for
every OCR line.
"""

from __future__ import annotations

import json
import os
import sys
import gc
import threading
import time
import traceback
from collections import OrderedDict
from pathlib import Path

import ctranslate2
import sentencepiece as sentencepiece


if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

MODEL_ROOT = Path(os.environ.get("VALTRANS_LITE_MODELS", Path(sys.executable).parent / "models"))
MAX_LOADED_MODELS = 2
IDLE_RELEASE_SECONDS = 10 * 60
_CACHE: OrderedDict[tuple[str, str], tuple[ctranslate2.Translator, sentencepiece.SentencePieceProcessor]] = OrderedDict()
_CACHE_LOCK = threading.RLock()
_LAST_ACTIVITY = time.monotonic()


def _package_dir(source: str, target: str) -> Path:
    return MODEL_ROOT / f"{source}_{target}"


def _model_files(source: str, target: str) -> tuple[Path, Path]:
    package = _package_dir(source, target)
    return package / "model", package / "sentencepiece.model"


def _pair_ready(source: str, target: str) -> bool:
    model, tokenizer = _model_files(source, target)
    return (model / "model.bin").is_file() and tokenizer.is_file()


def _load_pair(source: str, target: str):
    global _LAST_ACTIVITY
    key = (source, target)
    with _CACHE_LOCK:
        _LAST_ACTIVITY = time.monotonic()
        if key in _CACHE:
            _CACHE.move_to_end(key)
            return _CACHE[key]

    model_dir, tokenizer_path = _model_files(source, target)
    if not _pair_ready(source, target):
        raise RuntimeError(f"모델이 설치되지 않았습니다: {source.upper()}→{target.upper()}")

    cpu_count = os.cpu_count() or 2
    translator = ctranslate2.Translator(
        str(model_dir),
        device="cpu",
        compute_type="int8",
        inter_threads=1,
        intra_threads=max(1, min(4, cpu_count // 2)),
    )
    tokenizer = sentencepiece.SentencePieceProcessor(model_file=str(tokenizer_path))
    with _CACHE_LOCK:
        while len(_CACHE) >= MAX_LOADED_MODELS:
            _CACHE.popitem(last=False)
        _CACHE[key] = (translator, tokenizer)
        _CACHE.move_to_end(key)
        gc.collect()
        return translator, tokenizer


def _release_all_models() -> int:
    with _CACHE_LOCK:
        released = len(_CACHE)
        for translator, _ in _CACHE.values():
            try:
                translator.unload_model()
            except Exception:
                pass
        _CACHE.clear()
    if released:
        gc.collect()
    return released


def _idle_release_worker():
    while True:
        time.sleep(30)
        with _CACHE_LOCK:
            idle = time.monotonic() - _LAST_ACTIVITY
            has_models = bool(_CACHE)
        if has_models and idle >= IDLE_RELEASE_SECONDS:
            _release_all_models()


def _translate_pair(texts: list[str], source: str, target: str) -> list[str]:
    translator, tokenizer = _load_pair(source, target)
    encoded = [tokenizer.encode(text, out_type=str) for text in texts]
    results = translator.translate_batch(
        encoded,
        beam_size=1,
        max_batch_size=16,
        max_decoding_length=128,
        repetition_penalty=1.05,
    )
    return [tokenizer.decode(result.hypotheses[0]).strip() for result in results]


def translate(texts: list[str], source: str, target: str) -> list[str]:
    source = source.lower()
    target = target.lower()
    if source == target:
        return texts
    if _pair_ready(source, target):
        return _translate_pair(texts, source, target)
    if source != "en" and target != "en" and _pair_ready(source, "en") and _pair_ready("en", target):
        return _translate_pair(_translate_pair(texts, source, "en"), "en", target)
    raise RuntimeError(f"번역 경로가 없습니다: {source.upper()}→{target.upper()}")


def status() -> dict:
    pairs = ["en_ko", "ko_en", "en_ja", "ja_en"]
    installed = {pair: _pair_ready(*pair.split("_")) for pair in pairs}
    with _CACHE_LOCK:
        loaded = [f"{source}_{target}" for source, target in _CACHE]
        idle_seconds = max(0, int(time.monotonic() - _LAST_ACTIVITY))
    return {
        "ready": all(installed.values()),
        "installed": installed,
        "loaded": loaded,
        "maxLoadedModels": MAX_LOADED_MODELS,
        "idleReleaseSeconds": IDLE_RELEASE_SECONDS,
        "idleSeconds": idle_seconds,
        "modelRoot": str(MODEL_ROOT),
        "compute": "CPU INT8",
    }


def handle(message: dict) -> dict:
    command = message.get("command", "")
    if command == "status":
        return status()
    if command == "translate":
        texts = message.get("texts") or []
        if not isinstance(texts, list) or not all(isinstance(item, str) for item in texts):
            raise ValueError("texts는 문자열 배열이어야 합니다.")
        translations = translate(texts, message.get("source", "en"), message.get("target", "ko"))
        return {"translations": translations, **status()}
    if command == "warmup":
        source = message.get("source", "en")
        target = message.get("target", "ko")
        if source == target:
            source = "en" if target != "en" else "ko"
        if _pair_ready(source, target):
            _load_pair(source, target)
        elif source != "en" and target != "en":
            _load_pair(source, "en")
            _load_pair("en", target)
        else:
            raise RuntimeError(f"예열할 모델이 없습니다: {source.upper()}→{target.upper()}")
        return status()
    if command == "release":
        return {"released": _release_all_models(), **status()}
    if command == "keepalive":
        global _LAST_ACTIVITY
        with _CACHE_LOCK:
            _LAST_ACTIVITY = time.monotonic()
        return status()
    if command == "shutdown":
        return {"shutdown": True}
    raise ValueError(f"알 수 없는 명령: {command}")


def write_response(request_id, result=None, error=None):
    payload = {"id": request_id, "ok": error is None}
    if error is None:
        payload["result"] = result
    else:
        payload["error"] = str(error)
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main() -> int:
    threading.Thread(target=_idle_release_worker, name="valtrans-lite-idle-release", daemon=True).start()
    for raw_line in sys.stdin:
        request_id = None
        try:
            message = json.loads(raw_line)
            request_id = message.get("id")
            result = handle(message)
            write_response(request_id, result=result)
            if result.get("shutdown"):
                return 0
        except Exception as error:
            write_response(request_id, error=error)
            if os.environ.get("VALTRANS_LITE_DEBUG") == "1":
                traceback.print_exc(file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
