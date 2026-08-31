[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InputRoot,
    [Parameter(Mandatory = $true)][string]$EvidenceRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$InputRoot = [IO.Path]::GetFullPath($InputRoot)
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
$ResultPath = Join-Path $EvidenceRoot 'qa-result.json'
$ReadyPath = Join-Path $EvidenceRoot 'ready-for-host-capture.json'
$HostCaptureFlag = Join-Path $EvidenceRoot 'host-capture-complete.flag'
$UiEvidence = Join-Path $EvidenceRoot 'guest-ui'
$TestExecutable = Join-Path $InputRoot 'tests\CodexUsageGuard.Tests.exe'
$AppSource = Join-Path $InputRoot 'app'
$InstallScript = Join-Path $InputRoot 'Install-User.ps1'
$RollbackScript = Join-Path $InputRoot 'Rollback-User.ps1'
$ManifestPath = Join-Path $InputRoot 'sandbox-input-manifest.json'
$InstalledDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Usage Guard Sandbox QA'
$InstalledExecutable = Join-Path $InstalledDirectory 'CodexUsageGuard.exe'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace UsageGuard.GuestQa {
    public static class ProcessNative {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
        public static int ReadExitCode(IntPtr process) {
            uint exitCode;
            if (!GetExitCodeProcess(process, out exitCode)) {
                throw new InvalidOperationException("process_exit_code_unavailable");
            }
            return unchecked((int)exitCode);
        }
    }
}
'@

function Write-AtomicJson {
    param([string]$Path, [object]$Value)
    $Temporary = $Path + '.new'
    [IO.File]::WriteAllText(
        $Temporary,
        ($Value | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $Temporary -Destination $Path -Force
}

function Invoke-OwnedProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [int]$TimeoutMilliseconds
    )
    $Out = Join-Path $env:TEMP ('UsageGuard-QA-' + [guid]::NewGuid().ToString('N') + '.out')
    $Err = $Out + '.err'
    $ArgumentLine = (($Arguments | ForEach-Object {
        $Value = [string]$_
        if ($Value -match '[\s"]') {
            '"' + $Value.Replace('"', '\"') + '"'
        }
        else { $Value }
    }) -join ' ')
    $Process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentLine `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput $Out `
        -RedirectStandardError $Err
    $NativeHandle = [IntPtr]$Process.Handle
    try {
        if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
            Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
            throw 'A guest QA process exceeded its hard timeout.'
        }
        $Process.WaitForExit()
        $Process.Refresh()
        $NativeExitCode = [UsageGuard.GuestQa.ProcessNative]::ReadExitCode($NativeHandle)
        [pscustomobject]@{
            ExitCode = $NativeExitCode
            StandardOutput = if (Test-Path -LiteralPath $Out) {
                [IO.File]::ReadAllText($Out)
            } else { '' }
            StandardError = if (Test-Path -LiteralPath $Err) {
                [IO.File]::ReadAllText($Err)
            } else { '' }
        }
    }
    finally {
        Remove-Item -LiteralPath $Out, $Err -Force -ErrorAction SilentlyContinue
        $Process.Dispose()
    }
}

