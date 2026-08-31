[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [switch]$StopExistingExactInstance,

    [switch]$LeaveOpen,

    [string]$TargetScreenDeviceName = '\\.\DISPLAY1',

    [switch]$AllowSinglePrimaryDisplay,

    [ValidateRange(0, 30)]
    [int]$StabilizationSeconds = 8,

    [ValidateRange(2, 30)]
    [int]$SampleSeconds = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf) -or
    [IO.Path]::GetFileName($ExecutablePath) -cne 'CodexUsageGuard.exe') {
    throw 'ExecutablePath must identify an existing CodexUsageGuard.exe.'
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class UsageGuardWindowProbe
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
        int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y,
        int width, int height, bool repaint);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute,
        out RECT value, int valueSize);

    public static bool GetVisibleWindowRect(IntPtr hWnd, out RECT rect)
    {
        if (DwmGetWindowAttribute(hWnd, 9, out rect, Marshal.SizeOf(typeof(RECT))) == 0)
            return true;
        return GetWindowRect(hWnd, out rect);
    }

    public static IntPtr FindForProcess(int processId)
    {
        IntPtr found = IntPtr.Zero;
        long largestArea = 0;
        EnumWindows((window, ignored) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == (uint)processId && GetWindowRect(window, out RECT rect)) {
                long width = Math.Max(0, rect.Right - rect.Left);
                long height = Math.Max(0, rect.Bottom - rect.Top);
                long area = width * height;
                if (area > largestArea) { largestArea = area; found = window; }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
Add-Type -AssemblyName System.Windows.Forms

$Screens = @([Windows.Forms.Screen]::AllScreens)
$TargetScreen = $Screens | Where-Object {
    $_.DeviceName -ceq $TargetScreenDeviceName
} | Select-Object -First 1
if ($null -eq $TargetScreen -or ($TargetScreen.Primary -and
        (-not $AllowSinglePrimaryDisplay -or $Screens.Count -ne 1))) {
    throw 'The target must be non-primary, unless the user explicitly allowed the only connected primary display.'
}
$QaArgument = if ($TargetScreen.Primary) {
    '--layout-qa-single-display'
} else {
    '--layout-qa-display'
}

function Move-WindowToTargetScreen {
    param([IntPtr]$WindowHandle)
    $rect = [UsageGuardWindowProbe+RECT]::new()
    if (-not [UsageGuardWindowProbe]::GetWindowRect($WindowHandle, [ref]$rect)) {
        throw 'Could not read the Usage Guard window bounds.'
    }
    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $area = $TargetScreen.WorkingArea
    $x = $area.Left + [Math]::Max(0, [Math]::Floor(($area.Width - $width) / 2))
    $y = $area.Top + [Math]::Max(0, [Math]::Floor(($area.Height - $height) / 2))
    if (-not [UsageGuardWindowProbe]::MoveWindow(
            $WindowHandle, $x, $y, $width, $height, $false)) {
        throw 'Could not position Usage Guard on the approved test display.'
    }
}

function Assert-WindowOnTargetScreen {
    param([IntPtr]$WindowHandle)
    $rect = [UsageGuardWindowProbe+RECT]::new()
    if (-not [UsageGuardWindowProbe]::GetVisibleWindowRect($WindowHandle, [ref]$rect)) {
        throw 'Could not verify the Usage Guard window bounds.'
    }
    $bounds = $TargetScreen.WorkingArea
    if ($rect.Left -lt $bounds.Left -or $rect.Top -lt $bounds.Top -or
        $rect.Right -gt $bounds.Right -or $rect.Bottom -gt $bounds.Bottom) {
        throw "Usage Guard was not fully contained on the approved test display. Window=($($rect.Left),$($rect.Top),$($rect.Right),$($rect.Bottom)); Screen=($($bounds.Left),$($bounds.Top),$($bounds.Right),$($bounds.Bottom))."
    }
}

function Test-WindowOnTargetScreen {
    param([IntPtr]$WindowHandle)
    $rect = [UsageGuardWindowProbe+RECT]::new()
    if (-not [UsageGuardWindowProbe]::GetVisibleWindowRect($WindowHandle, [ref]$rect)) {
        return $false
    }
    $bounds = $TargetScreen.WorkingArea
    return $rect.Left -ge $bounds.Left -and $rect.Top -ge $bounds.Top -and
        $rect.Right -le $bounds.Right -and $rect.Bottom -le $bounds.Bottom
}

function Get-ExactProcesses {
    @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -eq 'CodexUsageGuard.exe' -and
        $_.ExecutablePath -eq $ExecutablePath
    })
}

function Wait-ExactProcessExit {
    param([int]$ProcessId, [int]$TimeoutMilliseconds = 15000)
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
            return $true
        }
        Start-Sleep -Milliseconds 50
    }
    return $false
}

