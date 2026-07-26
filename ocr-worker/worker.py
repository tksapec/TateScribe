"""Offline JSON Lines OCR worker. Models are loaded only from local paths."""
import json
import os
import subprocess
import sys
from pathlib import Path

PROTOCOL_VERSION = 1
_paddle_engine = None
_paddle_engine_config = None
_paddle_initialization_error = None


def reset_paddle_engine_cache() -> None:
    """Clear process-local state. Primarily useful when restarting or testing the worker."""
    global _paddle_engine, _paddle_engine_config, _paddle_initialization_error
    _paddle_engine = None
    _paddle_engine_config = None
    _paddle_initialization_error = None


def get_paddle_engine(det_dir: Path, rec_dir: Path):
    global _paddle_engine, _paddle_engine_config, _paddle_initialization_error
    config = (str(det_dir.resolve()), str(rec_dir.resolve()))
    if _paddle_engine is not None and _paddle_engine_config == config:
        return _paddle_engine

    from paddleocr import PaddleOCR
    try:
        engine = PaddleOCR(
            text_detection_model_dir=config[0],
            text_recognition_model_dir=config[1],
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )
    except Exception as error:
        _paddle_engine = None
        _paddle_engine_config = None
        _paddle_initialization_error = {
            "exceptionType": type(error).__name__,
            "message": str(error),
        }
        raise
    _paddle_engine = engine
    _paddle_engine_config = config
    _paddle_initialization_error = None
    return engine


def error_response(request_id, stage: str, error: Exception, retryable: bool = True) -> dict:
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "requestId": request_id,
        "status": "error",
        "stage": stage,
        "exceptionType": type(error).__name__,
        "error": str(error),
        "retryable": retryable,
    }


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
            return error_response(request_id, "PaddleOCR", error)
    if request.get("engine") == "tesseract":
        try:
            return tesseract_response(request)
        except Exception as error:
            return error_response(request_id, "Tesseract", error)
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
    engine = get_paddle_engine(det_dir, rec_dir)
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


def tesseract_response(request: dict) -> dict:
    image_path = Path(request["imagePath"])
    if not image_path.is_file():
        raise ValueError("OCR input image does not exist")
    root = Path(__file__).resolve().parents[1]
    runtime = root / "ocr-runtime"
    executable = Path(os.environ.get("TATESCRIBE_TESSERACT_PATH", runtime / "tesseract" / "tesseract.exe"))
    if not executable.is_file():
        executable = Path(r"C:\Program Files\Tesseract-OCR\tesseract.exe")
    tessdata = Path(os.environ.get("TESSDATA_PREFIX", runtime / "tessdata"))
    if not executable.is_file() or not (tessdata / "jpn_vert.traineddata").is_file():
        raise ValueError("Tesseract jpn_vert runtime is missing. Run setup before OCR.")
    completed = subprocess.run(
        [str(executable), str(image_path), "stdout", "--tessdata-dir", str(tessdata), "-l", "jpn_vert", "--psm", "5"],
        capture_output=True, text=True, encoding="utf-8", errors="replace", check=True,
    )
    text = collapse_tesseract_paragraphs(completed.stdout)
    return {
        "protocolVersion": PROTOCOL_VERSION,
        "requestId": request["requestId"],
        "status": "ok",
        "engine": "tesseract",
        "modelVersion": "jpn_vert",
        "words": [{"text": text, "confidence": 0.8, "left": 0, "top": 0, "right": 1, "bottom": 1}],
    }


def collapse_tesseract_paragraphs(text: str) -> str:
    paragraphs = []
    current_paragraph = []
    for line in text.splitlines():
        line = line.strip()
        if line:
            current_paragraph.append(line)
        elif current_paragraph:
            paragraphs.append("".join(current_paragraph))
            current_paragraph = []
    if current_paragraph:
        paragraphs.append("".join(current_paragraph))
    return "\n".join(paragraphs)


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
