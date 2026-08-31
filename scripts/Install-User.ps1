[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [string]$InstallDirectory = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Usage Guard'),

    [string]$SkillSourceDirectory,

    [switch]$InstallCodexIntegration,

    [switch]$LaunchAfterInstall,

    [string]$BackupSuffix = ('backup-' + (Get-Date -Format 'yyyy-MM-dd-HHmmss'))
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$UserProfilePath = [Environment]::GetFolderPath('UserProfile')
$LocalDataPath = [Environment]::GetFolderPath('LocalApplicationData')
$SkillsParent = [IO.Path]::GetFullPath((Join-Path $UserProfilePath '.codex\skills'))
$SkillInstallDirectory = [IO.Path]::GetFullPath((Join-Path $SkillsParent 'codex-usage-guard'))
$LegacyInstallDirectory = [IO.Path]::GetFullPath((Join-Path $UserProfilePath '.codex\tools\codex-usage-guard'))
$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory)
$InstallParent = [IO.Path]::GetFullPath((Split-Path -Parent $InstallDirectory))
$SourceDirectory = [IO.Path]::GetFullPath($SourceDirectory)
$StateDirectory = [IO.Path]::GetFullPath((Join-Path $LocalDataPath 'OpenAI\CodexUsageGuard'))
$LocatorPath = [IO.Path]::GetFullPath((Join-Path $StateDirectory 'installation.json'))
$TargetBackup = "$InstallDirectory.$BackupSuffix"
$LegacyBackup = "$LegacyInstallDirectory.$BackupSuffix"
$SkillBackup = "$SkillInstallDirectory.$BackupSuffix"
$LocatorBackup = "$LocatorPath.$BackupSuffix"
$TargetStage = [IO.Path]::GetFullPath((Join-Path $InstallParent "Usage Guard.installing-$PID"))
$SkillStage = [IO.Path]::GetFullPath((Join-Path $SkillsParent "codex-usage-guard.installing-$PID"))
$TargetExecutable = Join-Path $InstallDirectory 'CodexUsageGuard.exe'
$LegacyExecutable = Join-Path $LegacyInstallDirectory 'CodexUsageGuard.exe'