function Request-ExactShutdown {
    $owned = @(Get-ExactProcesses)
    if ($owned.Count -eq 0) {
        return
    }
    $request = Start-Process -FilePath $ExecutablePath -ArgumentList '--shutdown' `
        -PassThru -WindowStyle Hidden
    if (-not $request.WaitForExit(17000)) {
        throw 'The bounded shutdown request did not return.'
    }
    if ($request.ExitCode -ne 0) {
        throw "The helper rejected graceful shutdown with exit code $($request.ExitCode)."
    }
    foreach ($item in $owned) {
        if (-not (Wait-ExactProcessExit -ProcessId $item.ProcessId)) {
            throw "The exact helper PID $($item.ProcessId) did not exit gracefully."
        }
    }
}

function Start-VisibleCandidate {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $started = Start-Process -FilePath $ExecutablePath `
        -ArgumentList @($QaArgument, $TargetScreen.DeviceName) `
        -PassThru
    $ready = $null
    $window = [IntPtr]::Zero
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        $candidate = Get-Process -Id $started.Id -ErrorAction SilentlyContinue
        if ($candidate) {
            $candidate.Refresh()
            $window = [UsageGuardWindowProbe]::FindForProcess($candidate.Id)
            if ($window -ne [IntPtr]::Zero -and $candidate.Responding -and
                [UsageGuardWindowProbe]::IsWindowVisible($window) -and
                (Test-WindowOnTargetScreen -WindowHandle $window)) {
                $ready = $candidate
                break
            }
        }
        Start-Sleep -Milliseconds 25
    }
    $watch.Stop()
    if (-not $ready) {
        throw 'The visible Usage Guard QA popup did not become responsive within 15 seconds.'
    }
    Assert-WindowOnTargetScreen -WindowHandle $window
    [pscustomobject]@{
        Process = $ready
        WindowHandle = $window
        ReadyMilliseconds = [Math]::Round($watch.Elapsed.TotalMilliseconds, 1)
    }
}

function Measure-ProcessSample {
    param([Diagnostics.Process]$Process)
    $Process.Refresh()
    $cpuStart = $Process.TotalProcessorTime
    $watch = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds $SampleSeconds
    $Process.Refresh()
    $watch.Stop()
    $cpuDelta = ($Process.TotalProcessorTime - $cpuStart).TotalSeconds
    $cpuPercent = 100 * $cpuDelta /
        ($watch.Elapsed.TotalSeconds * [Environment]::ProcessorCount)
    [pscustomobject]@{
        CpuPercent = [Math]::Round($cpuPercent, 3)
        WorkingSetMiB = [Math]::Round($Process.WorkingSet64 / 1MB, 2)
        PrivateMemoryMiB = [Math]::Round($Process.PrivateMemorySize64 / 1MB, 2)
        Threads = @($Process.Threads).Count
        Handles = $Process.HandleCount
        Responding = $Process.Responding
    }
}

function Measure-GpuSample {
    param([int]$ProcessId)
    try {
        $counter = Get-Counter -Counter '\GPU Engine(*)\Utilization Percentage' `
            -SampleInterval 1 -MaxSamples 3 -ErrorAction Stop
        $samples = @($counter.CounterSamples | Where-Object {
            $_.InstanceName -match "(^|_)pid_$ProcessId(_|$)"
        })
        if ($samples.Count -eq 0) {
            return [pscustomobject]@{ Available = $true; AveragePercent = 0.0; PeakPercent = 0.0 }
        }
        $totals = @($samples | Group-Object Timestamp | ForEach-Object {
            ($_.Group | Measure-Object CookedValue -Sum).Sum
        })
        return [pscustomobject]@{
            Available = $true
            AveragePercent = [Math]::Round(($totals | Measure-Object -Average).Average, 3)
            PeakPercent = [Math]::Round(($totals | Measure-Object -Maximum).Maximum, 3)
        }
    }
    catch {
        return [pscustomobject]@{
            Available = $false
            AveragePercent = $null
            PeakPercent = $null
        }
    }
}

$existing = @(Get-ExactProcesses)
if ($existing.Count -gt 0) {
    if (-not $StopExistingExactInstance) {
        throw 'An exact Usage Guard instance is already running. Use -StopExistingExactInstance for a controlled restart.'
    }
    Request-ExactShutdown
}

