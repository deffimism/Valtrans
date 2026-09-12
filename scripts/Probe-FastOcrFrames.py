"""Compare real saved game-chat crops; never changes application model settings."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import time
import sys

sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')

import numpy as np
from PIL import Image
from paddleocr import PaddleOCR


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', type=Path, nargs='+', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--languages', nargs='+', default=['ch', 'korean'])
    parser.add_argument('--scales', nargs='+', type=int, default=[1, 2, 3])
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open('x', encoding='utf-8') as output:
        for language in args.languages:
            started = time.perf_counter()
            ocr = PaddleOCR(ocr_version='PP-OCRv5', lang=language,
                           use_doc_orientation_classify=False, use_doc_unwarping=False,
                           use_textline_orientation=False)
            init_ms = round((time.perf_counter() - started) * 1000, 1)
            for path in args.input:
                with Image.open(path) as original:
                    original = original.convert('RGB')
                    for scale in args.scales:
                        image = original.resize((original.width * scale, original.height * scale),
                                                Image.Resampling.BICUBIC) if scale != 1 else original
                        started = time.perf_counter()
                        predictions = list(ocr.predict(np.asarray(image)))
                        elapsed = round((time.perf_counter() - started) * 1000, 1)
                        lines = []
                        for prediction in predictions:
                            texts = prediction.get('rec_texts', [])
                            scores = prediction.get('rec_scores', [])
                            lines.extend({'text': text, 'score': float(score)}
                                         for text, score in zip(texts, scores))
                        record = dict(input=str(path), sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                                      language=language, scale=scale, lines=lines, ms=elapsed, initMs=init_ms,
                                      paddleocr=importlib.metadata.version('paddleocr'),
                                      paddlepaddle=importlib.metadata.version('paddlepaddle'))
                        output.write(json.dumps(record, ensure_ascii=False) + '\n')
                        output.flush()
                        print(language, path.name, scale, elapsed, lines, flush=True)
            del ocr


if __name__ == '__main__':
    main()
