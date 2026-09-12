"""Replay saved test crops through the production recognizer with candidate evidence."""
import argparse
import contextlib
import json
from pathlib import Path
import sys
import time
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'Ocr'))
from chat_ocr import ChatRecognizer


def main():
    sys.stdout.reconfigure(encoding='utf-8')
    sys.stderr.reconfigure(encoding='utf-8')
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', type=Path, nargs='+', required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--repeats', type=int, default=1)
    parser.add_argument('--cache-size', type=int, default=128)
    args = parser.parse_args()
    evidence = []
    reader = ChatRecognizer(cache_size=args.cache_size)
    original_read = reader._read

    def observe(language, images):
        values = original_read(language, images)
        evidence.append(dict(language=language, sizes=[im.size for im in images], candidates=values))
        return values

    reader._read = observe
    with contextlib.redirect_stdout(sys.stderr):
        reader.prepare(['EN', 'JP', 'KO'])
    with args.output.open('x', encoding='utf-8') as output:
        for repeat, path in ((repeat, path) for repeat in range(args.repeats) for path in args.input):
            evidence.clear()
            with Image.open(path) as frame:
                hits, batches = reader.cache_hits, reader.inference_batches
                start = time.perf_counter()
                with contextlib.redirect_stdout(sys.stderr):
                    rows = reader.recognize(frame, ['EN', 'JP', 'KO'])
                record = dict(input=str(path), repeat=repeat, cacheSize=args.cache_size,
                              cacheHits=reader.cache_hits-hits, inferenceBatches=reader.inference_batches-batches,
                              milliseconds=(time.perf_counter()-start)*1000,
                              rows=rows, candidates=evidence)
            output.write(json.dumps(record, ensure_ascii=False) + '\n')
            output.flush()
            print(json.dumps(record, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
