$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\test.ps1"
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
dotnet publish "$PSScriptRoot\..\src\TateScribe.App\TateScribe.App.csproj" -c Release -r win-x64 --self-contained true -o $output
$runtime = Join-Path $PSScriptRoot '..\ocr-runtime'
if (-not (Test-Path (Join-Path $runtime 'Scripts\python.exe'))) { throw 'OCR runtime is missing. Run scripts/setup-ocr.ps1 before packaging.' }
$packagedRuntime = Join-Path $output 'ocr-runtime'
$packagedWorker = Join-Path $output 'ocr-worker'
New-Item -ItemType Directory -Path $packagedRuntime, $packagedWorker | Out-Null
Copy-Item (Join-Path $runtime '*') $packagedRuntime -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot '..\ocr-worker\*') $packagedWorker -Recurse -Force
$archive = Join-Path $PSScriptRoot '..\artifacts\TateScribe-win-x64.zip'
if (Test-Path $archive) { Remove-Item -LiteralPath $archive -Force }
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $archive -CompressionLevel Optimal
Write-Host "Created $archive"
