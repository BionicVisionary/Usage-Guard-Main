[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$RemoveCodexIntegration,
    [switch]$RemoveSanitizedState,
    [string]$RestoreLegacyToolBackup,
    [string]$RestoreSkillBackup
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$UserProfilePath = [Environment]::GetFolderPath('UserProfile')
$LocalDataPath = [Environment]::GetFolderPath('LocalApplicationData')
$StateDirectory = [IO.Path]::GetFullPath((Join-Path $LocalDataPath 'OpenAI\CodexUsageGuard'))
$LocatorPath = [IO.Path]::GetFullPath((Join-Path $StateDirectory 'installation.json'))
$SkillsParent = [IO.Path]::GetFullPath((Join-Path $UserProfilePath '.codex\skills'))
$SkillInstallDirectory = [IO.Path]::GetFullPath((Join-Path $SkillsParent 'codex-usage-guard'))
$LegacyParent = [IO.Path]::GetFullPath((Join-Path $UserProfilePath '.codex\tools'))
$LegacyInstallDirectory = [IO.Path]::GetFullPath((Join-Path $LegacyParent 'codex-usage-guard'))
$ProgramsPath = [Environment]::GetFolderPath('Programs')
$ShortcutDirectory = if ([string]::IsNullOrWhiteSpace($ProgramsPath)) {
    $null
}
else {
    [IO.Path]::GetFullPath((Join-Path $ProgramsPath 'Usage Guard'))
}

function Assert-ChildPath {
    param([string]$Path, [string]$Parent)
    if (-not $Path.StartsWith($Parent.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing path outside helper-owned directory: $Path"
    }
}

if (-not (Test-Path -LiteralPath $LocatorPath -PathType Leaf)) {
    throw 'The helper installation locator is missing; refusing to guess an install path.'
}
$Locator = Get-Content -Raw -LiteralPath $LocatorPath | ConvertFrom-Json
if ($Locator.schemaVersion -ne 1 -or $Locator.executablePath -isnot [string] -or
    $Locator.executableSha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw 'The helper installation locator is invalid.'
}
$ExecutablePath = [IO.Path]::GetFullPath($Locator.executablePath)
$InstallDirectory = [IO.Path]::GetFullPath((Split-Path -Parent $ExecutablePath))
if ([IO.Path]::GetFileName($ExecutablePath) -cne 'CodexUsageGuard.exe' -or
    -not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw 'The located helper executable is unavailable.'
}
$ActualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExecutablePath).Hash
if ($ActualHash -ne $Locator.executableSha256) {
    throw 'The located helper executable hash changed; refusing automatic removal.'
}
if ($InstallDirectory -eq [IO.Path]::GetPathRoot($InstallDirectory)) {
    throw 'Refusing to remove a drive root.'
}

function Remove-OwnedLaunchTogetherShortcuts {
    if ([string]::IsNullOrWhiteSpace($ShortcutDirectory)) { return }
    if (-not (Test-Path -LiteralPath $ShortcutDirectory -PathType Container)) { return }
    $Shell = New-Object -ComObject WScript.Shell
    try {
        foreach ($Definition in @(
            @{ Name = 'Usage Guard + Codex.lnk'; Arguments = '--launch-provider codex'; Description = 'Start Usage Guard and Codex together' },
            @{ Name = 'Usage Guard + Claude.lnk'; Arguments = '--launch-provider claude'; Description = 'Start Usage Guard and Claude together' }
        )) {
            $Path = Join-Path $ShortcutDirectory $Definition.Name
            if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { continue }
            $Shortcut = $Shell.CreateShortcut($Path)
            try {
                if ($Shortcut.TargetPath -is [string] -and
                    [IO.Path]::IsPathRooted($Shortcut.TargetPath) -and
                    [IO.Path]::GetFullPath($Shortcut.TargetPath) -eq $ExecutablePath -and
                    $Shortcut.Arguments -ceq $Definition.Arguments -and
                    $Shortcut.Description -ceq $Definition.Description) {
                    Remove-Item -LiteralPath $Path -Force
                }
            }
            finally {
                [Runtime.InteropServices.Marshal]::FinalReleaseComObject($Shortcut) | Out-Null
            }
        }
    }
    finally {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($Shell) | Out-Null
    }
    if ((Get-ChildItem -LiteralPath $ShortcutDirectory -Force).Count -eq 0) {
        Remove-Item -LiteralPath $ShortcutDirectory -Force
    }
}

$Owned = @(Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'CodexUsageGuard.exe' -and $_.ExecutablePath -eq $ExecutablePath
})
if ($Owned.Count -gt 0) {
    $Shutdown = Start-Process -FilePath $ExecutablePath -ArgumentList '--shutdown' -PassThru -WindowStyle Hidden
    if (-not $Shutdown.WaitForExit(20000)) {
        throw 'The helper shutdown request did not return in time.'
    }
    if ($Shutdown.ExitCode -ne 0) {
        throw "The helper rejected graceful shutdown with exit code $($Shutdown.ExitCode)."
    }
    foreach ($Process in $Owned) {
        Wait-Process -Id $Process.ProcessId -Timeout 10 -ErrorAction SilentlyContinue
        if (Get-Process -Id $Process.ProcessId -ErrorAction SilentlyContinue) {
            throw "The helper PID $($Process.ProcessId) did not exit cleanly."
        }
    }
    Start-Sleep -Milliseconds 500
}
else {
    # A just-exited WinExe can disappear from process inventory before Windows
    # releases the final executable mapping and notification-area resources.
    Start-Sleep -Milliseconds 500
}

