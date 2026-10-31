[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RunRoot,[string]$OutputPath='')
$ErrorActionPreference='Stop'
$RunRoot=[IO.Path]::GetFullPath($RunRoot)
if(-not $OutputPath){$OutputPath=Join-Path $RunRoot 'prompt-samples.md'}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if(-not $OutputPath.StartsWith($RunRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Output must stay inside the selected run directory'}
$log=Join-Path $RunRoot 'logs/decisions.jsonl'
if(-not(Test-Path -LiteralPath $log)){$log=Join-Path $RunRoot 'decisions.jsonl'}
if(-not(Test-Path -LiteralPath $log)){throw 'Decision log not found'}
$captures=@(Get-Content -LiteralPath $log | ForEach-Object {try{$_|ConvertFrom-Json}catch{}} | Where-Object Kind -eq 'model_prompt_capture' | Sort-Object At)
if($captures.Count -eq 0){throw 'No raw model prompt captures are available yet'}
$actions=@($captures|Where-Object {$_.Data.kind -eq 'daily_action'}|Sort-Object {[int]$_.Data.gameDay})
$maintenance=@($captures|Where-Object {$_.Data.kind -eq 'sleep_maintenance'}|Sort-Object {[int]$_.Data.gameDay})
$selected=[Collections.Generic.List[object]]::new()
if($actions.Count){$selected.Add($actions[0])}
if($actions.Count -gt 2){$selected.Add($actions[[int][Math]::Floor(($actions.Count-1)/2)])}
if($actions.Count -gt 1){$selected.Add($actions[-1])}
if($maintenance.Count){$selected.Add($maintenance[0])}
if($maintenance.Count -gt 1){$selected.Add($maintenance[-1])}
foreach($pattern in @('inventory_full','unchanged_target_previously_failed','duplicate_not_executed','mail_arrived','pass_out')){
    $match=$captures|Where-Object { ($_.Data.prompt|ConvertTo-Json -Depth 100 -Compress) -match $pattern }|Select-Object -First 1
    if($match){$selected.Add($match)}
}
$unique=@($selected|Sort-Object Sequence -Unique)
$builder=[Text.StringBuilder]::new()
[void]$builder.AppendLine('# LLM 实际请求原文样本')
[void]$builder.AppendLine()
[void]$builder.AppendLine("测试目录：``$RunRoot``  ")
[void]$builder.AppendLine("来源：``$log``  ")
[void]$builder.AppendLine('以下每个代码块保留完整模型 JSON 请求体，包括 messages、tools 和模型参数；兼容旧记录仅保存 messages/tools。JSON 排版会改变，字段值不改写。')
[void]$builder.AppendLine()
$index=0
foreach($event in $unique){
    $index++
    $kind=if($event.Data.kind -eq 'sleep_maintenance'){'睡后季节进度维护'}else{'日常行动决策'}
    [void]$builder.AppendLine("## 样本 $index：$kind · 游戏日 $($event.Data.gameDay)")
    [void]$builder.AppendLine()
    [void]$builder.AppendLine("时间：$($event.At) · 决策 ID：``$($event.DecisionId)`` · 请求 ID：``$($event.Data.requestId)`` · 轮次：$($event.Data.round)")
    [void]$builder.AppendLine()
    $json=$event.Data.prompt|ConvertTo-Json -Depth 100
    [void]$builder.AppendLine('````json')
    [void]$builder.AppendLine($json)
    [void]$builder.AppendLine('````')
    [void]$builder.AppendLine()
}
[IO.File]::WriteAllText($OutputPath,$builder.ToString(),[Text.UTF8Encoding]::new($false))
Write-Output "Saved $($unique.Count) original prompt samples to $OutputPath"