function Save-ExactGuestWindow {
    param([Diagnostics.Process]$Process, [string]$OutputPath)
    Add-Type -AssemblyName System.Drawing
    if (-not ('UsageGuard.GuestQa.Native' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace UsageGuard.GuestQa {
    public static class Native {
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    }
}
'@
    }
    $Process.Refresh()
    $Hwnd = $Process.MainWindowHandle
    if ($Hwnd -eq [IntPtr]::Zero -or
        -not [IO.Path]::GetFullPath($Process.MainModule.FileName).Equals(
            [IO.Path]::GetFullPath($InstalledExecutable),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The exact guest Usage Guard window is unavailable.'
    }
    $Rect = [UsageGuard.GuestQa.Native+RECT]::new()
    if (-not [UsageGuard.GuestQa.Native]::GetWindowRect($Hwnd, [ref]$Rect)) {
        throw 'The guest Usage Guard frame could not be read.'
    }
    $Width = $Rect.Right - $Rect.Left
    $Height = $Rect.Bottom - $Rect.Top
    if ($Width -lt 560 -or $Height -lt 620) {
        throw 'The guest Usage Guard frame is unexpectedly small.'
    }
    $Bitmap = [Drawing.Bitmap]::new($Width, $Height)
    $Graphics = [Drawing.Graphics]::FromImage($Bitmap)
    $Hdc = $Graphics.GetHdc()
    try {
        if (-not [UsageGuard.GuestQa.Native]::PrintWindow($Hwnd, $Hdc, 2)) {
            throw 'The guest Usage Guard capture failed.'
        }
    }
    finally {
        $Graphics.ReleaseHdc($Hdc)
        $Graphics.Dispose()
    }
    try { $Bitmap.Save($OutputPath, [Drawing.Imaging.ImageFormat]::Png) }
    finally { $Bitmap.Dispose() }
    $Hwnd
}

$AppProcess = $null
$TestCount = 0
$InstallVerified = $false
$RollbackVerified = $false
$CurrentStep = 'input_validation'
$SyntheticFailureNames = @()
$TestProcessExitCode = $null
$FailureType = $null
$FailureId = $null
$FailureHResult = $null
$InstallExitCode = $null
$InstalledExecutablePresent = $false
$InstalledHashMatches = $false
$RollbackExitCode = $null
$InstallDirectoryPresentAfterRollback = $null
$RollbackFailureCode = $null
try {
    if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container) -or
        @(Get-ChildItem -LiteralPath $EvidenceRoot -Force).Count -ne 0) {
        throw 'The writable guest evidence folder was not newly empty.'
    }
    foreach ($Required in @(
        $TestExecutable,
        (Join-Path $AppSource 'CodexUsageGuard.exe'),
        $InstallScript,
        $RollbackScript,
        $ManifestPath)) {
        if (-not (Test-Path -LiteralPath $Required -PathType Leaf)) {
            throw 'A staged read-only QA input is missing.'
        }
    }
    $Manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    if ($Manifest.schemaVersion -ne 1 -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $TestExecutable).Hash -cne
            ([string]$Manifest.testExecutableSha256).ToUpperInvariant() -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $AppSource 'CodexUsageGuard.exe')).Hash -cne
            ([string]$Manifest.appExecutableSha256).ToUpperInvariant()) {
        throw 'The staged Sandbox QA input manifest does not match.'
    }

    $CurrentStep = 'guest_local_stage'
    $GuestWorkRoot = 'C:\UsageGuardQA\Work'
    if (Test-Path -LiteralPath $GuestWorkRoot) {
        throw 'The guest-local QA work folder was not initially absent.'
    }
    New-Item -ItemType Directory -Path $GuestWorkRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $InputRoot 'tests') `
        -Destination (Join-Path $GuestWorkRoot 'tests') -Recurse
    Copy-Item -LiteralPath (Join-Path $InputRoot 'app') `
        -Destination (Join-Path $GuestWorkRoot 'app') -Recurse
    Copy-Item -LiteralPath $InstallScript, $RollbackScript `
        -Destination $GuestWorkRoot
    $TestExecutable = Join-Path $GuestWorkRoot 'tests\CodexUsageGuard.Tests.exe'
    $AppSource = Join-Path $GuestWorkRoot 'app'
    $InstallScript = Join-Path $GuestWorkRoot 'Install-User.ps1'
    $RollbackScript = Join-Path $GuestWorkRoot 'Rollback-User.ps1'
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $TestExecutable).Hash -cne
            ([string]$Manifest.testExecutableSha256).ToUpperInvariant() -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $AppSource 'CodexUsageGuard.exe')).Hash -cne
            ([string]$Manifest.appExecutableSha256).ToUpperInvariant()) {
        throw 'The guest-local QA copy did not match the staged manifest.'
    }

    $CurrentStep = 'synthetic_tests'
    $Tests = Invoke-OwnedProcess -FilePath $TestExecutable `
        -Arguments @('--sandbox-core-tests') `
        -TimeoutMilliseconds 180000
    $TestProcessExitCode = $Tests.ExitCode
    if ($Tests.ExitCode -ne 0 -or
        $Tests.StandardOutput.Trim() -notmatch '^PASS ([0-9]+) synthetic tests$') {
        $SyntheticFailureNames = @($Tests.StandardError -split "`r?`n" |
            Where-Object { $_ -match '^FAIL [a-zA-Z0-9 .-]{1,120}$' } |
            ForEach-Object { $_.Substring(5) })
        throw 'The isolated synthetic test suite failed.'
    }
    $TestCount = [int]$Matches[1]

    $CurrentStep = 'ui_render'
    New-Item -ItemType Directory -Path $UiEvidence | Out-Null
    $Render = Invoke-OwnedProcess -FilePath $TestExecutable `
        -Arguments @('--render-ui-evidence', $UiEvidence) `
        -TimeoutMilliseconds 120000
    if ($Render.ExitCode -ne 0 -or
        -not (Test-Path -LiteralPath (Join-Path $UiEvidence 'ui-evidence.json') -PathType Leaf)) {
        throw 'The isolated UI evidence render failed.'
    }

    $CurrentStep = 'user_install'
    $PowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $Install = Invoke-OwnedProcess -FilePath $PowerShell -Arguments @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', $InstallScript,
        '-SourceDirectory', $AppSource,
        '-InstallDirectory', $InstalledDirectory,
        '-BackupSuffix', 'sandbox-qa'
    ) -TimeoutMilliseconds 120000
    $InstallExitCode = $Install.ExitCode
    $InstalledExecutablePresent = Test-Path -LiteralPath $InstalledExecutable -PathType Leaf
    $InstalledHashMatches = $InstalledExecutablePresent -and
        (Get-FileHash -Algorithm SHA256 -LiteralPath $InstalledExecutable).Hash -ceq
            ([string]$Manifest.appExecutableSha256).ToUpperInvariant()
    if ($InstallExitCode -ne 0 -or -not $InstalledHashMatches) {
        throw 'The isolated user-scoped install did not verify.'
    }
    $InstallVerified = $true

    $CurrentStep = 'popup_launch'
    $env:USAGE_GUARD_SANDBOX_QA_SESSION = '1'
    $AppProcess = Start-Process -FilePath $InstalledExecutable `
        -ArgumentList '--sandbox-layout-qa' -PassThru
    $Deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $AppProcess.Refresh()
    } while ($AppProcess.MainWindowHandle -eq [IntPtr]::Zero -and
        -not $AppProcess.HasExited -and [DateTime]::UtcNow -lt $Deadline)
    if ($AppProcess.HasExited) { throw 'The isolated popup exited before inspection.' }

    $CurrentStep = 'guest_capture'
    $GuestCodexCapture = Join-Path $EvidenceRoot 'guest-production-codex.png'
    $Hwnd = Save-ExactGuestWindow -Process $AppProcess -OutputPath $GuestCodexCapture
    [void][UsageGuard.GuestQa.Native]::PostMessage($Hwnd, 0x8502, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 500
    $GuestClaudeCapture = Join-Path $EvidenceRoot 'guest-production-claude.png'
    [void](Save-ExactGuestWindow -Process $AppProcess -OutputPath $GuestClaudeCapture)

    $CurrentStep = 'host_capture_wait'
    Write-AtomicJson -Path $ReadyPath -Value ([ordered]@{
        schemaVersion = 1
        state = 'ready_for_exact_host_capture'
        syntheticTestCount = $TestCount
        installVerified = $InstallVerified
        guestEvidenceGenerated = $true
        networkRequired = $false
        modelTaskCreated = $false
    })

    $FlagDeadline = [DateTime]::UtcNow.AddMinutes(5)
    while (-not (Test-Path -LiteralPath $HostCaptureFlag -PathType Leaf) -and
        [DateTime]::UtcNow -lt $FlagDeadline) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $HostCaptureFlag -PathType Leaf)) {
        throw 'The bounded host capture acknowledgement did not arrive.'
    }

    [void][UsageGuard.GuestQa.Native]::PostMessage(
        $AppProcess.MainWindowHandle,
        0x0010,
        [IntPtr]::Zero,
        [IntPtr]::Zero)
    if (-not $AppProcess.WaitForExit(10000)) {
        Stop-Process -Id $AppProcess.Id -Force
        $AppProcess.WaitForExit()
    }
    $AppProcess.Dispose()
    $AppProcess = $null

    $CurrentStep = 'user_rollback'
    $Rollback = Invoke-OwnedProcess -FilePath $PowerShell -Arguments @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', $RollbackScript,
        '-RemoveSanitizedState',
        '-Confirm:$false'
    ) -TimeoutMilliseconds 120000
    $RollbackExitCode = $Rollback.ExitCode
    if ($RollbackExitCode -ne 0) {
        $RollbackFailureCode = if ($Rollback.StandardError -match 'being used by another process') {
            'installed_file_still_in_use'
        }
        elseif ($Rollback.StandardError -match 'installation locator is missing') {
            'locator_missing'
        }
        elseif ($Rollback.StandardError -match 'executable hash changed') {
            'locator_hash_mismatch'
        }
        elseif ($Rollback.StandardError -match 'Cannot bind argument.*Path') {
            'optional_path_unavailable'
        }
        elseif ($Rollback.StandardError -match 'confirmation') {
            'confirmation_unavailable'
        }
        else { 'unclassified_rollback_failure' }
    }
    $InstallDirectoryPresentAfterRollback = Test-Path -LiteralPath $InstalledDirectory
    $RollbackVerified = $RollbackExitCode -eq 0 -and
        -not $InstallDirectoryPresentAfterRollback
    if (-not $RollbackVerified) { throw 'The isolated rollback did not verify.' }

    $ScreenshotFiles = @(Get-ChildItem -LiteralPath $EvidenceRoot -Filter '*.png' -Recurse)
    Write-AtomicJson -Path $ResultPath -Value ([ordered]@{
        schemaVersion = 1
        status = 'passed'
        syntheticTestCount = $TestCount
        installVerified = $InstallVerified
        rollbackVerified = $RollbackVerified
        screenshotCount = $ScreenshotFiles.Count
        guestGeneratedAtUtc = [DateTimeOffset]::UtcNow
        networkEnabled = $false
        clipboardEnabled = $false
        vGpuEnabled = $false
        modelTaskCreated = $false
    })
}
catch {
    $FailureType = $_.Exception.GetType().Name
    $FailureHResult = $_.Exception.HResult
    $FailureId = ([string]$_.FullyQualifiedErrorId -replace '[^a-zA-Z0-9,._-]', '_')
    if ($FailureId.Length -gt 160) { $FailureId = $FailureId.Substring(0, 160) }
    Write-AtomicJson -Path $ResultPath -Value ([ordered]@{
        schemaVersion = 1
        status = 'failed'
        failure = 'guest_qa_failed_safely'
        failureStep = $CurrentStep
        syntheticTestCount = $TestCount
        installVerified = $InstallVerified
        rollbackVerified = $RollbackVerified
        syntheticFailureNames = $SyntheticFailureNames
        testProcessExitCode = $TestProcessExitCode
        failureType = $FailureType
        failureId = $FailureId
        failureHResult = $FailureHResult
        installExitCode = $InstallExitCode
        installedExecutablePresent = $InstalledExecutablePresent
        installedHashMatches = $InstalledHashMatches
        rollbackExitCode = $RollbackExitCode
        installDirectoryPresentAfterRollback = $InstallDirectoryPresentAfterRollback
        rollbackFailureCode = $RollbackFailureCode
        guestGeneratedAtUtc = [DateTimeOffset]::UtcNow
    })
}
finally {
    if ($null -ne $AppProcess) {
        try {
            if (-not $AppProcess.HasExited) { Stop-Process -Id $AppProcess.Id -Force }
        } catch { }
        $AppProcess.Dispose()
    }
    Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\shutdown.exe') `
        -ArgumentList '/s', '/t', '0' -WindowStyle Hidden | Out-Null
}
