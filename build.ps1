# Runs the tests, then publishes and packages the Windows and Linux apps into dist\.
#   .\build.ps1 [--skip-tests] [--out <folder>]
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet run scripts/build.cs -- @args
exit $LASTEXITCODE
