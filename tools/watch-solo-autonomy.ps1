[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunRoot,[ValidateRange(1,112)][int]$Days=5,[switch]$Resume)
$ErrorActionPreference='Stop'
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
$session=Get-Content -LiteralPath (Join-Path $RunRoot 'session.json') -Raw|ConvertFrom-Json
$headers=@{Authorization='Bearer '+$session.token}
$base=($session.endpoint -replace '^ws:','http:') -replace '/bridge$',''
function Request($path,$body=$null){
 if($null -eq $body){return Invoke-RestMethod "$base/$path" -Headers $headers -TimeoutSec 10}
 return Invoke-RestMethod "$base/$path" -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes(($body|ConvertTo-Json -Depth 8))) -TimeoutSec 10
}
function Status($phase,$state){@{at=[DateTimeOffset]::Now.ToString('o');phase=$phase;gameDay=$state.GameDay;time=$state.TimeOfDay;runId=(Split-Path -Leaf $RunRoot);trialDays=$Days;mode='deepseek_autonomous';intervention='one startup instruction; no scripted gameplay; player may use monitor controls'}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $RunRoot 'autonomy-status.json') -Encoding UTF8}
$deadline=[DateTime]::UtcNow.AddSeconds(180)
do{$state=Request 'state';if($state.WorldReady){break};Start-Sleep -Milliseconds 500}while([DateTime]::UtcNow -lt $deadline)
if(-not $state.WorldReady -or $state.ControlMode -ne 'solo_test' -or $state.Multiplayer -or (-not $Resume -and $state.GameDay -ne 0) -or $state.TestRunId -ne (Split-Path -Leaf $RunRoot)){throw 'Expected the explicitly selected isolated solo farm'}
$agent=Request 'agent/state';if(-not $agent.PlanningEnabled){throw 'Model planning is disabled'}
$instruction="请在这个全新隔离单人存档中完全自主完成前${Days}天开荒，以真实经营收益为目标并采用滚动贪心，连贯完成种植养护收获、采集、砍树制箱和归仓，自主规划记录并根据实际回执调整，常规操作及绑定自己制作的开荒箱均已授权，继续工作、恢复、睡觉和晕倒代价由你权衡，矿洞相关能力关闭，其余游戏功能均已开放，玩家负责观察及自行操作，完成第${Days}夜原版保存并进入第$($Days+1)天早晨后结束测试。"
$instructionPath=Join-Path $RunRoot 'initial-instruction.txt'
if($Resume){
 if(-not(Test-Path -LiteralPath $instructionPath)){throw 'Existing original instruction is required to resume'}
}else{
 if(Test-Path -LiteralPath $instructionPath){throw 'This run already received its initial instruction; refusing to submit it twice'}
 [IO.File]::WriteAllText($instructionPath,$instruction,[Text.UTF8Encoding]::new($false))
 $null=Request 'agent/goal' @{objective=$instruction;completionCondition=('第'+$Days+'夜保存完成，进入第'+($Days+1)+'天早晨');priorities=@('真实经营收益','种植养护收获','资源积累与归仓');restrictions=@('不进入矿洞');autonomy='除矿洞外全部游戏功能开放，自主权衡工作与休息';disabledSkills=@('mine_travel')}
}
# Only startup control requests. No /task, strategy feedback, or recovery commands.
$null=Request 'agent/resume' @{}
Status 'running' $state
$previous='';$disconnectedAt=$null
while($true){
 Start-Sleep -Seconds 5
 try{$state=Request 'state';$disconnectedAt=$null}catch{
  Status 'connection_lost_no_intervention' $state
  if($null -eq $disconnectedAt){$disconnectedAt=[DateTime]::UtcNow}
  if(([DateTime]::UtcNow-$disconnectedAt).TotalMinutes -ge 5){exit 2}
  continue
 }
 if(-not $state.WorldReady){Status 'waiting_for_world' $state;continue}
 $world=$state.WorldView|ConvertFrom-Json
 $key="$($state.GameDay):$($state.TimeOfDay):$($state.Paused):$($state.Menu)"
 if($key -ne $previous){
  $snapshot=@{at=[DateTimeOffset]::Now.ToString('o');runId=$state.TestRunId;revision=$state.Revision;gameDay=$state.GameDay;time=$state.TimeOfDay;location=$state.Location;tile=$state.Tile;stamina=$state.Stamina;health=$state.Health;paused=$state.Paused;activeTaskId=$state.ActiveTaskId;menu=$state.Menu;menuContext=$state.MenuContext;money=$world.money;inventory=$world.inventory;soil=$world.soil;chests=$world.chests}
  [IO.File]::AppendAllText((Join-Path $RunRoot 'observation-snapshots.jsonl'),(($snapshot|ConvertTo-Json -Depth 15 -Compress)+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))
  $previous=$key
 }
 Status 'running' $state
 if($state.GameDay -ge $Days -and $state.Menu -eq '' -and $state.ActiveTaskId -eq '' -and $state.Paused){Status 'completed_native_days' $state;exit 0}
}
