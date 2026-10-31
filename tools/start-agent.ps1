[CmdletBinding()]
param([string]$SettingsFile='',[string]$DataDirectory='')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $SettingsFile){$SettingsFile=Join-Path $repoRoot '.local/model.credentials.json'}
if(-not $DataDirectory){$DataDirectory=Join-Path $repoRoot ('.local/llm-run-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
if(-not(Test-Path -LiteralPath $SettingsFile)){throw 'Model settings file does not exist'}
New-Item -ItemType Directory -Path $DataDirectory -Force|Out-Null
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
$start.Arguments='"'+(Join-Path $repoRoot 'src/StardewCompanion.Diagnostics/bin/Release/net10.0/StardewCompanion.Diagnostics.dll')+'" --agent'
$start.WorkingDirectory=$repoRoot;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$start.EnvironmentVariables['SC_SESSION_FILE']=Join-Path $repoRoot '.local/session.json'
$start.EnvironmentVariables['SC_MODEL_SETTINGS']=[IO.Path]::GetFullPath($SettingsFile)
$start.EnvironmentVariables['SC_AGENT_DATA']=[IO.Path]::GetFullPath($DataDirectory)
$process=[Diagnostics.Process]::Start($start)
@{processId=$process.Id;dataDirectory=[IO.Path]::GetFullPath($DataDirectory)}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $repoRoot '.local/agent-process.json') -Encoding UTF8
Write-Output "Agent PID $($process.Id); starts paused; decisions: $DataDirectory/decisions.jsonl"
