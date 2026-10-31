[CmdletBinding()]
param([string]$Directory='')
$ErrorActionPreference='Stop'
$workspace=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($Directory)){$Directory=Join-Path $workspace '.local\release-preview\StardewCompanion'}
$exe=Join-Path $Directory 'StardewCompanion.exe'
if(-not(Test-Path -LiteralPath $exe)){throw "Preview app is missing: $exe"}
$dataRoot=Join-Path $workspace ('.local\preview-smoke-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataRoot|Out-Null
$start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$exe;$start.WorkingDirectory=$Directory;$start.UseShellExecute=$false;$start.WindowStyle='Normal'
$start.Arguments='--data "'+$dataRoot+'"'
$process=[Diagnostics.Process]::Start($start)
try{
    $ready=$false
    for($i=0;$i -lt 40;$i++){
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if($process.HasExited){throw 'Self-contained desktop exited before its local service started.'}
        $sessionPath=Join-Path $dataRoot 'session.json'
        if(-not(Test-Path -LiteralPath $sessionPath)){continue}
        try{
            $session=Get-Content -LiteralPath $sessionPath -Raw|ConvertFrom-Json
            $headers=@{Authorization='Bearer '+$session.token}
            $health=Invoke-RestMethod 'http://127.0.0.1:27841/health' -Headers $headers -TimeoutSec 1
            if($health.stage -eq 'development'){$ready=$true;break}
        }catch{}
    }
    if(-not $ready){throw 'Self-contained desktop did not start its authenticated local service.'}
    $process.Refresh()
    [pscustomobject]@{Started=$true;WindowTitle=$process.MainWindowTitle;AuthenticatedService=$ready;SessionFile=(Join-Path $dataRoot 'session.json')}|ConvertTo-Json
}finally{
    if(-not $process.HasExited){$process.CloseMainWindow()|Out-Null;$null=$process.WaitForExit(8000)}
}
