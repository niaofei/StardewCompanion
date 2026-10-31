[CmdletBinding()]
param([string]$SessionFile='',[long]$After=0,[switch]$Once)
$ErrorActionPreference='Stop'
if(-not $SessionFile){$SessionFile=Join-Path (Split-Path -Parent $PSScriptRoot) '.local/session.json'}
$session=Get-Content -LiteralPath $SessionFile -Raw|ConvertFrom-Json
$headers=@{Authorization='Bearer '+$session.token}
$base=($session.endpoint -replace '^ws:','http:') -replace '/bridge$',''
do{
    $batch=Invoke-RestMethod "$base/agent/events?after=$After&limit=200" -Headers $headers -TimeoutSec 5
    foreach($event in $batch.events){
        $data=$event.Data|ConvertTo-Json -Depth 15 -Compress
        if($event.Kind -eq 'decision_context'){$data='instruction='+$event.Data.instruction+'; location='+$event.Data.observation.Location+'; time='+$event.Data.observation.TimeOfDay}
        Write-Output ('{0} #{1} {2} decision={3} {4}' -f $event.At,$event.Sequence,$event.Kind,$event.DecisionId,$data)
        $After=[long]$event.Sequence
    }
    if(-not $Once){Start-Sleep -Milliseconds 500}
}while(-not $Once)
