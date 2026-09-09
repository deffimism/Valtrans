"""Local stdin/stdout OCR worker. No HTTP listener, downloads, or translation.

One request at a time. The parent owns this process and kills it on cancellation.
Do not log request images/text. stdout is a JSON-lines protocol only.
"""
import argparse
import base64
import contextlib
import io
import json
import os
from pathlib import Path
import sys
import time

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["HF_HUB_DISABLE_IMPLICIT_TOKEN"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
os.environ["TOKENIZERS_PARALLELISM"] = "false"
os.environ["PYTHONIOENCODING"] = "utf-8"
sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")


def send(value):
    print(json.dumps(value, ensure_ascii=False), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--runtime", required=True, type=Path)
    args = parser.parse_args()
    with contextlib.redirect_stdout(sys.stderr):
        import torch
        from PIL import Image
        from transformers import AutoModelForImageTextToText, AutoProcessor
        if not torch.cuda.is_available():
            raise RuntimeError("CUDA GPU를 사용할 수 없습니다. GPU용 OCR 실행 환경을 확인하세요.")
        torch.set_num_threads(4)
        lock = json.loads((args.runtime / "model.json").read_text(encoding="utf-8"))
        snapshot = Path(lock["snapshot"])
        if not snapshot.is_dir():
            raise RuntimeError("모델 경로가 없습니다. 실행 환경을 이동했다면 모델을 다시 준비하세요.")
        model = AutoModelForImageTextToText.from_pretrained(str(snapshot), local_files_only=True,
            trust_remote_code=False, dtype=torch.bfloat16, attn_implementation="sdpa").to("cuda").eval()
        processor = AutoProcessor.from_pretrained(str(snapshot), local_files_only=True, trust_remote_code=False)
    send({"ready": True, "model": lock["model"], "gpu": torch.cuda.get_device_name(),
          "allocatedMiB": round(torch.cuda.memory_allocated() / 2**20)})
    while True:
        line = sys.stdin.readline(12_000_001)
        if not line:
            return
        if len(line) > 12_000_000 or not line.endswith("\n"):
            raise ValueError("OCR 요청 크기가 너무 큽니다.")
        request = json.loads(line)
        if request.get("command") == "stop":
            return
        request_id = request.get("id")
        try:
            start = time.perf_counter()
            png = base64.b64decode(request["png"], validate=True)
            with Image.open(io.BytesIO(png)) as source:
                if source.width < 20 or source.height < 20 or source.width * source.height > 4_000_000:
                    raise ValueError("OCR 영역은 채팅 부분만 작게 지정하세요 (최대 400만 픽셀).")
                image = source.convert("RGB")
            with contextlib.redirect_stdout(sys.stderr), torch.inference_mode():
                messages = [{"role": "user", "content": [
                    {"type": "image", "image": image}, {"type": "text", "text": "OCR:"}]}]
                inputs = processor.apply_chat_template(messages, tokenize=True, add_generation_prompt=True,
                    return_dict=True, return_tensors="pt", processor_kwargs={"images_kwargs": {"size": {
                        "shortest_edge": processor.image_processor.size["shortest_edge"],
                        "longest_edge": 1280 * 28 * 28}}}).to(model.device)
                output = model.generate(**inputs, max_new_tokens=128, max_time=12,
                                        do_sample=False, use_cache=True)
                tokens = output[0][inputs["input_ids"].shape[-1]:]
                eos = model.generation_config.eos_token_id
                finished = len(tokens) > 0 and tokens[-1].item() in (eos if isinstance(eos, list) else [eos])
                text = processor.decode(tokens, skip_special_tokens=True)
                torch.cuda.synchronize()
                del inputs, output, tokens
            send({"id": request_id, "text": text if finished else "", "finished": finished,
                  "milliseconds": round((time.perf_counter() - start) * 1000)})
        except Exception as ex:
            send({"id": request_id, "error": str(ex)[:300]})


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:
        send({"ready": False, "error": str(ex)[:300]})
        sys.exit(1)