$cold = $null
$restart = $null
$finalProcess = $null
try {
    $cold = Start-VisibleCandidate
    $primary = $cold.Process
    if ($StabilizationSeconds -gt 0) {
        Start-Sleep -Seconds $StabilizationSeconds
    }
    $active = Measure-ProcessSample -Process $primary
    $gpu = Measure-GpuSample -ProcessId $primary.Id

    $secondLaunchWatch = [Diagnostics.Stopwatch]::StartNew()
    $secondary = Start-Process -FilePath $ExecutablePath -PassThru
    if (-not $secondary.WaitForExit(5000)) {
        throw 'The ordinary secondary launch did not exit after signaling the primary.'
    }
    $secondLaunchWatch.Stop()
    if (@(Get-ExactProcesses).Count -ne 1) {
        throw 'Ordinary concurrent launch violated the single-instance contract.'
    }

    $primary.Refresh()
    [UsageGuardWindowProbe]::ShowWindow($primary.MainWindowHandle, 6) | Out-Null
    Start-Sleep -Milliseconds 250
    $reopenWatch = [Diagnostics.Stopwatch]::StartNew()
    $reopen = Start-Process -FilePath $ExecutablePath -PassThru
    if (-not $reopen.WaitForExit(5000)) {
        throw 'The reopen signal process did not exit.'
    }
    $reopenDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $primary.Refresh()
        $reopened = $primary.MainWindowHandle -ne 0 -and
            $primary.Responding -and
            [UsageGuardWindowProbe]::IsWindowVisible($primary.MainWindowHandle) -and
            (Test-WindowOnTargetScreen -WindowHandle $primary.MainWindowHandle)
        if (-not $reopened) { Start-Sleep -Milliseconds 25 }
    } while (-not $reopened -and [DateTime]::UtcNow -lt $reopenDeadline)
    $reopenWatch.Stop()
    if (-not $reopened -or $primary.MainWindowHandle -eq 0) {
        throw 'The minimized popup did not reopen visibly and responsively.'
    }
    Assert-WindowOnTargetScreen -WindowHandle $primary.MainWindowHandle

    $closeToTraySupported = [UsageGuardWindowProbe]::PostMessage(
        $primary.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 350
    $afterClose = Get-Process -Id $primary.Id -ErrorAction SilentlyContinue
    $closeKeptProcessAlive = $null -ne $afterClose
    if ($closeKeptProcessAlive) {
        if (Wait-ExactProcessExit -ProcessId $primary.Id -TimeoutMilliseconds 2000) {
            $closeKeptProcessAlive = $false
        }
    }
    if ($closeKeptProcessAlive) {
        $openAfterClose = Start-Process -FilePath $ExecutablePath -PassThru
        if (-not $openAfterClose.WaitForExit(5000)) {
            throw 'The post-close reopen signal did not exit.'
        }
        Start-Sleep -Milliseconds 250
        $primary.Refresh()
    }
    else {
        $afterCloseRestart = Start-VisibleCandidate
        $primary = $afterCloseRestart.Process
    }
    if ($StabilizationSeconds -gt 0) {
        Start-Sleep -Seconds $StabilizationSeconds
    }

    $idle = Measure-ProcessSample -Process $primary
    Request-ExactShutdown
    if (@(Get-ExactProcesses).Count -ne 0) {
        throw 'A Usage Guard process remained after graceful shutdown.'
    }

    $restart = Start-VisibleCandidate
    $finalProcess = $restart.Process
    if ($StabilizationSeconds -gt 0) {
        Start-Sleep -Seconds $StabilizationSeconds
    }
    $restartIdle = Measure-ProcessSample -Process $finalProcess
    $result = [ordered]@{
        schemaVersion = 1
        measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        executablePath = $ExecutablePath
        executableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ExecutablePath).Hash.ToLowerInvariant()
        logicalProcessors = [Environment]::ProcessorCount
        sampleSeconds = $SampleSeconds
        stabilizationSeconds = $StabilizationSeconds
        targetScreenDeviceName = $TargetScreen.DeviceName
        targetScreenBounds = $TargetScreen.Bounds
        coldStartupToVisibleResponsiveMs = $cold.ReadyMilliseconds
        activeSample = $active
        gpuSample = $gpu
        ordinarySecondLaunchExitMs = [Math]::Round($secondLaunchWatch.Elapsed.TotalMilliseconds, 1)
        singleInstanceProcessCount = @(Get-ExactProcesses).Count
        minimizedReopenToVisibleResponsiveMs = [Math]::Round($reopenWatch.Elapsed.TotalMilliseconds, 1)
        closeToTrayKeptProcessAlive = $closeKeptProcessAlive
        idleSample = $idle
        gracefulShutdownLeftProcesses = 0
        restartToVisibleResponsiveMs = $restart.ReadyMilliseconds
        restartSample = $restartIdle
        finalPid = $finalProcess.Id
        finalWindowVisible = [UsageGuardWindowProbe]::IsWindowVisible($finalProcess.MainWindowHandle)
        finalResponding = $finalProcess.Responding
        leftOpen = [bool]$LeaveOpen
    }
    $parent = Split-Path -Parent $OutputPath
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
    [pscustomobject]$result
}
finally {
    if (-not $LeaveOpen) {
        Request-ExactShutdown
    }
}
