[CmdletBinding()]
param([string]$SessionFile='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $SessionFile){$current=Get-Content -LiteralPath (Join-Path $repo '.local/solo5-current.json') -Raw|ConvertFrom-Json;$SessionFile=$current.sessionFile}
$start=[Diagnostics.ProcessStartInfo]::new()
$start.FileName=Join-Path $repo '.tools/dotnet/dotnet.exe'
$start.Arguments='"'+(Join-Path $repo 'src/StardewCompanion.Desktop/bin/Release/net10.0-windows/StardewCompanion.dll')+'" --monitor "'+[IO.Path]::GetFullPath($SessionFile)+'"'
$start.WorkingDirectory=$repo;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
$p=[Diagnostics.Process]::Start($start)
Write-Output "Decision monitor and player controls PID $($p.Id)"
