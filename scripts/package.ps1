$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\test.ps1"
$output = Join-Path $PSScriptRoot '..\artifacts\win-x64'
dotnet publish "$PSScriptRoot\..\src\TateScribe.App\TateScribe.App.csproj" -c Release -r win-x64 --self-contained true -o $output
