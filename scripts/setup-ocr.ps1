param([string]$Python = "python")
$ErrorActionPreference = 'Stop'
& $Python -m pip install --requirement "$PSScriptRoot\..\ocr-worker\requirements.lock"
Write-Host 'Install PaddleOCR model directories under ocr-runtime\models and configure their paths before running production OCR. No model download occurs at runtime.'
