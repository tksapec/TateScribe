import json
import subprocess
import sys
from pathlib import Path
import tempfile
from unittest import mock
import unittest

sys.path.insert(0, str(Path(__file__).parents[1]))
import worker


class WorkerProtocolTests(unittest.TestCase):
    def tearDown(self):
        worker.reset_paddle_engine_cache()

    def test_paddle_configuration_keeps_small_text_candidates(self):
        worker = (Path(__file__).parents[1] / "worker.py").read_text(encoding="utf-8")
        self.assertIn("text_det_thresh=0.15", worker)
        self.assertIn("text_det_box_thresh=0.3", worker)

    def test_tesseract_vertical_adapter_is_available(self):
        worker = (Path(__file__).parents[1] / "worker.py").read_text(encoding="utf-8")
        self.assertIn('request.get("engine") == "tesseract"', worker)
        self.assertIn("jpn_vert", worker)

    def test_mock_request_returns_versioned_result(self):
        worker = Path(__file__).parents[1] / "worker.py"
        request = {"protocolVersion": 1, "requestId": "test", "engine": "mock", "imagePath": "sample.png"}
        completed = subprocess.run([sys.executable, str(worker)], input=json.dumps(request) + "\n", text=True, capture_output=True, check=True)
        response = json.loads(completed.stdout)
        self.assertEqual("test", response["requestId"])
        self.assertEqual("ok", response["status"])
        self.assertEqual([], response["words"])

    def test_paddle_engine_is_initialized_once_for_the_same_model_directories(self):
        calls = []

        class FakePaddle:
            def __init__(self, **options):
                calls.append(options)

        with tempfile.TemporaryDirectory() as directory:
            det = Path(directory) / "det"
            rec = Path(directory) / "rec"
            det.mkdir()
            rec.mkdir()
            fake_module = type(sys)("paddleocr")
            fake_module.PaddleOCR = FakePaddle
            with mock.patch.dict(sys.modules, {"paddleocr": fake_module}):
                first = worker.get_paddle_engine(det, rec)
                second = worker.get_paddle_engine(det, rec)

        self.assertIs(first, second)
        self.assertEqual(1, len(calls))

    def test_paddle_engine_is_reinitialized_for_a_different_model_configuration(self):
        calls = []

        class FakePaddle:
            def __init__(self, **options):
                calls.append(options)

        with tempfile.TemporaryDirectory() as directory:
            first_det = Path(directory) / "det-1"
            first_rec = Path(directory) / "rec-1"
            second_det = Path(directory) / "det-2"
            second_rec = Path(directory) / "rec-2"
            for path in (first_det, first_rec, second_det, second_rec):
                path.mkdir()
            fake_module = type(sys)("paddleocr")
            fake_module.PaddleOCR = FakePaddle
            with mock.patch.dict(sys.modules, {"paddleocr": fake_module}):
                worker.get_paddle_engine(first_det, first_rec)
                worker.get_paddle_engine(second_det, second_rec)

        self.assertEqual(2, len(calls))

    def test_tesseract_request_never_initializes_paddle(self):
        with mock.patch.object(worker, "get_paddle_engine") as get_paddle:
            response = worker.response_for({
                "protocolVersion": 1,
                "requestId": "tess",
                "engine": "tesseract",
                "imagePath": "missing.png",
            })

        get_paddle.assert_not_called()
        self.assertEqual("error", response["status"])
        self.assertEqual("Tesseract", response["stage"])
        self.assertEqual("ValueError", response["exceptionType"])


if __name__ == "__main__":
    unittest.main()
