param([string]$Python = "python")
$ErrorActionPreference = 'Stop'
$runtime = Join-Path $PSScriptRoot '..\ocr-runtime'
$runtimePython = Join-Path $runtime 'Scripts\python.exe'
if (-not (Test-Path $runtimePython)) {
  & $Python -m venv $runtime
}
& $runtimePython -m pip install --upgrade pip
& $runtimePython -m pip install --requirement "$PSScriptRoot\..\ocr-worker\requirements.lock"
$env:PADDLE_PDX_CACHE_HOME = Join-Path $runtime 'cache'
$env:PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK = 'True'
& $runtimePython -c "from paddleocr import PaddleOCR; PaddleOCR(lang='japan', use_doc_orientation_classify=False, use_doc_unwarping=False, use_textline_orientation=False)"
Write-Host 'PaddleOCR models were downloaded during setup into ocr-runtime\cache. OCR runtime never downloads models.'