if ($PSCmdlet.ShouldProcess($InstallDirectory, 'Remove Usage Guard installation')) {
    Remove-OwnedLaunchTogetherShortcuts
    Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
    Remove-Item -LiteralPath $LocatorPath -Force
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Usage Guard' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'OpenAI Codex Usage Guard' -ErrorAction SilentlyContinue
}

if ($RemoveCodexIntegration -and (Test-Path -LiteralPath $SkillInstallDirectory)) {
    Assert-ChildPath -Path $SkillInstallDirectory -Parent $SkillsParent
    if ($PSCmdlet.ShouldProcess($SkillInstallDirectory, 'Remove optional Codex skill integration')) {
        Remove-Item -LiteralPath $SkillInstallDirectory -Recurse -Force
    }
}

if (-not [string]::IsNullOrWhiteSpace($RestoreLegacyToolBackup)) {
    $RestoreLegacyToolBackup = [IO.Path]::GetFullPath($RestoreLegacyToolBackup)
    Assert-ChildPath -Path $RestoreLegacyToolBackup -Parent $LegacyParent
    Assert-ChildPath -Path $LegacyInstallDirectory -Parent $LegacyParent
    if (Test-Path -LiteralPath $LegacyInstallDirectory) {
        throw 'The legacy tool destination already exists.'
    }
    if ($PSCmdlet.ShouldProcess($RestoreLegacyToolBackup, 'Restore prior Codex Usage Guard tool')) {
        Move-Item -LiteralPath $RestoreLegacyToolBackup -Destination $LegacyInstallDirectory
    }
}

if (-not [string]::IsNullOrWhiteSpace($RestoreSkillBackup)) {
    $RestoreSkillBackup = [IO.Path]::GetFullPath($RestoreSkillBackup)
    Assert-ChildPath -Path $RestoreSkillBackup -Parent $SkillsParent
    if (Test-Path -LiteralPath $SkillInstallDirectory) {
        throw 'The Codex skill destination already exists.'
    }
    if ($PSCmdlet.ShouldProcess($RestoreSkillBackup, 'Restore prior Codex skill')) {
        Move-Item -LiteralPath $RestoreSkillBackup -Destination $SkillInstallDirectory
    }
}

if ($RemoveSanitizedState -and (Test-Path -LiteralPath $StateDirectory)) {
    $StateParent = [IO.Path]::GetFullPath((Join-Path $LocalDataPath 'OpenAI'))
    Assert-ChildPath -Path $StateDirectory -Parent $StateParent
    if ($PSCmdlet.ShouldProcess($StateDirectory, 'Remove sanitized settings and state')) {
        Remove-Item -LiteralPath $StateDirectory -Recurse -Force
    }
}

[pscustomobject]@{
    RemovedInstallation = -not (Test-Path -LiteralPath $InstallDirectory)
    RemovedStartup = $true
    RemovedLaunchTogetherShortcuts = $true
    RemovedCodexIntegration = [bool]$RemoveCodexIntegration
    RemovedSanitizedState = [bool]$RemoveSanitizedState
    GlobalAgentsChanged = $false
}
