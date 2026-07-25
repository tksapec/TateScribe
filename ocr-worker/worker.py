"""Offline JSON Lines OCR worker. Models are loaded only from local paths."""
import json
import os
import sys
from pathlib import Path

PROTOCOL_VERSION = 1


def response_for(request: dict) -> dict:
    request_id = request.get("requestId")
    if request.get("protocolVersion") != PROTOCOL_VERSION:
        return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "status": "error", "error": "Unsupported protocol version"}
    if request.get("engine") == "mock":
        return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "status": "ok", "engine": "mock", "modelVersion": "none", "words": []}
    if request.get("engine") == "paddle":
        try:
            return paddle_response(request)
        except Exception as error:  # worker boundary: return a structured, retryable error
            return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "status": "error", "error": str(error)}
    return {"protocolVersion": PROTOCOL_VERSION, "requestId": request_id, "status": "error", "error": "OCR engine is not configured with a local model"}


def paddle_response(request: dict) -> dict:
    image_path = Path(request["imagePath"])
    if not image_path.is_file():
        raise ValueError("OCR input image does not exist")
    root = Path(__file__).resolve().parents[1]
    cache_root = Path(os.environ.get("PADDLE_PDX_CACHE_HOME", root / "ocr-runtime" / "cache"))
    det_dir = Path(os.environ.get("TATESCRIBE_PADDLE_DET_MODEL_DIR", cache_root / "official_models" / "PP-OCRv6_medium_det"))
    rec_dir = Path(os.environ.get("TATESCRIBE_PADDLE_REC_MODEL_DIR", cache_root / "official_models" / "PP-OCRv6_medium_rec"))
    if not det_dir.is_dir() or not rec_dir.is_dir():
        raise ValueError("Local PaddleOCR models are missing; run setup before OCR. Runtime downloads are disabled.")
    os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"
    from paddleocr import PaddleOCR
    engine = PaddleOCR(
        text_detection_model_dir=str(det_dir),
        text_recognition_model_dir=str(rec_dir),
        use_doc_orientation_classify=False,
        use_doc_unwarping=False,
        use_textline_orientation=False,
    )
    result = next(iter(engine.predict(
        str(image_path),
        text_det_thresh=0.15,
        text_det_box_thresh=0.3,
    )))
    data = result.json["res"]
    words = []
    for box, text, confidence in zip(data["rec_boxes"], data["rec_texts"], data["rec_scores"]):
        words.append({
            "text": text,
            "confidence": float(confidence),
            "left": float(box[0]),
            "top": float(box[1]),
            "right": float(box[2]),
            "bottom": float(box[3]),
        })
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "requestId": request["requestId"],
        "status": "ok",
        "engine": "paddle",
        "modelVersion": "PP-OCRv6-medium",
        "words": words,
    }


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
