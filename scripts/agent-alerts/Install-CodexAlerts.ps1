[CmdletBinding()]
param(
    [string]$NodePath,
    [string]$CodexConfigDirectory = (Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([string]::IsNullOrWhiteSpace($NodePath)) {
    $NodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
    if ($NodeCommand) { $NodePath = $NodeCommand.Source }
    else {
        $NodePath = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe'
    }
}
$NodePath = [IO.Path]::GetFullPath($NodePath)
$ReceiverPath = Join-Path $PSScriptRoot 'usage-guard-alert.mjs'
foreach ($ExecutableFile in @($NodePath, $ReceiverPath)) {
    if ($ExecutableFile -match '["%!?\r\n]' -or -not (Test-Path -LiteralPath $ExecutableFile -PathType Leaf)) {
        throw 'Node or the receiver is missing, or its path cannot safely be quoted. Install Node and pass -NodePath with its absolute path.'
    }
}
$CodexConfigDirectory = [IO.Path]::GetFullPath($CodexConfigDirectory)
New-Item -ItemType Directory -Path $CodexConfigDirectory -Force | Out-Null
$HooksPath = Join-Path $CodexConfigDirectory 'hooks.json'
$Configuration = [pscustomobject]@{ hooks = [pscustomobject]@{} }
if (Test-Path -LiteralPath $HooksPath) {
    $HooksFile = Get-Item -LiteralPath $HooksPath
    if ($HooksFile.Attributes -band [IO.FileAttributes]::ReparsePoint -or $HooksFile.Length -gt 1048576) {
        throw 'Refusing a redirected or oversized hook configuration.'
    }
    $Configuration = Get-Content -LiteralPath $HooksPath -Raw | ConvertFrom-Json
}
if (-not $Configuration -or $Configuration -isnot [pscustomobject]) { throw 'Invalid hook configuration.' }
if (-not $Configuration.PSObject.Properties['hooks']) {
    $Configuration | Add-Member -NotePropertyName hooks -NotePropertyValue ([pscustomobject]@{})
}
if ($Configuration.hooks -isnot [pscustomobject]) { throw 'Invalid hooks object; existing file was not changed.' }
$Command = '"' + $NodePath + '" "' + $ReceiverPath + '"'
$Changed = $false
foreach ($EventName in @('PreToolUse', 'PostToolUse')) {
    $Entries = @()
    if ($Configuration.hooks.PSObject.Properties[$EventName]) { $Entries = @($Configuration.hooks.$EventName) }
    $Present = $false
    foreach ($Entry in $Entries) {
        foreach ($Handler in @($Entry.hooks)) {
            if ($Handler.PSObject.Properties['command'] -and $Handler.command -like '*usage-guard-alert.mjs*') {
                if ($Handler.command -ne $Command -or $Entry.PSObject.Properties['matcher'] -or
                    $Handler.type -ne 'command' -or $Handler.timeout -ne 3 -or $Handler.additionalContextLimit -ne 500) {
                    throw 'A different Usage Guard hook already exists. Review it before migration; no hooks were changed.'
                }
                $Present = $true
            }
        }
    }
    if (-not $Present) {
        $Entries += [pscustomobject]@{ hooks = @([pscustomobject]@{
            type = 'command'; command = $Command; timeout = 3; additionalContextLimit = 500
        }) }
        $Configuration.hooks | Add-Member -NotePropertyName $EventName -NotePropertyValue $Entries -Force
        $Changed = $true
    }
}
if ($Changed) {
    $Before = if (Test-Path -LiteralPath $HooksPath) { [IO.File]::ReadAllBytes($HooksPath) } else { $null }
    $Temporary = Join-Path $CodexConfigDirectory ('hooks.' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($Temporary, ($Configuration | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
        if ($null -ne $Before) {
            $Backup = $HooksPath + '.backup-' + [guid]::NewGuid().ToString('N')
            [IO.File]::Replace($Temporary, $HooksPath, $Backup)
        } else { [IO.File]::Move($Temporary, $HooksPath) }
    } finally {
        if (Test-Path -LiteralPath $Temporary) { Remove-Item -LiteralPath $Temporary }
    }
}
Write-Output 'Configured both Usage Guard hooks. No trust records, settings or credentials were changed.'
Write-Output 'Next: run codex in PowerShell, then enter /hooks INSIDE Codex. Review and trust only these PreToolUse/PostToolUse entries. Keep Usage Guard monitoring on.'
