# Build, test and (optionally) publish Diagnostiq.
#   .\build.ps1            build + test
#   .\build.ps1 -Publish   build + test + portable exe in .\publish
param([switch]$Publish, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet build Diagnostiq.slnx -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if (-not $SkipTests) {
    dotnet test Diagnostiq.slnx -c Release --no-build
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

if ($Publish) {
    dotnet publish src/Diagnostiq/Diagnostiq.csproj -p:PublishProfile=win-x64
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
    Get-Item publish/Diagnostiq.exe | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
}
