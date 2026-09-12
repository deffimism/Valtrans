"""Fast OCR construction regression; no model download or Paddle dependency."""
import types
import unittest
from unittest.mock import Mock, patch

import fast_ocr_host


class UprightChatConfigurationTests(unittest.TestCase):
    def test_chat_skips_document_rotation_and_dewarping(self):
        constructor = Mock()
        with patch.dict("sys.modules", {"paddleocr": types.SimpleNamespace(PaddleOCR=constructor)}):
            result = fast_ocr_host.build_ocr()
        self.assertIs(result, constructor.return_value)
        constructor.assert_called_once_with(
            ocr_version="PP-OCRv5", lang="ch",
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )


if __name__ == "__main__":
    unittest.main()
