"""Isolated OPUS-MT candidate conversion/evaluation; never alters installed Lite.

Use the bundled Python with dependencies in artifacts/lite-candidate-runtime.
Model source revision is pinned; no remote Python code or account token is used.
"""
import csv
import hashlib
import json
import os
from pathlib import Path
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/lite-candidate-runtime'))
os.environ['HF_HUB_DISABLE_TELEMETRY'] = '1'
os.environ['HF_HUB_DISABLE_IMPLICIT_TOKEN'] = '1'
os.environ['HF_HOME'] = str(ROOT / 'artifacts/lite-candidate-cache')

import ctranslate2
from huggingface_hub import snapshot_download
from transformers import MarianTokenizer

REPOSITORY = 'Helsinki-NLP/opus-mt-tc-big-ko-en'
REVISION = 'fa26583a41d95346933f26b4cb8f9b700da0d445'
WORK = ROOT / 'artifacts/lite-candidate-ko-en'


def main():
    WORK.mkdir(parents=True, exist_ok=True)
    model = WORK / 'original'
    snapshot_download(REPOSITORY, revision=REVISION, local_dir=model, token=False,
                      allow_patterns=['README.md', '*.json', '*.spm', 'model.safetensors'])
    converted = WORK / 'ct2-int8'
    if not (converted / 'model.bin').exists():
        print('Converting official model to CPU int8', flush=True)
        ctranslate2.converters.TransformersConverter(str(model)).convert(
            str(converted), quantization='int8', force=False)
    tokenizer = MarianTokenizer.from_pretrained(model, local_files_only=True)
    translator = ctranslate2.Translator(str(converted), device='cpu', compute_type='int8',
                                       inter_threads=1, intra_threads=4)
    corpora = [ROOT / 'testdata/translation-quality/corpus.tsv',
               ROOT / 'testdata/translation-quality/held-out.tsv']
    result = WORK / ('results-' + str(time.time_ns()) + '.jsonl')
    with result.open('w', encoding='utf-8') as output:
        for corpus in corpora:
            with corpus.open(encoding='utf-8-sig', newline='') as stream:
                for row in csv.DictReader(stream, delimiter='\t'):
                    for variant in ('original', 'punctuated'):
                        source = row['ko']
                        if variant == 'punctuated' and source[-1:] not in '.!?。！？':
                            source += '.'
                        for beam in (1, 2):
                            start = time.perf_counter()
                            encoded = tokenizer.convert_ids_to_tokens(tokenizer.encode(source))
                            translated = translator.translate_batch([encoded], beam_size=beam,
                                max_decoding_length=160)[0].hypotheses[0]
                            text = tokenizer.decode(tokenizer.convert_tokens_to_ids(translated),
                                                    skip_special_tokens=True)
                            output.write(json.dumps(dict(id=row['id'], source=source,
                                reference=row['en'], variant=variant, beam=beam, output=text,
                                ms=round(1000 * (time.perf_counter()-start), 2)), ensure_ascii=False) + '\n')
                            output.flush()
                    print('Checked family ' + row['id'], flush=True)
    manifest = dict(repository=REPOSITORY, revision=REVISION, results=str(result),
                    ctranslate2=ctranslate2.__version__, license='CC-BY-4.0',
                    files={str(path.relative_to(WORK)): hashlib.sha256(path.read_bytes()).hexdigest()
                           for path in WORK.rglob('*') if path.is_file() and '.cache' not in path.parts})
    (WORK / 'probe-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print('Candidate results: ' + str(result), flush=True)


if __name__ == '__main__':
    main()
