param(
    [string]$VintageStoryPath = 'E:\Games\Vintagestory',
    [string]$Dotnet = 'dotnet',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if (-not (Test-Path -LiteralPath (Join-Path $VintageStoryPath 'VintagestoryAPI.dll'))) {
    throw "VintagestoryAPI.dll was not found in $VintageStoryPath"
}
& $Dotnet build (Join-Path $PSScriptRoot 'GroundClear.csproj') -c Release "-p:VintageStoryPath=$VintageStoryPath" --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'GroundClear build failed. A .NET 10 SDK is required.' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'tests\GroundClear.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'GroundClear tests failed.' }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$package = Join-Path $OutputDirectory 'GroundClear-1.0.0.zip'
$packageFiles = @(
    (Join-Path $PSScriptRoot 'bin\Release\net10.0\GroundClear.dll'),
    (Join-Path $PSScriptRoot 'modinfo.json'),
    (Join-Path $PSScriptRoot 'README.md')
)
Compress-Archive -LiteralPath $packageFiles -DestinationPath $package -Force
Write-Output "Built: $package"
