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
$tesseract = Join-Path $runtime 'tesseract\tesseract.exe'
if (-not (Test-Path $tesseract)) {
  winget install --id tesseract-ocr.tesseract --exact --silent --accept-package-agreements --accept-source-agreements
  New-Item -ItemType Directory -Path (Join-Path $runtime 'tesseract') -Force | Out-Null
  Copy-Item 'C:\Program Files\Tesseract-OCR\*' (Join-Path $runtime 'tesseract') -Recurse -Force
}
$tessdata = Join-Path $runtime 'tessdata'
New-Item -ItemType Directory -Path $tessdata -Force | Out-Null
$jpnVert = Join-Path $tessdata 'jpn_vert.traineddata'
if (-not (Test-Path $jpnVert)) { Invoke-WebRequest -Uri 'https://github.com/tesseract-ocr/tessdata_best/raw/main/jpn_vert.traineddata' -OutFile $jpnVert }
Write-Host 'PaddleOCR and Tesseract jpn_vert models were installed into ocr-runtime. OCR runtime never downloads models.'
