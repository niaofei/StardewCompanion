[CmdletBinding()]
param([ValidateSet('state','events','message','pause','resume','cancel','chest')][string]$Operation='state',[string]$Text='',[string]$ArgumentsJson='{}',[long]$After=0)
$ErrorActionPreference='Stop'
$session=Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '.local/session.json') -Raw|ConvertFrom-Json
$headers=@{Authorization='Bearer '+$session.token}
$base=($session.endpoint -replace '^ws:','http:') -replace '/bridge$',''
$uri="$base/agent/$Operation"
if($Operation -eq 'events'){$uri+='?after='+$After+'&limit=200'}
if($Operation -in @('state','events')){$result=Invoke-RestMethod $uri -Headers $headers}
elseif($Operation -eq 'message'){$result=Invoke-RestMethod $uri -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes((@{text=$Text}|ConvertTo-Json)))}
elseif($Operation -eq 'chest'){$result=Invoke-RestMethod $uri -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($ArgumentsJson))}
else{$result=Invoke-RestMethod $uri -Headers $headers -Method Post}
$result|ConvertTo-Json -Depth 25
