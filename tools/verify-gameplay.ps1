[CmdletBinding()]
param([int]$ConnectionTimeoutSeconds=120)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$localRoot=Join-Path $repoRoot '.local'
$session=Get-Content -LiteralPath (Join-Path $localRoot 'session.json') -Raw|ConvertFrom-Json
$headers=@{Authorization='Bearer '+$session.token}
$base=($session.endpoint -replace '^ws:','http:') -replace '/bridge$',''
$fixture=Get-Content -LiteralPath (Join-Path $localRoot 'm0-fixture.json') -Raw|ConvertFrom-Json
$evidence=[Collections.Generic.List[object]]::new()
$evidencePath=Join-Path $localRoot 'logic-gameplay-evidence.json'
function Save-Evidence {$evidence.ToArray()|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $evidencePath -Encoding UTF8}
function Assert-Game([bool]$condition,[string]$name,$details){
    $evidence.Add([pscustomobject]@{name=$name;passed=$condition;at=[DateTimeOffset]::Now.ToString('o');details=$details});Save-Evidence
    if(-not $condition){throw "FAIL: $name"};Write-Output "PASS: $name"
}
function State {Invoke-RestMethod "$base/state" -Headers $headers -TimeoutSec 3}
function Control([string]$type){$null=Invoke-RestMethod "$base/$type" -Headers $headers -Method Post -TimeoutSec 3}
function Wait-State([scriptblock]$condition,[int]$seconds=30){
    $until=[DateTime]::UtcNow.AddSeconds($seconds)
    do{$state=State;if($state -and (& $condition $state)){return $state};Start-Sleep -Milliseconds 200}while([DateTime]::UtcNow -lt $until)
    throw 'Timed out waiting for game state'
}
function Start-GameTask([string]$skill,$arguments){
    $id=[Guid]::NewGuid().ToString('N')
    $body=@{skill=$skill;arguments=$arguments;taskId=$id}|ConvertTo-Json -Depth 10
    $null=Invoke-RestMethod "$base/task" -Headers $headers -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 5
    return $id
}
function Wait-GameTask([string]$id,[int]$seconds=90){
    $until=[DateTime]::UtcNow.AddSeconds($seconds)
    do{
        $all=Invoke-RestMethod "$base/results" -Headers $headers -TimeoutSec 3
        $result=$all|Where-Object {$_.TaskId -eq $id -and $_.Status -in @('completed','partial','failed','cancelled','rejected')}|Select-Object -Last 1
        if($result){return $result};Start-Sleep -Milliseconds 200
    }while([DateTime]::UtcNow -lt $until)
    throw "Task $id did not finish"
}
function Run-GameTask([string]$skill,$arguments){
    $result=Wait-GameTask (Start-GameTask $skill $arguments)
    $null=Wait-State {param($s) $s.Revision -gt $result.ObservationRevision} 10
    return $result
}
function Prepare-Fixture([string]$operation){
    $id=[Guid]::NewGuid().ToString('N');$inbox=Join-Path $localRoot 'logic-fixture-command.json';$temp=$inbox+'.tmp'
    @{id=$id;operation=$operation}|ConvertTo-Json|Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $inbox -Force
    $outbox=Join-Path $localRoot 'logic-fixture-result.json';$until=[DateTime]::UtcNow.AddSeconds(10)
    do{
        if(Test-Path -LiteralPath $outbox){$result=Get-Content -LiteralPath $outbox -Raw|ConvertFrom-Json;if($result.id -eq $id){if($result.status -ne 'completed'){throw $result.reason};return $result.details}}
        Start-Sleep -Milliseconds 150
    }while([DateTime]::UtcNow -lt $until)
    throw 'Dedicated test fixture did not respond'
}
try{
    Write-Output 'Waiting for the real AI farmhand to join SCM0...'
    $state=Wait-State {param($s) $s.WorldReady} $ConnectionTimeoutSeconds
    Assert-Game ($state.SaveId -eq $fixture.saveId -and $state.PlayerName -eq 'M0AI' -and -not $state.IsHost) 'real vanilla-host farmhand identity' @{save=$state.SaveId;player=$state.PlayerId}
    Control 'resume'
    $result=Run-GameTask 'go_to' @{location='Farm';x=53;y=17}
    Assert-Game ($result.Status -eq 'completed') 'native walk from cabin to farm' $result
    $sample=Prepare-Fixture 'prepare'
    $result=Run-GameTask 'bind_chest' @{x=$sample.shipping.x;y=$sample.shipping.y}
    Assert-Game ($result.Status -eq 'rejected' -and $result.Reason -eq 'not_supported_work_chest') 'mini shipping bin cannot be authorized as storage' $result
    $bound=Run-GameTask 'bind_chest' @{x=$sample.chest.x;y=$sample.chest.y}
    Assert-Game ($bound.Status -eq 'completed') 'ordinary work chest can be bound' $bound
    $chestId=($bound.Details|ConvertFrom-Json).id
    $result=Run-GameTask 'water_area' @{location='Farm';x=$sample.crop.x;y=$sample.crop.y;width=1;height=1}
    $waterView=(State).WorldView|ConvertFrom-Json
    $watered=$waterView.soil|Where-Object {$_.x -eq $sample.crop.x -and $_.y -eq $sample.crop.y}
    Assert-Game ($result.Status -eq 'completed' -and $result.Completed -eq 1 -and $watered.watered -and $watered.crop -eq '24') 'empty can refills and waters without harvesting a ripe crop' @{result=$result;soil=$watered}
    $filled=Prepare-Fixture 'fill_inventory'
    $result=Run-GameTask 'harvest_area' @{location='Farm';x=$sample.crop.x;y=$sample.crop.y;width=1;height=1}
    $state=State;$view=$state.WorldView|ConvertFrom-Json
    $after=($view.inventory|Where-Object {$_.id -eq '(O)24'}|Measure-Object -Property count -Sum).Sum
    Assert-Game ($filled.full -and $result.Status -eq 'completed' -and $result.Completed -eq ($after-$filled.parsnips) -and $result.Completed -gt 0) 'full-slot inventory accepts stacking harvest and reports the actual increase' @{result=$result;before=$filled.parsnips;after=$after}
    $null=Prepare-Fixture 'clear_fixture_filler'
    $result=Run-GameTask 'harvest_area' @{location='Farm';x=$sample.scytheCrop.x;y=$sample.scytheCrop.y;width=1;height=1}
    Assert-Game ($result.Status -eq 'completed' -and $result.Completed -gt 0) 'scythe harvest collects ground drops before reporting completion' $result
    $storage=@{chestId=$chestId;location='Farm';x=$sample.chest.x;y=$sample.chest.y;itemId='(O)388';count=5;allowedIds=@('(O)388')}
    $result=Run-GameTask 'withdraw_items' $storage
    Assert-Game ($result.Status -eq 'completed' -and $result.Completed -eq 5) 'native chest withdrawal transfers the requested quantity' $result
    $storage.count=150;$id=Start-GameTask 'withdraw_items' $storage
    $null=Wait-State {param($s) $s.ActiveTaskId -eq $id -and $s.Menu -eq 'ItemGrabMenu'} 20
    Control 'cancel'
    $result=Wait-GameTask $id
    $state=Wait-State {param($s) $s.ActiveTaskId -eq '' -and $s.Menu -eq ''} 10
    Assert-Game ($result.Status -eq 'cancelled') 'cancelling storage closes the owned menu and stops transfer' $result
    $result=Run-GameTask 'return_home' @{}
    Assert-Game ($result.Status -eq 'completed') 'native return-home succeeds after storage cancellation' $result
    Write-Output 'Labor and cancellation checks passed. Starting native sleep; vanilla host must also go to bed.'
    $day=(State).GameDay;$id=Start-GameTask 'sleep' @{}
    $sleepState=Wait-State {param($s) $s.Menu -eq 'ReadyCheckDialog' -or $s.GameDay -gt $day} 60
    Assert-Game ($sleepState.GameDay -gt $day -or $sleepState.Menu -eq 'ReadyCheckDialog') 'AI reaches its own bed and enters normal multiplayer readiness' @{day=$sleepState.GameDay;menu=$sleepState.Menu;task=$id}
    $next=Wait-State {param($s) $s.GameDay -gt $day} 120
    Assert-Game ($next.GameDay -gt $day) 'vanilla host and AI finish a synchronized new day' @{before=$day;after=$next.GameDay}
}catch{
    $evidence.Add([pscustomobject]@{name='run_stopped';passed=$false;at=[DateTimeOffset]::Now.ToString('o');details=$_.Exception.Message});Save-Evidence
    try{Control 'pause'}catch{}
    throw
}