function Assert-ChildPath {
    param([string]$Path, [string]$Parent)
    $ResolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd('\') + '\'
    if (-not $Path.StartsWith($ResolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing path outside intended directory: $Path"
    }
}

if ($InstallDirectory -eq [IO.Path]::GetPathRoot($InstallDirectory)) {
    throw 'The installation directory cannot be a drive root.'
}
Assert-ChildPath -Path $InstallDirectory -Parent $InstallParent
Assert-ChildPath -Path $TargetStage -Parent $InstallParent
Assert-ChildPath -Path $SkillInstallDirectory -Parent $SkillsParent
Assert-ChildPath -Path $SkillStage -Parent $SkillsParent

$AllowedAppFiles = @(
    'CodexUsageGuard.exe',
    'CodexUsageGuard.dll',
    'CodexUsageGuard.deps.json',
    'CodexUsageGuard.runtimeconfig.json'
)
if (-not (Test-Path -LiteralPath (Join-Path $SourceDirectory 'CodexUsageGuard.exe') -PathType Leaf)) {
    throw 'The verified application executable is missing.'
}
$AppFiles = @(Get-ChildItem -LiteralPath $SourceDirectory -File | Where-Object {
    $_.Name -in $AllowedAppFiles
})

function Assert-OwnedInstallDirectory {
    if (-not (Test-Path -LiteralPath $InstallDirectory)) {
        return
    }
    if (-not (Test-Path -LiteralPath $InstallDirectory -PathType Container)) {
        throw 'The installation destination exists but is not a directory.'
    }

    $Entries = @(Get-ChildItem -LiteralPath $InstallDirectory -Force)
    if ($Entries.Count -eq 0) {
        return
    }
    if (-not (Test-Path -LiteralPath $LocatorPath -PathType Leaf)) {
        throw 'Refusing to replace a non-empty destination that is not owned by Usage Guard.'
    }

    try {
        $Locator = Get-Content -Raw -LiteralPath $LocatorPath | ConvertFrom-Json
    }
    catch {
        throw 'Refusing to replace a non-empty destination with an invalid ownership record.'
    }
    $LocatorExecutablePath = $null
    if ($Locator.executablePath -is [string]) {
        try {
            $LocatorExecutablePath = [IO.Path]::GetFullPath($Locator.executablePath)
        }
        catch {
            $LocatorExecutablePath = $null
        }
    }
    if ($Locator.schemaVersion -ne 1 -or
        $Locator.executablePath -isnot [string] -or
        $Locator.executableSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        -not [IO.Path]::IsPathRooted($Locator.executablePath) -or
        $LocatorExecutablePath -isnot [string] -or
        -not $LocatorExecutablePath.Equals(
            [IO.Path]::GetFullPath($TargetExecutable),
            [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $TargetExecutable -PathType Leaf) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $TargetExecutable).Hash -cne
            $Locator.executableSha256.ToUpperInvariant()) {
        throw 'Refusing to replace a non-empty destination whose ownership record does not match it.'
    }
    foreach ($Entry in $Entries) {
        if ($Entry.PSIsContainer -or
            ($Entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            $Entry.Name -notin $AllowedAppFiles) {
            throw 'Refusing to replace a destination containing files not owned by Usage Guard.'
        }
    }
}

Assert-OwnedInstallDirectory

if ($InstallCodexIntegration) {
    if ([string]::IsNullOrWhiteSpace($SkillSourceDirectory)) {
        throw 'SkillSourceDirectory is required with InstallCodexIntegration.'
    }
    $SkillSourceDirectory = [IO.Path]::GetFullPath($SkillSourceDirectory)
    foreach ($SkillFile in @('SKILL.md', 'scripts\check_usage.ps1', 'scripts\invoke_guard_process.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $SkillSourceDirectory $SkillFile) -PathType Leaf)) {
            throw "Verified Codex integration file is missing: $SkillFile"
        }
    }
}

foreach ($Backup in @($TargetBackup, $LocatorBackup)) {
    if (Test-Path -LiteralPath $Backup) {
        throw "A rollback backup already exists: $Backup"
    }
}
if ($InstallDirectory -ne $LegacyInstallDirectory -and (Test-Path -LiteralPath $LegacyInstallDirectory) -and (Test-Path -LiteralPath $LegacyBackup)) {
    throw "A legacy rollback backup already exists: $LegacyBackup"
}
if ($InstallCodexIntegration -and (Test-Path -LiteralPath $SkillInstallDirectory) -and (Test-Path -LiteralPath $SkillBackup)) {
    throw "A skill rollback backup already exists: $SkillBackup"
}

function Stop-HelperAtPath {
    param([string]$ExecutablePath)
    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        return
    }
    $Owned = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq 'CodexUsageGuard.exe' -and $_.ExecutablePath -eq $ExecutablePath
    })
    if ($Owned.Count -eq 0) {
        return
    }
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
            throw "The existing helper PID $($Process.ProcessId) did not exit cleanly."
        }
    }
    Start-Sleep -Milliseconds 500
}

Stop-HelperAtPath -ExecutablePath $TargetExecutable
if ($LegacyExecutable -ne $TargetExecutable) {
    Stop-HelperAtPath -ExecutablePath $LegacyExecutable
}

$TargetHadExisting = Test-Path -LiteralPath $InstallDirectory
$LegacyHadExisting = $InstallDirectory -ne $LegacyInstallDirectory -and (Test-Path -LiteralPath $LegacyInstallDirectory)
$SkillHadExisting = $InstallCodexIntegration -and (Test-Path -LiteralPath $SkillInstallDirectory)
$LocatorHadExisting = Test-Path -LiteralPath $LocatorPath

New-Item -ItemType Directory -Path $InstallParent -Force | Out-Null
New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
if ($InstallCodexIntegration) {
    New-Item -ItemType Directory -Path $SkillsParent -Force | Out-Null
}

