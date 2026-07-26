$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\test.ps1"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
dotnet publish "$PSScriptRoot\..\src\TateScribe.App\TateScribe.App.csproj" -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$runtime = Join-Path $PSScriptRoot '..\ocr-runtime'
if (-not (Test-Path (Join-Path $runtime 'Scripts\python.exe'))) { throw 'OCR runtime is missing. Run scripts/setup-ocr.ps1 before packaging.' }
if (-not (Test-Path (Join-Path $runtime 'tesseract\tesseract.exe'))) { throw 'Tesseract runtime is missing. Run scripts/setup-ocr.ps1 before packaging.' }
if (-not (Test-Path (Join-Path $runtime 'tessdata\jpn_vert.traineddata'))) { throw 'Tesseract jpn_vert model is missing. Run scripts/setup-ocr.ps1 before packaging.' }
$packagedRuntime = Join-Path $output 'ocr-runtime'
$packagedWorker = Join-Path $output 'ocr-worker'
New-Item -ItemType Directory -Path $packagedRuntime, $packagedWorker | Out-Null
Copy-Item (Join-Path $runtime '*') $packagedRuntime -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot '..\ocr-worker\worker.py') $packagedWorker -Force
Copy-Item (Join-Path $PSScriptRoot '..\ocr-worker\requirements.lock') $packagedWorker -Force
$archive = Join-Path $PSScriptRoot '..\artifacts\TateScribe-win-x64.zip'
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    (Resolve-Path -LiteralPath $output).Path,
    $archive,
    [System.IO.Compression.CompressionLevel]::NoCompression,
    $false)
Write-Host "Created $archive"
