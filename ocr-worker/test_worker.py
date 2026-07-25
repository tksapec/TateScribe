import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from worker import collapse_tesseract_paragraphs


class TesseractTextTests(unittest.TestCase):
    def test_collapse_tesseract_paragraphs_preserves_blank_line_boundaries(self):
        raw = "最初の行\n続きの行\n\n引用文\n"

        self.assertEqual("最初の行続きの行\n引用文", collapse_tesseract_paragraphs(raw))


if __name__ == "__main__":
    unittest.main()
