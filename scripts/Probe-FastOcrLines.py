"""Experimental upright-row segmentation on saved chat crops, not production routing."""
import argparse
import json
from pathlib import Path
import sys
import time
import numpy as np
from PIL import Image, ImageOps
from paddleocr import TextRecognition

sys.stdout.reconfigure(encoding='utf-8')
sys.stderr.reconfigure(encoding='utf-8')


def row_crops(image, body_only=False):
    pixels = np.asarray(image.convert('RGB')).astype(np.int16)
    bright = (pixels.max(axis=2) >= 135) & (pixels.max(axis=2) - pixels.min(axis=2) < 100)
    active = bright.sum(axis=1) >= max(4, image.width * .004)
    indices = np.flatnonzero(active)
    groups = np.split(indices, np.where(np.diff(indices) > 3)[0] + 1)
    crops = []
    for group in groups:
        if len(group) == 0:
            continue
        y1, y2 = int(group[0]), int(group[-1]) + 1
        if y2 - y1 < 8 or y2 - y1 > 80 or y1 == 0 or y2 == image.height:
            continue
        xs = np.flatnonzero(bright[y1:y2].any(axis=0))
        if len(xs) == 0:
            continue
        if body_only:
            white = (pixels[y1:y2].min(axis=2) >= 160) & (pixels[y1:y2].max(axis=2) - pixels[y1:y2].min(axis=2) <= 30)
            white_xs = np.flatnonzero(white.any(axis=0))
            if len(white_xs) > 4:
                xs = white_xs
        box = (max(0, int(xs[0]) - 4), max(0, y1 - 4),
               min(image.width, int(xs[-1]) + 5), min(image.height, y2 + 4))
        crops.append((box, image.crop(box)))
    return crops


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--input', type=Path, nargs='+', required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--body-only', action='store_true')
    args = p.parse_args()
    with args.output.open('x', encoding='utf-8') as stream:
        for model in ['PP-OCRv5_server_rec', 'korean_PP-OCRv5_mobile_rec']:
            engine = TextRecognition(model_name=model)
            for path in args.input:
                with Image.open(path) as source:
                    crops = row_crops(source.convert('RGB'), args.body_only)
                for invert in [False, True]:
                    inputs = [np.asarray(ImageOps.invert(ImageOps.grayscale(im)).convert('RGB') if invert else im)
                              for _, im in crops]
                    start = time.perf_counter()
                    predictions = list(engine.predict(inputs))
                    elapsed = round((time.perf_counter() - start) * 1000, 1)
                    lines = [dict(box=box, text=item['rec_text'], score=float(item['rec_score']))
                             for (box, _), item in zip(crops, predictions)]
                    record = dict(input=str(path), model=model, inverted=invert, ms=elapsed, lines=lines)
                    stream.write(json.dumps(record, ensure_ascii=False) + '\n')
                    stream.flush()
                    print(record, flush=True)
            engine.close()


if __name__ == '__main__':
    main()
