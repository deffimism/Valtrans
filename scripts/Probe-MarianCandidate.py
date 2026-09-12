"""Evaluate verified original Marian weights, avoiding the broken HF vocabulary.

This is an isolated experiment, not an installed Lite model. Archive scripts are
never extracted or executed; only model/tokenizer data and attribution are read.
"""
import csv
import hashlib
import json
from pathlib import Path
import sys
import time
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/lite-candidate-runtime'))
import ctranslate2
import sentencepiece

WORK = ROOT / 'artifacts/lite-candidate-ko-en'
ARCHIVE_HASH = '9132ed606b6964656520931d96a4a6afc7ddc7d55ca5438e1d403db0c05492ef'


def main():
    archive_path = WORK / 'original-marian.zip'
    with archive_path.open('rb') as stream:
        if hashlib.file_digest(stream, 'sha256').hexdigest() != ARCHIVE_HASH:
            raise ValueError('Original archive SHA256 mismatch')
    model = WORK / 'marian-original'
    model.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive_path) as archive:
        selected = [entry for entry in archive.infolist() if
                    entry.filename.endswith(('.npz', '.vocab', '.spm')) or entry.filename in ('README.md', 'LICENSE')]
        for entry in selected:
            if Path(entry.filename).name != entry.filename or entry.file_size > 1024**3:
                raise ValueError('Unexpected model archive member')
            destination = model / entry.filename
            if not destination.exists():
                with archive.open(entry) as source, destination.open('wb') as target:
                    while chunk := source.read(1024 * 1024):
                        target.write(chunk)
    converted = WORK / 'marian-ct2-int8'
    if not (converted / 'model.bin').exists():
        print('Converting original separate-vocabulary Marian model', flush=True)
        # This release stores one token per line, in embedding-index order.
        # CT2's Marian converter expects a token -> index YAML vocabulary.
        vocabularies = []
        for suffix in ('src', 'trg'):
            raw_vocab = next(model.glob(f'*.{suffix}.vocab'))
            tokens = raw_vocab.read_text(encoding='utf-8').splitlines()
            if len(tokens) != len(set(tokens)) or tokens[:3] != ['<unk>', '<s>', '</s>']:
                raise ValueError('Unexpected original vocabulary order/duplicates')
            yaml_vocab = model / f'{suffix}.vocab.yml'
            yaml_vocab.write_text('\n'.join(f'{json.dumps(token, ensure_ascii=False)}: {i}'
                for i, token in enumerate(tokens)) + '\n', encoding='utf-8')
            vocabularies.append(str(yaml_vocab))
        ctranslate2.converters.MarianConverter(str(next(model.glob('*.npz'))),
            vocabularies).convert(
                str(converted), quantization='int8', force=False)
    encoder = sentencepiece.SentencePieceProcessor(model_file=str(model / 'source.spm'))
    decoder = sentencepiece.SentencePieceProcessor(model_file=str(model / 'target.spm'))
    translator = ctranslate2.Translator(str(converted), device='cpu', compute_type='int8',
                                       inter_threads=1, intra_threads=4)
    result = WORK / ('marian-results-' + str(time.time_ns()) + '.jsonl')
    with result.open('w', encoding='utf-8') as output:
        for corpus_name in ('corpus.tsv', 'held-out.tsv'):
            with (ROOT / 'testdata/translation-quality' / corpus_name).open(encoding='utf-8-sig', newline='') as stream:
                for row in csv.DictReader(stream, delimiter='\t'):
                    for variant in ('original', 'punctuated'):
                        source = row['ko']
                        if variant == 'punctuated' and source[-1:] not in '.!?。！？':
                            source += '.'
                        for beam in (1, 2):
                            start = time.perf_counter()
                            translated = translator.translate_batch([encoder.encode(source, out_type=str)],
                                beam_size=beam, max_decoding_length=160)[0].hypotheses[0]
                            text = decoder.decode(translated)
                            output.write(json.dumps(dict(id=row['id'], source=source, reference=row['en'],
                                variant=variant, beam=beam, output=text,
                                ms=round(1000 * (time.perf_counter()-start), 2)), ensure_ascii=False) + '\n')
                            output.flush()
                    print('Checked family ' + row['id'], flush=True)
    manifest = dict(originalArchiveSha256=ARCHIVE_HASH, results=str(result),
                    license='CC-BY-4.0', ctranslate2=ctranslate2.__version__,
                    files={str(path.relative_to(WORK)): hashlib.sha256(path.read_bytes()).hexdigest()
                           for path in converted.rglob('*') if path.is_file()})
    (WORK / 'marian-probe-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    print('Marian results: ' + str(result), flush=True)


if __name__ == '__main__':
    main()
