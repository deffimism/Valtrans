"""Upright VALORANT chat rows with verified prefix separation.

An uncertain layout returns None so the caller can use document detection.
Colors only propose a split. Recognition must prove the removed crop is a
complete channel/name prefix, including its colon, before using the body crop.
"""
import re
import hashlib
from collections import OrderedDict
import numpy as np

PREFIX = re.compile(r'^\s*(?:\[[^\]\r\n]{1,16}\]|\([^\)\r\n]{1,16}\))\s*[^:：\r\n]{1,32}[:：]\s*$')


def segment_rows(image):
    pixels = np.asarray(image.convert('RGB')).astype(np.int16)
    bright = (pixels.max(axis=2) >= 135) & (pixels.max(axis=2) - pixels.min(axis=2) < 100)
    # A bright game scene is not evidence of rows of text.
    if float(bright.mean()) > .35:
        return None
    indices = np.flatnonzero(bright.sum(axis=1) >= max(4, image.width * .004))
    if not len(indices):
        return [] if pixels.max() < 100 else None
    groups = np.split(indices, np.where(np.diff(indices) > 3)[0] + 1)
    rows = []
    for group in groups:
        y1, y2 = int(group[0]), int(group[-1]) + 1
        if y1 == 0 or y2 == image.height:
            continue  # Cut-off history line, never infer its missing letters.
        if y2 - y1 > 80:
            return None
        if y2 - y1 < 8:
            continue
        xs = np.flatnonzero(bright[y1:y2].any(axis=0))
        x1, x2 = max(0, int(xs[0]) - 4), min(image.width, int(xs[-1]) + 5)
        box = (x1, max(0, y1 - 4), x2, min(image.height, y2 + 4))
        if (x2 - x1) < 8:
            continue
        white = (pixels[y1:y2].min(axis=2) >= 160) & (pixels[y1:y2].max(axis=2) - pixels[y1:y2].min(axis=2) <= 30)
        white_x = np.flatnonzero(white.any(axis=0))
        split = max(0, int(white_x[0]) - 4) if len(white_x) > 4 else x1
        candidate = split if split - x1 >= 24 and x2 - split >= 8 else None
        rows.append((box, candidate))
    return rows if len(rows) <= 12 else None


def choose_body(primary, korean):
    # Cross-model confidence is heuristic, not a correctness probability.
    # Never prefer a Korean model's Latin guess over actual kana/han characters.
    if korean is not None and re.search('[가-힣]', korean[0]) and korean[1] >= .7:
        if korean[1] >= primary[1] - .03:
            return korean
    return primary


class ChatRecognizer:
    def __init__(self, factory=None, cache_size=128):
        if factory is None:
            from paddleocr import TextRecognition
            factory = TextRecognition
        self.factory = factory
        self.models = {}
        self.cache_size = max(0, min(512, cache_size))
        self.cache = OrderedDict()
        self.cache_hits = 0
        self.inference_batches = 0

    def prepare(self, languages):
        self._model('ch')
        if 'KO' in languages:
            self._model('ko')

    def _model(self, language):
        if language not in self.models:
            name = 'korean_PP-OCRv5_mobile_rec' if language == 'ko' else 'PP-OCRv5_server_rec'
            self.models[language] = self.factory(model_name=name, device='cpu', cpu_threads=4)
        return self.models[language]

    def _read(self, language, images):
        if not images:
            return []
        # Exact pixels only: no fuzzy image/text similarity, no on-disk history.
        # Coordinates are intentionally absent so an unchanged row can move as chat scrolls.
        rgb = [im.convert('RGB') for im in images]
        keys = [(language, im.size, hashlib.sha256(im.tobytes()).digest()) for im in rgb]
        values = {}
        missing = {}
        for key, im in zip(keys, rgb):
            if key in self.cache:
                values[key] = self.cache.pop(key)
                self.cache[key] = values[key]
                self.cache_hits += 1
            elif key not in missing:
                missing[key] = im
        if not missing:
            return [values[key] for key in keys]
        self.inference_batches += 1
        predictions = list(self._model(language).predict([np.asarray(im) for im in missing.values()]))
        if len(predictions) != len(missing):
            raise ValueError('OCR recognition result count mismatch')
        for key, item in zip(missing, predictions):
            value = (item.get('rec_text', '').strip(), float(item.get('rec_score', 0)))
            values[key] = value
            if self.cache_size:
                self.cache[key] = value
                while len(self.cache) > self.cache_size:
                    self.cache.popitem(last=False)
        return [values[key] for key in keys]

    def recognize(self, image, languages):
        rows = segment_rows(image)
        if rows is None:
            return None
        primary = 'ko' if set(languages) == {'KO'} else 'ch'
        # Verify the proposed discarded fragment separately. Any extra word after
        # ':' rejects the split; colored message content must not be silently lost.
        candidates = [(i, (box[0], box[1], split, box[3]))
                      for i, (box, split) in enumerate(rows) if split is not None]
        prefix_reads = self._read(primary, [image.crop(box) for _, box in candidates])
        prefixes = {i: text for (i, _), (text, score) in zip(candidates, prefix_reads)
                    if score >= .9 and PREFIX.fullmatch(text)}
        content_boxes = [(split if i in prefixes else box[0], box[1], box[2], box[3])
                         for i, (box, split) in enumerate(rows)]
        crops = [image.crop(box) for box in content_boxes]
        main = self._read(primary, crops)
        secondary = self._read('ko', crops) if 'KO' in languages and primary != 'ko' else [None] * len(crops)
        result = []
        for i, (first, second) in enumerate(zip(main, secondary)):
            text, score = choose_body(first, second)
            if not text:
                continue
            result.append(dict(text=(prefixes[i] + ' ' if i in prefixes else '') + text,
                               confidence=score, box=list(rows[i][0]), prefixSeparated=i in prefixes))
        return result
