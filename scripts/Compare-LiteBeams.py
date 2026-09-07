"""Offline diagnostic only: same installed Argos models, CPU INT8, beams 1/2/4.

Does not modify the production host or its settings. Dependencies live in a
separate test directory. Scores must be reviewed by a human, not inferred from
the decoder likelihood. Warm inference timings exclude initial model loading.
"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import statistics
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--runtime', default=str(ROOT / 'artifacts/lite-benchmark-runtime'))
parser.add_argument('--models', default=str(Path(os.environ['LOCALAPPDATA']) / 'Valtrans/Lite/models'))
parser.add_argument('--output', default=str(ROOT / 'artifacts/quality/lite-beams.json'))
args = parser.parse_args()
sys.path.insert(0, args.runtime)
os.environ['VALTRANS_LITE_MODELS'] = args.models
spec = importlib.util.spec_from_file_location('lite_host', ROOT / 'Lite/valtrans_lite.py')
host = importlib.util.module_from_spec(spec)
spec.loader.exec_module(host)

CASES = [
    ('en','ko','hello, nice to meet you', '안녕, 만나서 반가워'),
    ('en','ko','please wait until I flash', '내가 섬광 쓸 때까지 기다려'),
    ('en','ko','do not push left, go right', '왼쪽으로 밀지 말고 오른쪽으로 가'),
    ('en','ko','maybe two on the left, nobody on the right', '아마 왼쪽 두 명, 오른쪽엔 아무도 없음'),
    ('ko','en','안녕하세요, 만나서 반가워요', 'Hello, nice to meet you'),
    ('ko','en','왼쪽 조심해', 'Watch left'),
    ('ko','en','미드 말고 B로 가자, 아직 들어가지는 마', "Go B, not mid. Do not enter yet"),
    ('ko','en','오른쪽에 두 명 있는 것 같아', 'I think there are two on the right'),
    ('en','ja','please wait until I flash', 'フラッシュを使うまで待って'),
    ('en','ja','do not push left, go right', '左に詰めないで、右へ行って'),
    ('en','ja','I left the game because I had to go', '用事があってゲームを抜けた'),
    ('en','ja','Jett is low but not one shot', 'ジェットはローだけど一発では倒せない'),
    ('ja','en','どうぞよろしくおねがいします', 'Nice to meet you'),
    ('ja','en','右には誰もいないと思います', 'I think nobody is on the right'),
    ('ja','en','左には行かないで、右に行って', 'Do not go left, go right'),
    ('ja','en','フラッシュを使うまで待って', 'Wait until I flash'),
    ('ja','ko','どうぞよろしくおねがいします', '잘 부탁드립니다'),
    ('ja','ko','たぶん右に二人、左にはいない', '아마 오른쪽 두 명, 왼쪽에는 없음'),
    ('ja','ko','左には行かないで、右に行って', '왼쪽으로 가지 말고 오른쪽으로 가'),
    ('ja','ko','フラッシュを使うまで待って', '내가 섬광 쓸 때까지 기다려'),
    ('ko','ja','안녕하세요, 만나서 반가워요', 'こんにちは、よろしくお願いします'),
    ('ko','ja','왼쪽 조심해', '左に気をつけて'),
    ('ko','ja','오른쪽에 두 명 있는 것 같아', '右に二人いると思う'),
    ('ko','ja','미드 말고 B로 가자, 아직 들어가지는 마', 'ミッドではなくBへ行こう、まだ入らないで'),
]

def pair(text, source, target, beam):
    translator, tokenizer = host._load_pair(source, target)
    result = translator.translate_batch([tokenizer.encode(text, out_type=str)],
        beam_size=beam, max_batch_size=16, max_decoding_length=128, repetition_penalty=1.05)[0]
    return tokenizer.decode(result.hypotheses[0]).strip()

def translate(text, source, target, beam):
    if host._pair_ready(source, target):
        return pair(text, source, target, beam)
    return pair(pair(text, source, 'en', beam), 'en', target, beam)

rows = []
try:
    for index, (source, target, text, reference) in enumerate(CASES):
        for beam in (1,2,4):
            # Warm up each pair/beam; retain the same two-model cache as the host.
            translate(text, source, target, beam)
            timings = []
            for _ in range(2):
                start = time.perf_counter()
                output = translate(text, source, target, beam)
                timings.append(round((time.perf_counter() - start) * 1000, 1))
            row = dict(case=index+1, source=source, target=target, text=text,
                reference=reference, beam=beam, output=output,
                median_ms=statistics.median(timings), timings_ms=timings)
            rows.append(row)
            print(json.dumps(row, ensure_ascii=False), flush=True)
finally:
    host._release_all_models()
report = dict(runtime=host.ctranslate2.__version__, compute='CPU INT8',
    threads=max(1,min(4,(os.cpu_count() or 2)//2)), samples=len(CASES),
    note='Offline raw model outputs, no glossary/guard. Different runtime from bundled executable may affect absolute speed.',
    medians_ms={str(b):statistics.median(r['median_ms'] for r in rows if r['beam']==b) for b in (1,2,4)},
    results=rows)
destination = Path(args.output)
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report['medians_ms']), flush=True)
