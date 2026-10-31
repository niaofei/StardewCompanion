[CmdletBinding()]
param([int]$ConnectionTimeoutSeconds=900,[int]$RunTimeoutSeconds=1200)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$localRoot=Join-Path $repoRoot '.local'
$session=Get-Content -LiteralPath (Join-Path $localRoot 'session.json') -Raw|ConvertFrom-Json
$headers=@{Authorization='Bearer '+$session.token}
$base=($session.endpoint -replace '^ws:','http:') -replace '/bridge$',''
$fixture=Get-Content -LiteralPath (Join-Path $localRoot 'm0-fixture.json') -Raw|ConvertFrom-Json
$statusPath=Join-Path $localRoot 'llm-trial-status.json'
function Set-TrialStatus($phase,$details){@{at=[DateTimeOffset]::Now.ToString('o');phase=$phase;details=$details}|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $statusPath -Encoding UTF8;Write-Output $phase}
function Request($path,$body=$null){
    if($null -eq $body){return Invoke-RestMethod "$base/$path" -Headers $headers -TimeoutSec 10}
    return Invoke-RestMethod "$base/$path" -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($body|ConvertTo-Json -Depth 12))) -TimeoutSec 10
}
try{
    Set-TrialStatus 'waiting_for_host' @{saveId=$fixture.saveId}
    $deadline=[DateTime]::UtcNow.AddSeconds($ConnectionTimeoutSeconds)
    $nextJoinRetry=[DateTime]::UtcNow.AddSeconds(10)
    do{
        $state=Request 'state';if($state.WorldReady){break}
        if([DateTime]::UtcNow -ge $nextJoinRetry){
            $nextJoinRetry=[DateTime]::UtcNow.AddSeconds(90)
            # The join fixture's first LAN attempt can time out before the host opens a save.
            # Retry only our own designated test client, after a local vanilla server is listening.
            if(Get-NetUDPEndpoint -LocalPort 24642 -ErrorAction SilentlyContinue){
                $metadata=Get-Content -LiteralPath (Join-Path $localRoot 'ai-process.json') -Raw|ConvertFrom-Json
                $client=Get-Process -Id $metadata.ProcessId -ErrorAction SilentlyContinue
                if($client -and [IO.Path]::GetFileName($client.Path) -eq 'StardewModdingAPI.exe'){
                    $null=$client.CloseMainWindow();if(-not $client.WaitForExit(8000)){throw 'Test client did not exit normally for a join retry'}
                }
                & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'start-m0.ps1') -Role AI -JoinTestFarm -LogicChecks
                if($LASTEXITCODE -ne 0){throw 'Could not restart the designated join client'}
            }
        }
        Start-Sleep -Seconds 1
    }while([DateTime]::UtcNow -lt $deadline)
    if(-not $state.WorldReady){throw 'AI did not join the test world before the connection deadline'}
    if($state.SaveId -ne $fixture.saveId -or $state.IsHost -or $state.PlayerName -ne 'M0AI'){throw 'Refusing to prepare or operate outside the dedicated SCM0 farmhand'}
    Set-TrialStatus 'preparing_samples' @{day=$state.GameDay}
    # Only setup is scripted. All labor and storage after this phase is selected by the actual model.
    $null=Request 'resume' @{}
    $task=[Guid]::NewGuid().ToString('N');$null=Request 'task' @{skill='go_to';arguments=@{location='Farm';x=53;y=17};taskId=$task}
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    do{
        $all=Request 'results';$result=$all|Where-Object {$_.TaskId -eq $task -and $_.Status -in @('completed','failed','partial','rejected')}|Select-Object -Last 1
        if($result){break};Start-Sleep -Milliseconds 200
    }while([DateTime]::UtcNow -lt $deadline)
    if($result.Status -ne 'completed'){throw ('Setup navigation failed: '+$result.Reason)}
    do{Start-Sleep -Milliseconds 200;$state=Request 'state'}while($state.Location -ne 'Farm')
    $id=[Guid]::NewGuid().ToString('N');$inbox=Join-Path $localRoot 'logic-fixture-command.json';$temp=$inbox+'.tmp'
    @{id=$id;operation='prepare'}|ConvertTo-Json|Set-Content -LiteralPath $temp -Encoding UTF8;Move-Item -LiteralPath $temp -Destination $inbox -Force
    $deadline=[DateTime]::UtcNow.AddSeconds(15)
    do{
        $outbox=Join-Path $localRoot 'logic-fixture-result.json'
        if(Test-Path -LiteralPath $outbox){$prepared=Get-Content -LiteralPath $outbox -Raw|ConvertFrom-Json;if($prepared.id -eq $id){break}}
        Start-Sleep -Milliseconds 200
    }while([DateTime]::UtcNow -lt $deadline)
    if($prepared.id -ne $id -or $prepared.status -ne 'completed'){throw ('Sample preparation failed: '+$prepared.reason)}
    $sample=$prepared.details
    # Resume permits explicit operator chest binding. The goal immediately invalidates any older idle planning.
    $null=Request 'agent/resume' @{}
    $null=Request 'agent/chest' @{name='自主测试工作箱';x=$sample.chest.x;y=$sample.chest.y;allowedIds=@('(O)24','(O)262','(O)178','(O)16','(O)388');protectedIds=@()}
    $points=@($sample.crop,$sample.scytheCrop,$sample.forage,$sample.dryCrop)
    $minX=($points.x|Measure-Object -Minimum).Minimum;$maxX=($points.x|Measure-Object -Maximum).Maximum
    $minY=($points.y|Measure-Object -Minimum).Minimum;$maxY=($points.y|Measure-Object -Maximum).Maximum
    $goal="本次是已授权的自主陪玩全流程试运行。暂时不能挖矿、钓鱼、战斗、交易、播种和清障，不要尝试或请求绕过。当前 Farm 测试区域 x=$minX 到 $maxX、y=$minY 到 $maxY。请自行观察、决定顺序并持续推进：给区域内未浇水的存活作物浇水（水不足时用补水技能），收获成熟作物，采集地面采集物，将本次取得的允许物品存入已授权的《自主测试工作箱》，最后正常返家上床等待房主换日。箱子坐标为 $($sample.chest.x),$($sample.chest.y)，所有工具受保护。不需要逐步征求许可；遇到容量不足先归仓，受阻时查看实际回执并调整。只依据观察和真实完成回执汇报，accepted 不代表完成。每次选择下一步时简短说明目标和观察依据。请现在开始自主执行，后续无需玩家逐项派发。"
    $null=Request 'agent/message' @{text=$goal}
    $initialDay=(Request 'state').GameDay
    $after=0;$completed=[Collections.Generic.List[object]]::new();$modelTasks=[Collections.Generic.HashSet[string]]::new();$hostNotified=$false;$dayChangedAt=$null
    Set-TrialStatus 'running' @{initialDay=$initialDay;goal=$goal;sample=$sample}
    $deadline=[DateTime]::UtcNow.AddSeconds($RunTimeoutSeconds)
    do{
        $batch=Request "agent/events?after=$after&limit=200"
        foreach($event in $batch.events){
            $after=[long]$event.Sequence
            if($event.Kind -eq 'task_dispatch' -and $event.Data.source -eq 'model'){$null=$modelTasks.Add([string]$event.Data.taskId)}
            if($event.Kind -eq 'task_result' -and $modelTasks.Contains([string]$event.Data.TaskId) -and $event.Data.Status -in @('completed','partial') -and $event.Data.Skill -in @('water_area','harvest_area','forage_area','deposit_items','return_home','sleep')){
                $completed.Add($event.Data);Write-Output ($event.Data.Skill+' '+$event.Data.Status+' '+$event.Data.Completed)
            }
            if($event.Kind -eq 'model_reply'){Write-Output ('LLM: '+$event.Data.text)}
            if($event.Kind -in @('tool_rejected','model_error')){Write-Output ($event.Kind+': '+($event.Data|ConvertTo-Json -Compress))}
        }
        $state=Request 'state'
        if($state.Menu -eq 'ReadyCheckDialog' -and -not $hostNotified){Set-TrialStatus 'waiting_for_host_sleep' @{completed=$completed.ToArray()};$hostNotified=$true}
        if($state.GameDay -gt $initialDay){
            if($null -eq $dayChangedAt){$dayChangedAt=[DateTime]::UtcNow}
            $sleepDone=@($completed|Where-Object {$_.Skill -eq 'sleep' -and $_.Status -eq 'completed' -and $_.Reason -eq 'new_day_observed'}).Count -gt 0
            # Date observation and sleep terminal receipt arrive asynchronously. Allow the receipt to arrive.
            if(-not $sleepDone -and ([DateTime]::UtcNow-$dayChangedAt).TotalSeconds -lt 5){Start-Sleep -Milliseconds 200;continue}
            $null=Request 'agent/pause' @{}
            $required=@('water_area','harvest_area','forage_area','deposit_items')
            $missing=@($required|Where-Object {$skill=$_;-not($completed|Where-Object {$_.Skill -eq $skill -and $_.Completed -gt 0})})
            if(-not $sleepDone){$missing+=@('sleep:new_day_observed')}
            Set-TrialStatus $(if($missing.Count -eq 0){'completed'}else{'incomplete'}) @{beforeDay=$initialDay;afterDay=$state.GameDay;missingSkills=$missing;completed=$completed.ToArray()}
            return
        }
        Start-Sleep -Seconds 1
    }while([DateTime]::UtcNow -lt $deadline)
    throw 'Autonomy run reached its monitoring deadline; inspect decision events for the actual stop reason'
}catch{
    try{$null=Request 'agent/pause' @{}}catch{}
    Set-TrialStatus 'stopped' @{reason=$_.Exception.Message}
    throw
}
