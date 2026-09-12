"""Paired, isolated raw-model comparison; never changes installed Lite models."""
import argparse
import json
import os
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/lite-candidate-runtime'))
import ctranslate2
import sentencepiece


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('inputs', type=Path)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    installed = Path(os.environ['LOCALAPPDATA']) / 'Valtrans/Lite/models/ko_en'
    candidate = ROOT / 'artifacts/lite-candidate-ko-en'
    inputs = json.loads(args.inputs.read_text(encoding='utf-8-sig'))
    with args.output.open('x', encoding='utf-8') as output:
        for name, directory, source_spm, target_spm in (
            ('argos-1.1', installed / 'model', installed / 'sentencepiece.model', installed / 'sentencepiece.model'),
            ('marian-big', candidate / 'marian-ct2-int8', candidate / 'marian-original/source.spm', candidate / 'marian-original/target.spm'),
        ):
            translator = ctranslate2.Translator(str(directory), device='cpu', compute_type='int8',
                inter_threads=1, intra_threads=4)
            encoder = sentencepiece.SentencePieceProcessor(model_file=str(source_spm))
            decoder = sentencepiece.SentencePieceProcessor(model_file=str(target_spm))
            for row in inputs:
                for variant in ('original', 'expanded'):
                    started = time.perf_counter()
                    tokens = translator.translate_batch([encoder.encode(row[variant], out_type=str)],
                        beam_size=1, max_decoding_length=128, repetition_penalty=1.05)[0].hypotheses[0]
                    record = dict(id=row['id'], model=name, variant=variant, source=row['original'],
                        input=row[variant], reference=row['reference'], output=decoder.decode(tokens),
                        ms=round((time.perf_counter()-started)*1000, 2))
                    output.write(json.dumps(record, ensure_ascii=False) + '\n')
                    output.flush()
            translator.unload_model()
    print(args.output)


if __name__ == '__main__':
    main()
