[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference='Stop'
$archivePath=[IO.Path]::GetFullPath($Archive)
$archiveHash=(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
$sidecar=Get-Content -LiteralPath ($archivePath+'.sha256.json') -Raw|ConvertFrom-Json
if($archiveHash -ne $sidecar.Hash){throw 'Outer archive SHA256 does not match sidecar.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
try{
    $names=@($zip.Entries|ForEach-Object{ $_.FullName.Replace('\','/') })
    foreach($required in @('StardewCompanion.exe','玩家快速开始.md','Mods/StardewCompanion.Bridge/StardewCompanion.Mod.dll','Mods/StardewCompanion.Bridge/StardewCompanion.Protocol.dll','Mods/StardewCompanion.Bridge/manifest.json','SHA256SUMS.json')){
        if($names -notcontains $required){throw "Required package file is missing: $required"}
    }
    if(@($names|Where-Object{$_ -match 'Fixture\.dll|TestJoinAssist'}).Count -gt 0){throw 'A development fixture is included in the package.'}
    $reader=[IO.StreamReader]::new($zip.GetEntry('SHA256SUMS.json').Open())
    $checksums=ConvertFrom-Json -InputObject $reader.ReadToEnd();$reader.Dispose()
    $sha=[Security.Cryptography.SHA256]::Create()
    foreach($record in $checksums){
        $entryName=[string]$record.path;$entryName=$entryName.Replace('/',[string][char]92)
        $entry=$zip.GetEntry($entryName)
        if($null -eq $entry){throw "Hashed entry is missing: $entryName"}
        $stream=$entry.Open();$hash=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','');$stream.Dispose()
        if($hash -ne $record.sha256){throw "File hash mismatch: $entryName"}
    }
    $sha.Dispose()
    [pscustomobject]@{Archive=$archivePath;SHA256=$archiveHash;Entries=$names.Count;HashedFiles=@($checksums).Count;DevelopmentFixtureIncluded=$false;Desktop='self-contained win-x64'}|ConvertTo-Json
}finally{$zip.Dispose()}
