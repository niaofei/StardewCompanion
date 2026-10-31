[CmdletBinding()]
param(
    [ValidateSet('health','state','results','pause','resume','cancel','task')][string]$Operation = 'state',
    [string]$Skill = '',
    [string]$ArgumentsJson = '{}',
    [string]$TaskId = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$session = Get-Content -LiteralPath (Join-Path $repoRoot '.local\session.json') -Raw | ConvertFrom-Json
$requestHeaders = @{ Authorization = 'Bearer ' + $session.token }
$endpoint = 'http://127.0.0.1:27841/' + $Operation
if ($Operation -in @('health','state','results')) {
    Invoke-RestMethod -Uri $endpoint -Headers $requestHeaders | ConvertTo-Json -Depth 12
} elseif ($Operation -eq 'task') {
    $payload = @{ skill=$Skill; arguments=($ArgumentsJson | ConvertFrom-Json) }
    if ($TaskId) { $payload.taskId=$TaskId }
    Invoke-RestMethod -Uri $endpoint -Headers $requestHeaders -Method Post -ContentType 'application/json' -Body ($payload | ConvertTo-Json -Depth 8) | ConvertTo-Json -Depth 8
} else {
    Invoke-RestMethod -Uri $endpoint -Headers $requestHeaders -Method Post | ConvertTo-Json -Depth 8
}
