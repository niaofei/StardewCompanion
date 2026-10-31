[CmdletBinding()]
param(
    [ValidateSet('Server','Host','AI')][string]$Role = 'Server',
    [string]$GamePath = 'F:\SteamLibrary\steamapps\common\Stardew Valley',
    [switch]$JoinTestFarm,
    [switch]$PrepareFixtures,
    [switch]$PrepareM1,
    [switch]$LogicChecks
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $repoRoot '.local'
New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
if ($Role -eq 'Server') {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = Join-Path $repoRoot '.tools\dotnet\dotnet.exe'
    $start.Arguments = '"' + (Join-Path $repoRoot 'src\StardewCompanion.Diagnostics\bin\Release\net10.0\StardewCompanion.Diagnostics.dll') + '"'
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.EnvironmentVariables['SC_SESSION_FILE'] = Join-Path $localRoot 'session.json'
    $process = [Diagnostics.Process]::Start($start)
    Write-Host "M0 diagnostic server PID $($process.Id), http://127.0.0.1:27841"
    return
}
$start = New-Object Diagnostics.ProcessStartInfo
$start.WorkingDirectory = $GamePath
$start.UseShellExecute = $false
foreach ($name in @('SC_AI_CLIENT','SC_BRIDGE_ENDPOINT','SC_BRIDGE_TOKEN','SC_M0_FIXTURE','SC_M0_FIXTURE_RESULT','SC_M0_PREPARE_FIXTURES','SC_M0_EXPECTED_SAVE','SC_M1_PREPARE','SC_LOGIC_FIXTURE_COMMAND','SC_LOGIC_FIXTURE_RESULT')) {
    $start.EnvironmentVariables.Remove($name)
}
if ($Role -eq 'Host') {
    $start.FileName = Join-Path $GamePath 'Stardew Valley.exe'
} else {
    $sessionPath = Join-Path $localRoot 'session.json'
    if (-not (Test-Path -LiteralPath $sessionPath)) { throw 'Start the M0 server first.' }
    $session = Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    $start.FileName = Join-Path $GamePath 'StardewModdingAPI.exe'
    $start.Arguments = '--mods-path "' + (Join-Path $localRoot 'ai-mods') + '" --no-terminal'
    $start.EnvironmentVariables['SC_AI_CLIENT'] = '1'
    $start.EnvironmentVariables['SC_BRIDGE_ENDPOINT'] = $session.endpoint
    $start.EnvironmentVariables['SC_BRIDGE_TOKEN'] = $session.token
    if($LogicChecks){
        if(-not $JoinTestFarm){throw 'LogicChecks requires the explicit SCM0 join fixture.'}
        $fixture=Get-Content -LiteralPath (Join-Path $localRoot 'm0-fixture.json') -Raw | ConvertFrom-Json
        if($fixture.farm -ne 'SCM0'){throw 'Only the dedicated SCM0 fixture is permitted.'}
        $start.EnvironmentVariables['SC_M0_EXPECTED_SAVE']=$fixture.saveId
        $start.EnvironmentVariables['SC_LOGIC_FIXTURE_COMMAND']=Join-Path $localRoot 'logic-fixture-command.json'
        $start.EnvironmentVariables['SC_LOGIC_FIXTURE_RESULT']=Join-Path $localRoot 'logic-fixture-result.json'
    }
    if ($JoinTestFarm) {
        $start.EnvironmentVariables['SC_M0_FIXTURE'] = 'join'
        $start.EnvironmentVariables['SC_M0_FIXTURE_RESULT'] = Join-Path $localRoot 'm0-live-probe.json'
        if ($PrepareFixtures -or $PrepareM1) {
            $fixture = Get-Content -LiteralPath (Join-Path $localRoot 'm0-fixture.json') -Raw | ConvertFrom-Json
            $start.EnvironmentVariables['SC_M0_PREPARE_FIXTURES'] = '1'
            $start.EnvironmentVariables['SC_M0_EXPECTED_SAVE'] = $fixture.saveId
            if ($PrepareM1) { $start.EnvironmentVariables['SC_M1_PREPARE'] = '1' }
        }
    }
}
# Games are interactive tools: users must see the first-time character/join screens.
$start.WindowStyle = 'Normal'
$process = [Diagnostics.Process]::Start($start)
[pscustomobject]@{ ProcessId=$process.Id; Role=$Role; Executable=$start.FileName } | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $localRoot ($Role.ToLowerInvariant() + '-process.json')) -Encoding UTF8
Write-Host "$Role game PID $($process.Id)"
