param(
    [switch]$IncludeSlowZip
)

$ErrorActionPreference = 'Stop'
$testArguments = @(
    'test',
    "$PSScriptRoot\..\TateScribe.sln",
    '--configuration',
    'Release'
)
if (-not $IncludeSlowZip) {
    $testArguments += @('--filter', 'Category!=SlowZip')
}
dotnet @testArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (Test-Path "$PSScriptRoot\..\ocr-worker\tests") {
    python -m unittest discover "$PSScriptRoot\..\ocr-worker\tests"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
