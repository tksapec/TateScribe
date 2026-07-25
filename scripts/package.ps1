$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\test.ps1"
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'
dotnet publish "$PSScriptRoot\..\src\TateScribe.App\TateScribe.App.csproj" -c Release -r win-x64 --self-contained true -o $output
$runtime = Join-Path $PSScriptRoot '..\ocr-runtime'
if (-not (Test-Path (Join-Path $runtime 'Scripts\python.exe'))) { throw 'OCR runtime is missing. Run scripts/setup-ocr.ps1 before packaging.' }
Copy-Item $runtime (Join-Path $output 'ocr-runtime') -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot '..\ocr-worker') (Join-Path $output 'ocr-worker') -Recurse -Force
