# Usage: powershell -File tools/core-tests/run.ps1 [fixture-name-filter] [-Unity <Editor\Data path>]
param([string]$Filter = '', [string]$Unity = 'D:\source\Unity\6000.6.6f1\Editor\Data')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
$dotnet = Join-Path $Unity 'DotNetSdk\dotnet.exe'
$out = Join-Path $env:TEMP 'cypher-core-tests'
& $dotnet build "$PSScriptRoot\core-tests.csproj" -c Release -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet "$out\core-tests.dll" $Filter
exit $LASTEXITCODE
