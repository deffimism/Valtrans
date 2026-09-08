"""Download the tested official revision only. No remote Python code or inference."""
import json
import os
from pathlib import Path
import sys

root = Path(sys.argv[1]).resolve()
os.environ["HF_HOME"] = str(root / "hf-cache")
os.environ["HF_HUB_DISABLE_IMPLICIT_TOKEN"] = "1"
os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
from huggingface_hub import snapshot_download

model = "PaddlePaddle/PaddleOCR-VL-1.5"
revision = "2a4195faa5e7914c12f2fc601d72c81caf8d2da5"
snapshot = snapshot_download(model, revision=revision, token=False, local_dir=root / "models" / revision,
                             allow_patterns=["*.json", "*.safetensors", "*.txt", "*.model", "*.jinja"])
(root / "model.json").write_text(json.dumps({"model": model, "revision": revision, "snapshot": snapshot},
                                           ensure_ascii=False, indent=2), encoding="utf-8")
print("OCR model downloaded", flush=True)
