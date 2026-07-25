import json
import subprocess
import sys
from pathlib import Path
import unittest


class WorkerProtocolTests(unittest.TestCase):
    def test_mock_request_returns_versioned_result(self):
        worker = Path(__file__).parents[1] / "worker.py"
        request = {"protocolVersion": 1, "requestId": "test", "engine": "mock", "imagePath": "sample.png"}
        completed = subprocess.run([sys.executable, str(worker)], input=json.dumps(request) + "\n", text=True, capture_output=True, check=True)
        response = json.loads(completed.stdout)
        self.assertEqual("test", response["requestId"])
        self.assertEqual("ok", response["status"])
        self.assertEqual([], response["words"])


if __name__ == "__main__":
    unittest.main()
