$ErrorActionPreference = 'Stop'
dotnet test "$PSScriptRoot\..\TateScribe.sln" --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (Test-Path "$PSScriptRoot\..\ocr-worker\tests") {
    python -m unittest discover "$PSScriptRoot\..\ocr-worker\tests"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
