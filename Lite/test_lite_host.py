"""Tokenizer wiring tests without downloading models or starting a subprocess."""
import importlib.util
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


class Tokenizer:
    def __init__(self, model_file):
        self.name = Path(model_file).name

    def encode(self, text, out_type):
        return [self.name + ':encoded:' + text]

    def decode(self, tokens):
        return self.name + ':decoded:' + tokens[0]


class Translator:
    def __init__(self, *args, **kwargs):
        self.unloaded = False

    def translate_batch(self, encoded, **kwargs):
        return [SimpleNamespace(hypotheses=[item]) for item in encoded]

    def unload_model(self):
        self.unloaded = True


spec = importlib.util.spec_from_file_location('lite_host_tested', Path(__file__).with_name('valtrans_lite.py'))
host = importlib.util.module_from_spec(spec)
with patch.dict(sys.modules, {'ctranslate2': SimpleNamespace(Translator=Translator),
                              'sentencepiece': SimpleNamespace(SentencePieceProcessor=Tokenizer)}):
    spec.loader.exec_module(host)


class HostTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='valtrans-lite-test-')
        self.previous_root = host.MODEL_ROOT
        host.MODEL_ROOT = Path(self.temp.name)
        self.pair = host.MODEL_ROOT / 'ko_en'
        (self.pair / 'model').mkdir(parents=True)
        (self.pair / 'model/model.bin').touch()

    def tearDown(self):
        host._release_all_models()
        host.MODEL_ROOT = self.previous_root
        self.temp.cleanup()

    def test_existing_shared_tokenizer_remains_supported(self):
        (self.pair / 'sentencepiece.model').touch()
        self.assertTrue(host._pair_ready('ko', 'en'))
        self.assertEqual(['sentencepiece.model:decoded:sentencepiece.model:encoded:hello'],
            host.translate(['hello'], 'ko', 'en'))
        _, encoder, decoder = host._load_pair('ko', 'en')
        self.assertIs(encoder, decoder)

    def test_marian_uses_target_decoder_not_source_encoder(self):
        for name in ('source.spm', 'target.spm'):
            (self.pair / name).touch()
        self.assertEqual(['target.spm:decoded:source.spm:encoded:hello'], host.translate(['hello'], 'ko', 'en'))
        translator, encoder, decoder = host._load_pair('ko', 'en')
        self.assertIsNot(encoder, decoder)
        self.assertEqual(1, host._release_all_models())
        self.assertTrue(translator.unloaded)

    def test_half_installed_separate_tokenizers_do_not_fall_back_to_shared(self):
        for name in ('source.spm', 'sentencepiece.model'):
            (self.pair / name).touch()
        self.assertFalse(host._pair_ready('ko', 'en'))
        with self.assertRaises(RuntimeError):
            host._load_pair('ko', 'en')

    def test_same_language_does_not_load_a_model(self):
        self.assertEqual(['안녕'], host.translate(['안녕'], 'ko', 'ko'))
        self.assertFalse(host._CACHE)

    def test_four_bidirectional_pairs_stay_loaded_but_cache_is_bounded(self):
        pairs = [('ko', 'en'), ('en', 'ja'), ('ja', 'en'), ('en', 'ko')]
        loaded = []
        for source, target in pairs + [('de', 'en')]:
            pair = host.MODEL_ROOT / f'{source}_{target}'
            (pair / 'model').mkdir(parents=True, exist_ok=True)
            (pair / 'model/model.bin').touch()
            (pair / 'sentencepiece.model').touch()
            loaded.append(host._load_pair(source, target)[0])
            if len(loaded) == 4:
                self.assertEqual(4, len(host._CACHE))
                self.assertFalse(any(translator.unloaded for translator in loaded))
        self.assertEqual(4, len(host._CACHE))
        self.assertTrue(loaded[0].unloaded)
        self.assertFalse(loaded[-1].unloaded)


if __name__ == '__main__':
    unittest.main()
