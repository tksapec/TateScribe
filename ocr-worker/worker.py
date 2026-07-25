"""Offline JSON Lines OCR worker. Models are loaded only from configured local paths."""
import json
import sys

PROTOCOL_VERSION = 1


def response_for(request: dict) -> dict:
    request_id = request.get("requestId")
    if request.get("protocolVersion") != PROTOCOL_VERSION:
        return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "status": "error", "error": "Unsupported protocol version"}
    if request.get("engine") == "mock":
        return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "status": "ok", "engine": "mock", "modelVersion": "none", "words": []}
    return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "status": "error", "error": "OCR engine is not configured with a local model"}


def main() -> int:
    for line in sys.stdin:
        try:
            request = json.loads(line)
            print(json.dumps(response_for(request), ensure_ascii=False), flush=True)
        except json.JSONDecodeError:
            print(json.dumps({"protocolVersion": PROTOCOL_VERSION, "requestId": None, "status": "error", "error": "Invalid JSON request"}), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
