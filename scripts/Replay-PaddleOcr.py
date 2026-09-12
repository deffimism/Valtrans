"""Run the production VL worker on saved OCR test images, without opening a UI."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--input', type=Path, nargs='+', required=True)
parser.add_argument('--runtime', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
sys.stdout.reconfigure(encoding='utf-8')
requests = '\n'.join(json.dumps(dict(id=str(i), png=base64.b64encode(path.read_bytes()).decode('ascii')))
                     for i, path in enumerate(args.input)) + '\n'
worker = Path(__file__).resolve().parents[1] / 'Ocr' / 'paddle_host.py'
result = subprocess.run([str(args.runtime / '.venv/Scripts/python.exe'), str(worker), '--runtime', str(args.runtime)],
    input=requests, capture_output=True, text=True, encoding='utf-8', timeout=180,
    creationflags=subprocess.CREATE_NO_WINDOW if sys.platform == 'win32' else 0)
with args.output.open('x', encoding='utf-8') as output:
    output.write(result.stdout)
print(result.stdout)
if result.returncode:
    print(result.stderr[-2000:])
    raise SystemExit(result.returncode)