try {
    New-Item -ItemType Directory -Path $TargetStage | Out-Null
    foreach ($File in $AppFiles) {
        Copy-Item -LiteralPath $File.FullName -Destination $TargetStage
        $SourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $File.FullName).Hash
        $StageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $TargetStage $File.Name)).Hash
        if ($SourceHash -ne $StageHash) {
            throw "Staged application hash mismatch: $($File.Name)"
        }
    }

    if ($InstallCodexIntegration) {
        New-Item -ItemType Directory -Path (Join-Path $SkillStage 'scripts') -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $SkillSourceDirectory 'SKILL.md') -Destination $SkillStage
        Copy-Item -LiteralPath (Join-Path $SkillSourceDirectory 'scripts\check_usage.ps1') -Destination (Join-Path $SkillStage 'scripts')
        Copy-Item -LiteralPath (Join-Path $SkillSourceDirectory 'scripts\invoke_guard_process.ps1') -Destination (Join-Path $SkillStage 'scripts')
    }

    if ($TargetHadExisting) {
        Move-Item -LiteralPath $InstallDirectory -Destination $TargetBackup
    }
    Move-Item -LiteralPath $TargetStage -Destination $InstallDirectory
    if ($LegacyHadExisting) {
        Move-Item -LiteralPath $LegacyInstallDirectory -Destination $LegacyBackup
    }

    if ($InstallCodexIntegration) {
        if ($SkillHadExisting) {
            Move-Item -LiteralPath $SkillInstallDirectory -Destination $SkillBackup
        }
        Move-Item -LiteralPath $SkillStage -Destination $SkillInstallDirectory
    }

    if ($LocatorHadExisting) {
        Copy-Item -LiteralPath $LocatorPath -Destination $LocatorBackup
    }
    $ExecutableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $TargetExecutable).Hash.ToLowerInvariant()
    $LocatorTemporary = "$LocatorPath.new"
    $LocatorJson = [ordered]@{
        schemaVersion = 1
        executablePath = $TargetExecutable
        executableSha256 = $ExecutableHash
    } | ConvertTo-Json
    [IO.File]::WriteAllText(
        $LocatorTemporary,
        $LocatorJson,
        [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $LocatorTemporary -Destination $LocatorPath -Force

    $RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $RunName = 'Usage Guard'
    $LegacyRunName = 'OpenAI Codex Usage Guard'
    $ExistingRun = $null
    $ExistingLegacyRun = $null
    if (Test-Path -LiteralPath $RunKey) {
        $RunValues = Get-ItemProperty -LiteralPath $RunKey
        $RunProperty = $RunValues.PSObject.Properties[$RunName]
        $LegacyRunProperty = $RunValues.PSObject.Properties[$LegacyRunName]
        if ($null -ne $RunProperty) {
            $ExistingRun = [string]$RunProperty.Value
        }
        if ($null -ne $LegacyRunProperty) {
            $ExistingLegacyRun = [string]$LegacyRunProperty.Value
        }
    }
    $LegacyRun = '"' + $LegacyExecutable + '" --background'
    if ($ExistingRun -eq $LegacyRun -or $ExistingLegacyRun -eq $LegacyRun) {
        Set-ItemProperty -Path $RunKey -Name $RunName -Value ('"' + $TargetExecutable + '" --background')
        Remove-ItemProperty -Path $RunKey -Name $LegacyRunName -ErrorAction SilentlyContinue
    }
}
catch {
    if (Test-Path -LiteralPath $TargetStage) {
        Remove-Item -LiteralPath $TargetStage -Recurse -Force
    }
    if (Test-Path -LiteralPath $SkillStage) {
        Remove-Item -LiteralPath $SkillStage -Recurse -Force
    }
    if (Test-Path -LiteralPath $InstallDirectory) {
        Remove-Item -LiteralPath $InstallDirectory -Recurse -Force
    }
    if ($TargetHadExisting -and (Test-Path -LiteralPath $TargetBackup)) {
        Move-Item -LiteralPath $TargetBackup -Destination $InstallDirectory
    }
    if ($LegacyHadExisting -and (Test-Path -LiteralPath $LegacyBackup)) {
        Move-Item -LiteralPath $LegacyBackup -Destination $LegacyInstallDirectory
    }
    if ($InstallCodexIntegration) {
        if (Test-Path -LiteralPath $SkillInstallDirectory) {
            Remove-Item -LiteralPath $SkillInstallDirectory -Recurse -Force
        }
        if ($SkillHadExisting -and (Test-Path -LiteralPath $SkillBackup)) {
            Move-Item -LiteralPath $SkillBackup -Destination $SkillInstallDirectory
        }
    }
    if ($LocatorHadExisting -and (Test-Path -LiteralPath $LocatorBackup)) {
        Copy-Item -LiteralPath $LocatorBackup -Destination $LocatorPath -Force
    }
    throw
}

if ($LaunchAfterInstall) {
    Start-Process -FilePath $TargetExecutable -WindowStyle Hidden
}

[pscustomobject]@{
    InstalledTool = $InstallDirectory
    InstalledSkill = if ($InstallCodexIntegration) { $SkillInstallDirectory } else { $null }
    Locator = $LocatorPath
    ExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $TargetExecutable).Hash
    TargetRollback = if ($TargetHadExisting) { $TargetBackup } else { $null }
    LegacyRollback = if ($LegacyHadExisting) { $LegacyBackup } else { $null }
    SkillRollback = if ($SkillHadExisting) { $SkillBackup } else { $null }
    SettingsPreserved = $true
    StartupChangedOnlyIfLegacyEntryExisted = $true
}
