import unittest
from unittest.mock import patch
from PIL import Image
import chat_ocr


class ChatOcrTests(unittest.TestCase):
    def cached_reader(self, size=128):
        class FakeModel:
            def predict(self, images):
                return [dict(rec_text=str(int(im.sum())), rec_score=.95) for im in images]
        return chat_ocr.ChatRecognizer(factory=lambda **kwargs: FakeModel(), cache_size=size)

    def test_identical_pixels_reuse_result_but_changed_pixel_does_not(self):
        reader = self.cached_reader()
        image = Image.new('RGB', (20, 20), 'black')
        original = reader._read('ch', [image])
        self.assertEqual(original, reader._read('ch', [image.copy()]))
        self.assertEqual(1, reader.inference_batches)
        image.putpixel((0, 0), (255, 255, 255))
        self.assertNotEqual(original, reader._read('ch', [image]))
        self.assertEqual(2, reader.inference_batches)

    def test_cache_is_separated_by_model_and_dimensions(self):
        reader = self.cached_reader()
        reader._read('ch', [Image.new('RGB', (20, 20))])
        reader._read('ko', [Image.new('RGB', (20, 20))])
        reader._read('ch', [Image.new('RGB', (10, 40))])
        self.assertEqual(3, reader.inference_batches)

    def test_cache_bound_and_disable(self):
        for size in (0, 2):
            reader = self.cached_reader(size)
            for i in range(5):
                reader._read('ch', [Image.new('RGB', (20, 20), (i, i, i))])
            self.assertEqual(size, len(reader.cache))

    def test_batch_repetition_preserves_order_and_count(self):
        reader = self.cached_reader()
        black, white = Image.new('RGB', (20, 20)), Image.new('RGB', (20, 20), 'white')
        values = reader._read('ch', [black, white, black])
        self.assertEqual(3, len(values))
        self.assertEqual(values[0], values[2])
        self.assertNotEqual(values[0], values[1])

    def test_blank_dark_crop_has_no_rows(self):
        self.assertEqual([], chat_ocr.segment_rows(Image.new('RGB', (640, 115), (16, 24, 40))))

    def test_bright_scene_requires_detector(self):
        self.assertIsNone(chat_ocr.segment_rows(Image.new('RGB', (640, 115), 'white')))

    def test_korean_requires_script_and_confidence(self):
        self.assertEqual(('왼쪽 조심해', .99), chat_ocr.choose_body(('召', .95), ('왼쪽 조심해', .99)))
        self.assertEqual(('Bラッシュ', .85), chat_ocr.choose_body(('Bラッシュ', .85), ('B1', .98)))
        self.assertEqual(('watch left', .99), chat_ocr.choose_body(('watch left', .99), ('와치 레프트', .4)))

    def _read_case(self, prefix):
        reader = chat_ocr.ChatRecognizer(factory=lambda **kwargs: None)
        calls = []
        def read(language, images):
            calls.append([image.size for image in images])
            return [(prefix, .99)] if len(calls) == 1 else [('not B rush', .95)]
        reader._read = read
        with patch.object(chat_ocr, 'segment_rows', return_value=[((0, 0, 100, 20), 40)]):
            result = reader.recognize(Image.new('RGB', (100, 20)), ['JP'])
        return calls, result

    def test_verified_prefix_allows_body_crop(self):
        calls, result = self._read_case('[TEAM] PlayerA:')
        self.assertEqual([(60, 20)], calls[1])
        self.assertTrue(result[0]['prefixSeparated'])
        self.assertEqual('[TEAM] PlayerA: not B rush', result[0]['text'])

    def test_colored_body_word_after_colon_must_not_be_discarded(self):
        calls, result = self._read_case('[TEAM] PlayerA: not')
        self.assertEqual([(100, 20)], calls[1])
        self.assertFalse(result[0]['prefixSeparated'])

    def test_bare_name_without_channel_is_not_a_verified_prefix(self):
        calls, result = self._read_case('PlayerA:')
        self.assertEqual([(100, 20)], calls[1])
        self.assertFalse(result[0]['prefixSeparated'])

    def test_second_fullwidth_colon_does_not_hide_colored_body(self):
        self.assertIsNone(chat_ocr.PREFIX.fullmatch('[TEAM] PlayerA： not：'))


if __name__ == '__main__':
    unittest.main()
