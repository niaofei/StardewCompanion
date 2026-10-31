[CmdletBinding()]
param([string]$RunRoot='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not $RunRoot){$RunRoot=(Get-Content (Join-Path $repo '.local/solo5-current.json') -Raw|ConvertFrom-Json).runRoot}
$session=Get-Content (Join-Path $RunRoot 'session.json') -Raw|ConvertFrom-Json
$headers=@{Authorization='Bearer '+$session.token};$base=($session.endpoint -replace '^ws:','http:') -replace '/bridge$',''
$cases=[Collections.Generic.List[object]]::new()
function Request($path,$body=$null){
 if($null -eq $body){return Invoke-RestMethod "$base/$path" -Headers $headers -TimeoutSec 10}
 return Invoke-RestMethod "$base/$path" -Headers $headers -Method Post -ContentType 'application/json' -Body ($body|ConvertTo-Json -Depth 25 -Compress) -TimeoutSec 10
}
function State {Request 'state'}
function View {((State).WorldView|ConvertFrom-Json)}
function Scene($name){
 $null=Request 'pause' @{};$id=[Guid]::NewGuid().ToString('N');$file=Join-Path $RunRoot 'lab-control.json'
 @{id=$id;scene=$name}|ConvertTo-Json|Set-Content -LiteralPath $file -Encoding UTF8
 $end=[DateTime]::UtcNow.AddSeconds(20)
 do{Start-Sleep -Milliseconds 300;if(Test-Path ($file+'.result')){$ready=Get-Content ($file+'.result') -Raw|ConvertFrom-Json;if($ready.id -eq $id){break}}}while([DateTime]::UtcNow -lt $end)
 if($ready.id -ne $id -or $ready.status -ne 'ready'){throw 'Fixture scene did not become ready'}
 Start-Sleep -Milliseconds 600;$null=Request 'resume' @{}
}
function Task($skill,$arguments,[int]$seconds=180){
 $id=[Guid]::NewGuid().ToString('N');$null=Request 'task' @{taskId=$id;skill=$skill;arguments=$arguments}
 $end=[DateTime]::UtcNow.AddSeconds($seconds);$result=$null
 do{Start-Sleep -Milliseconds 200;$result=(Request 'results')|Where-Object {$_.TaskId -eq $id -and $_.Status -in @('completed','partial','failed','rejected','cancelled')}|Select-Object -Last 1;if($result){break}}while([DateTime]::UtcNow -lt $end)
 if(-not $result){$null=Request 'cancel' @{};throw "Native task timeout: $skill"}
 Start-Sleep -Milliseconds 350
 $cases.Add(@{skill=$skill;arguments=$arguments;result=$result;at=[DateTimeOffset]::Now.ToString('o')})
 $cases|ConvertTo-Json -Depth 30|Set-Content (Join-Path $RunRoot 'business-cases.json') -Encoding UTF8
 Write-Host ($skill+': '+$result.Status+' '+$result.Reason+' count='+$result.Completed)
 return $result
}
$end=[DateTime]::UtcNow.AddSeconds(90)
do{Start-Sleep -Milliseconds 500;$s=State}while(-not $s.WorldReady -and [DateTime]::UtcNow -lt $end)
if($s.ControlMode -ne 'solo_test'){throw 'Expected isolated solo lab'}
Scene 'farming'
$r=Task 'survey_area' @{location='Farm';x=60;y=20;width=5;height=3}
$r=Task 'cultivate_plot' @{location='Farm';x=60;y=20;width=5;height=3;seedId='(O)472';plantCount=15;fertilizerId='(O)368';clearPolicy='debris';allowPartial=$false;waterPolicy='water_now'}
if($r.Status -ne 'completed'){throw 'Native composite cultivation failed; inspect business-cases.json'}
$soil=@((View).soil|Where-Object {$_.x -ge 60 -and $_.x -lt 65 -and $_.y -ge 20 -and $_.y -lt 23})
if(@($soil|Where-Object {$_.crop -eq '24' -and $_.watered}).Count -ne 15){throw 'Composite cultivation did not verify 15 planted and watered cells'}
Scene 'farming'
$r=Task 'cultivate_plot' @{location='Farm';x=60;y=20;width=5;height=3;seedId='(O)472';plantCount=15;fertilizerId=$null;clearPolicy='none';allowPartial=$false;waterPolicy='skip_today'}
if($r.Status -ne 'completed'){throw 'Skip-water cultivation failed'}
$soil=@((View).soil|Where-Object {$_.x -ge 60 -and $_.x -lt 65 -and $_.y -ge 20 -and $_.y -lt 23})
if(@($soil|Where-Object {$_.crop -eq '24' -and -not $_.watered}).Count -ne 15){throw 'Skip-water policy was not respected'}
Scene 'trees'
$r=Task 'gather_resource' @{location='Farm';x=66;y=19;width=9;height=4;itemId='(O)388';count=10;sources=@('wild_trees','small_stumps');removeStumps=$true}
if($r.Status -ne 'completed'){throw 'Native tree gathering failed'}
Scene 'production'
$r=Task 'craft_items' @{recipe='Chest';count=2;sourceChests=@()}
if($r.Status -ne 'completed'){throw 'Generic native crafting failed'}
$r=Task 'craft_and_place' @{recipe='Chest';itemId='(BC)130';sourceChests=@();location='Farm';tiles=@(@{x=66;y=22})}
if($r.Status -ne 'completed'){throw 'Craft and place failed'}
$r=Task 'service_machines' @{location='Farm';mode='load';targets=@(@{x=68;y=20;inputId='(O)24';quality=0;fuelId=$null})}
if($r.Status -ne 'completed'){throw 'Native machine loading failed'}
Scene 'recovery'
$r=Task 'recover' @{method='food';itemId='(O)194';quality=0;maxItems=3;targetStamina=60;targetHealth=100;maxGameMinutes=120}
if($r.Status -ne 'completed'){throw 'Native food recovery failed'}
$null=Request 'pause' @{}
Write-Output "Native lab checks complete: $RunRoot/business-cases.json"
