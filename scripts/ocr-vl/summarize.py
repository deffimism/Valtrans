"""Score saved OCR output without loading either model."""
import argparse
import json
from pathlib import Path
import statistics
from benchmark import score, save, WORK

parser = argparse.ArgumentParser()
parser.add_argument("--results", type=Path, default=WORK / "results")
args = parser.parse_args()
cases = {item["id"]: item for item in json.loads((args.results / "inputs.json").read_text(encoding="utf-8"))}
windows = json.loads((args.results / "windows.json").read_text(encoding="utf-8-sig"))
paddle = json.loads((args.results / "paddle.json").read_text(encoding="utf-8"))
if paddle["task"] != "ocr":
    raise ValueError("Spotting output must not be scored as plain OCR text")
summary = []
for row in windows + [{**row, "mode": "PaddleOCR-VL-1.5"} for row in paddle["cases"]]:
    case = cases[row["id"]]
    if row["sha256"] != case["sha256"]:
        raise ValueError("The engines did not use identical images")
    if not row["runs"]:
        raise ValueError("Incomplete benchmark")
    last = row["runs"][-1]
    metrics = score(last["text"], case)
    result = {"id": row["id"], "mode": row["mode"],
              "median_seconds": statistics.median(run["seconds"] for run in row["runs"]),
              "last_seconds": last["seconds"], "text": last["text"],
              "finished": last.get("finished", True), **metrics}
    summary.append(result)
    print(json.dumps(result, ensure_ascii=False))
save(args.results / "summary.json", summary)
