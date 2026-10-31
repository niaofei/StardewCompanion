[CmdletBinding()]
param([string]$ApplicationDirectory='')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if([string]::IsNullOrWhiteSpace($ApplicationDirectory)){$ApplicationDirectory=Join-Path $repoRoot '.local\release-preview\StardewCompanion'}
$ApplicationDirectory=[IO.Path]::GetFullPath($ApplicationDirectory)
$exe=Join-Path $ApplicationDirectory 'StardewCompanion.exe'
if(-not(Test-Path -LiteralPath $exe)){throw "找不到已发布的程序：$exe"}
$desktop=[Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$shortcutPath=Join-Path $desktop '星露谷 AI 陪玩.lnk'
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath=$exe
$shortcut.WorkingDirectory=$ApplicationDirectory
$shortcut.Description='启动星露谷 AI 协同陪玩'
$shortcut.Save()
Write-Output $shortcutPath
