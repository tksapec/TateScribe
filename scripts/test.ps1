$ErrorActionPreference = 'Stop'
dotnet test "$PSScriptRoot\..\TateScribe.sln" --configuration Release
if (Test-Path "$PSScriptRoot\..\ocr-worker\tests") { python -m unittest discover "$PSScriptRoot\..\ocr-worker\tests" }
