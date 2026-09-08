"""Offline OCR experiment. Never modifies app settings or calls a translation API.

Model and dependencies are downloaded separately; inference uses local_files_only.
Ground truth is used ONLY after inference, never supplied to the OCR model.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import statistics
import time
import unicodedata

ROOT = Path(__file__).resolve().parents[2]
MODEL = "PaddlePaddle/PaddleOCR-VL-1.5"
WORK = ROOT / "artifacts/ocr-vl"
os.environ.setdefault("HF_HOME", str(WORK / "hf-cache"))
os.environ["HF_HUB_DISABLE_IMPLICIT_TOKEN"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"


def normalized(value):
    return "".join(unicodedata.normalize("NFKC", value).split())


def distance(a, b):
    row = list(range(len(b) + 1))
    for i, left in enumerate(a, 1):
        new = [i]
        for j, right in enumerate(b, 1):
            new.append(min(new[-1] + 1, row[j] + 1, row[j-1] + (left != right)))
        row = new
    return row[-1]


def score(text, case):
    actual = normalized(text)
    expected = normalized("\n".join(case["lines"]))
    return {
        "character_error_rate_ignoring_whitespace": distance(expected, actual) / max(1, len(expected)),
        "critical_substrings": {term: normalized(term) in actual for term in case["critical"]},
    }


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def fixtures(source, manifest, output):
    from PIL import Image
    result = []
    for case in manifest["cases"]:
        path = source / case["file"]
        with Image.open(path) as original:
            x0, y0, x1, y1 = case["crop"]
            if not (0 <= x0 < x1 <= original.width and 0 <= y0 < y1 <= original.height):
                raise ValueError(f"Invalid crop for {case['id']}: {original.size}")
            image = original.convert("RGB").crop(case["crop"])
            target = output / "inputs" / f"{case['id']}.png"
            target.parent.mkdir(parents=True, exist_ok=True)
            image.save(target)
            result.append({**case, "image": str(target), "size": list(image.size),
                           "sha256": hashlib.sha256(target.read_bytes()).hexdigest()})
    save(output / "inputs.json", result)
    return result


def run(args):
    if args.self_test:
        assert distance("abc", "axc") == 1
        assert distance("", "12") == 2
        assert normalized(" ミッド ２\n") == "ミッド2"
        fixture = {"lines": ["(팀) 나: ミッド 2"], "critical": ["ミッド2"]}
        assert score("(팀)나:ミッド2", fixture)["character_error_rate_ignoring_whitespace"] == 0
        assert not score("ミッド3", fixture)["critical_substrings"]["ミッド2"]
        print("PASS: OCR scoring and normalization", flush=True)
        return
    if args.download:
        from huggingface_hub import HfApi, snapshot_download
        info = HfApi().model_info(MODEL, token=False)
        snapshot = snapshot_download(MODEL, revision=info.sha, token=False,
                                     local_dir=WORK / "models" / info.sha,
                                     allow_patterns=["*.json", "*.safetensors", "*.txt", "*.model", "*.jinja"])
        save(WORK / "model.json", {"model": MODEL, "revision": info.sha, "snapshot": snapshot})
        print(f"Model downloaded: {info.sha}", flush=True)
        return
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    cases = fixtures(args.images, manifest, args.output)
    if args.only:
        cases = [case for case in cases if case["id"] == args.only]
        if not cases:
            raise ValueError("Unknown case ID")
    if args.prepare_only:
        print(f"Prepared {len(cases)} identical crops for both engines", flush=True)
        return

    # No downloads or image uploads are possible in the inference stage.
    os.environ["HF_HUB_OFFLINE"] = "1"
    import torch
    import transformers
    from PIL import Image
    from transformers import AutoModelForImageTextToText, AutoProcessor
    if args.threads:
        torch.set_num_threads(args.threads)
    if not torch.cuda.is_available():
        raise RuntimeError("CUDA is unavailable; do not silently substitute slow CPU timing")
    lock = json.loads((WORK / "model.json").read_text(encoding="utf-8"))
    start = time.perf_counter()
    print("Loading PaddleOCR-VL locally…", flush=True)
    model = AutoModelForImageTextToText.from_pretrained(
        lock["snapshot"], dtype=torch.bfloat16, local_files_only=True,
        trust_remote_code=False, attn_implementation="sdpa").to("cuda").eval()
    processor = AutoProcessor.from_pretrained(lock["snapshot"], local_files_only=True, trust_remote_code=False)
    torch.cuda.synchronize()
    report = {
        **lock, "gpu": torch.cuda.get_device_name(), "torch": torch.__version__,
        "transformers": transformers.__version__, "task": args.task,
        "cpu_threads": torch.get_num_threads(),
        "use_kv_cache": not args.no_kv_cache,
        "load_seconds": time.perf_counter() - start, "max_new_tokens": args.max_tokens,
        "notes": "Local cropped screenshots. No translation or glossary. First run may include kernel warmup. CER ignores whitespace, not a confidence score. No in-game FPS measurement.",
        "cases": []}
    save(args.output / "paddle.json", report)
    for case in cases:
        item = {"id": case["id"], "sha256": case["sha256"], "runs": []}
        report["cases"].append(item)
        for repeat in range(args.repeats):
            torch.cuda.reset_peak_memory_stats()
            torch.cuda.synchronize()
            start = time.perf_counter()
            with Image.open(case["image"]) as image:
                messages = [{"role": "user", "content": [
                    {"type": "image", "image": image.convert("RGB")},
                    {"type": "text", "text": "OCR:" if args.task == "ocr" else "Spotting:"}]}]
                inputs = processor.apply_chat_template(messages, add_generation_prompt=True,
                    tokenize=True, return_dict=True, return_tensors="pt",
                    processor_kwargs={"images_kwargs": {"size": {
                        "shortest_edge": processor.image_processor.size["shortest_edge"],
                        "longest_edge": 1280 * 28 * 28}}}).to(model.device)
            with torch.inference_mode():
                output = model.generate(**inputs, max_new_tokens=args.max_tokens, do_sample=False,
                                        max_time=args.timeout, use_cache=not args.no_kv_cache)
            torch.cuda.synchronize()
            seconds = time.perf_counter() - start
            tokens = output[0][inputs["input_ids"].shape[-1]:]
            text = processor.decode(tokens, skip_special_tokens=True)
            eos = model.generation_config.eos_token_id
            eos_ids = eos if isinstance(eos, list) else [eos]
            finished = len(tokens) > 0 and tokens[-1].item() in eos_ids
            entry = {"seconds": seconds, "text": text, "generated_tokens": len(tokens),
                     "finished": finished, "peak_allocated_mib": torch.cuda.max_memory_allocated()/2**20,
                     "peak_reserved_mib": torch.cuda.max_memory_reserved()/2**20,
                     **(score(text, case) if args.task == "ocr" else {})}
            item["runs"].append(entry)
            save(args.output / "paddle.json", report)
            print(json.dumps({"case": case["id"], "repeat": repeat, **entry}, ensure_ascii=False), flush=True)
            del inputs, output, tokens
        item["median_seconds"] = statistics.median(x["seconds"] for x in item["runs"])
    save(args.output / "paddle.json", report)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--download", action="store_true")
    parser.add_argument("--prepare-only", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--images", type=Path, default=Path(os.environ.get("TEMP", ".")))
    parser.add_argument("--manifest", type=Path, default=Path(__file__).with_name("cases.json"))
    parser.add_argument("--output", type=Path, default=WORK / "results")
    parser.add_argument("--repeats", type=int, choices=range(1, 6), default=2)
    parser.add_argument("--max-tokens", type=int, default=512)
    parser.add_argument("--timeout", type=float, default=45)
    parser.add_argument("--task", choices=["ocr", "spotting"], default="ocr")
    parser.add_argument("--only", help="Run one fixture ID")
    parser.add_argument("--threads", type=int, choices=range(1, 33), default=4)
    parser.add_argument("--no-kv-cache", action="store_true",
                        help="Reproduce the slow official checkpoint default for comparison")
    run(parser.parse_args())
