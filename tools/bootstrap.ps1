[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $repoRoot '.tools'
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
$sdkVersion = '10.0.403'
$sdkHash = '7a114c318fa7f1d9822991b534b2814d0e78386ceda816b371f5141acefe741d87ccb606ede06cadac8f9ac5070e32b708a67bbd1619ff5e5a7858a27874ef48'
$sdkArchive = Join-Path $toolRoot "dotnet-sdk-$sdkVersion.zip"
$sdkRoot = Join-Path $toolRoot 'dotnet'
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'dotnet.exe'))) {
    Write-Host "Downloading local .NET SDK $sdkVersion"
    & curl.exe --fail --location --retry 2 --silent --show-error --output $sdkArchive "https://builds.dotnet.microsoft.com/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.zip"
    if ($LASTEXITCODE -ne 0) { throw 'SDK download failed' }
    if ((Get-FileHash -LiteralPath $sdkArchive -Algorithm SHA512).Hash.ToLowerInvariant() -ne $sdkHash) { throw 'SDK SHA512 mismatch' }
    Expand-Archive -LiteralPath $sdkArchive -DestinationPath $sdkRoot -Force
}
& (Join-Path $sdkRoot 'dotnet.exe') --version
$smapiArchive = Join-Path $toolRoot 'SMAPI-4.5.2-installer.zip'
$smapiRoot = Join-Path $toolRoot 'smapi-installer'
if (-not (Test-Path -LiteralPath $smapiRoot)) {
    Write-Host 'Downloading SMAPI 4.5.2 installer (not installing into the game yet)'
    & curl.exe --fail --location --retry 2 --silent --show-error --output $smapiArchive 'https://github.com/Pathoschild/SMAPI/releases/download/4.5.2/SMAPI-4.5.2-installer.zip'
    if ($LASTEXITCODE -ne 0) { throw 'SMAPI download failed' }
    Expand-Archive -LiteralPath $smapiArchive -DestinationPath $smapiRoot
}
Write-Host "Tools ready in $toolRoot"
