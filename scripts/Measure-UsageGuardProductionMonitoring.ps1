[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ExecutablePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [ValidateRange(65, 180)][int]$ObservationSeconds = 68,
    [switch]$LeaveOpen
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$LocalData = [Environment]::GetFolderPath('LocalApplicationData')
$ExpectedCliPath = [IO.Path]::GetFullPath((Join-Path $LocalData 'Programs\OpenAI\Codex\bin\codex.exe'))
$DataRoot = Join-Path $LocalData 'OpenAI\CodexUsageGuard'
$SettingsPath = Join-Path $DataRoot 'settings.json'
$ProvidersPath = Join-Path $DataRoot 'providers.json'

if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf) -or
    [IO.Path]::GetFileName($ExecutablePath) -cne 'CodexUsageGuard.exe' -or
    (Test-Path -LiteralPath $OutputPath)) {
    throw 'Use an existing CodexUsageGuard.exe and a new output path.'
}

function Get-ExactHelperProcesses {
    @(Get-CimInstance Win32_Process -Filter "Name = 'CodexUsageGuard.exe'" |
        Where-Object { $_.ExecutablePath -eq $ExecutablePath })
}

function Request-ExactShutdown {
    $owned = @(Get-ExactHelperProcesses)
    if ($owned.Count -eq 0) { return }
    $request = Start-Process -FilePath $ExecutablePath `
        -ArgumentList '--shutdown' -PassThru -WindowStyle Hidden
    if (-not $request.WaitForExit(17000) -or $request.ExitCode -ne 0) {
        throw 'The exact helper did not accept bounded graceful shutdown.'
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        if (@(Get-ExactHelperProcesses).Count -eq 0) { return }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'An exact helper process remained after graceful shutdown.'
}

if (@(Get-ExactHelperProcesses).Count -ne 0) {
    throw 'An exact Usage Guard instance is already running.'
}
if (-not (Test-Path -LiteralPath $ExpectedCliPath -PathType Leaf)) {
    throw 'The pinned official Codex CLI path is unavailable.'
}

$settingsHashBefore = (Get-FileHash -LiteralPath $SettingsPath `
    -Algorithm SHA256).Hash
$providersHashBefore = (Get-FileHash -LiteralPath $ProvidersPath `
    -Algorithm SHA256).Hash
$settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
$pollingSeconds = [int]$settings.pollingIntervalSeconds
if ($pollingSeconds -lt 30 -or $pollingSeconds -gt 300) {
    throw 'The validated polling interval is unavailable.'
}

$process = $null
$result = $null
try {
    $startup = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $ExecutablePath -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        Start-Sleep -Milliseconds 25
        $process.Refresh()
    } while (($process.MainWindowHandle -eq [IntPtr]::Zero -or
            -not $process.Responding) -and
        [DateTime]::UtcNow -lt $deadline)
    $startup.Stop()
    if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero -or
        -not $process.Responding) {
        throw 'The production popup did not become responsive.'
    }

    $observedChildren = [Collections.Generic.HashSet[int]]::new()
    $cpuStart = $process.TotalProcessorTime
    $measurement = [Diagnostics.Stopwatch]::StartNew()
    $peakWorkingSet = 0L
    $peakPrivate = 0L
    $peakThreads = 0
    $peakHandles = 0
    $allResponsive = $true
    while ($measurement.Elapsed.TotalSeconds -lt $ObservationSeconds) {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
        if ($process.HasExited) {
            throw 'The production monitor exited during measurement.'
        }
        $peakWorkingSet = [Math]::Max($peakWorkingSet, $process.WorkingSet64)
        $peakPrivate = [Math]::Max($peakPrivate, $process.PrivateMemorySize64)
        $peakThreads = [Math]::Max($peakThreads, @($process.Threads).Count)
        $peakHandles = [Math]::Max($peakHandles, $process.HandleCount)
        $allResponsive = $allResponsive -and $process.Responding

        foreach ($child in @(Get-CimInstance Win32_Process -Filter (
                "ParentProcessId = $($process.Id) AND Name = 'codex.exe'"))) {
            if ($child.ExecutablePath -eq $ExpectedCliPath -and
                $child.CommandLine -match '(?i)\bapp-server\b' -and
                $child.CommandLine -match '(?i)stdio://') {
                $observedChildren.Add([int]$child.ProcessId) | Out-Null
            }
        }
    }
    $measurement.Stop()
    $process.Refresh()
    $cpuSeconds = ($process.TotalProcessorTime - $cpuStart).TotalSeconds
    $cpuPercent = 100 * $cpuSeconds /
        ($measurement.Elapsed.TotalSeconds * [Environment]::ProcessorCount)
    $result = [ordered]@{
        schemaVersion = 1
        measuredAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        executablePath = $ExecutablePath
        executableSha256 = (Get-FileHash -LiteralPath $ExecutablePath `
            -Algorithm SHA256).Hash.ToLowerInvariant()
        startupToVisibleResponsiveMs = [Math]::Round(
            $startup.Elapsed.TotalMilliseconds, 1)
        observationSeconds = [Math]::Round($measurement.Elapsed.TotalSeconds, 1)
        configuredPollingIntervalSeconds = $pollingSeconds
        exactOwnedAppServerProcessesObserved = $observedChildren.Count
        appServerReadsPerMinuteObserved = [Math]::Round(
            60 * $observedChildren.Count / $measurement.Elapsed.TotalSeconds, 3)
        cpuPercent = [Math]::Round($cpuPercent, 3)
        peakWorkingSetMiB = [Math]::Round($peakWorkingSet / 1MB, 2)
        peakPrivateMemoryMiB = [Math]::Round($peakPrivate / 1MB, 2)
        peakThreads = $peakThreads
        peakHandles = $peakHandles
        responsiveThroughout = $allResponsive
        gpuMeasured = $false
        networkRequestRateMeasured = $false
        modelTaskCreationMeasured = $false
        settingsHashPreserved = $false
        providersHashPreserved = $false
        gracefulShutdownLeftProcesses = $null
        leftOpen = [bool]$LeaveOpen
    }
}
finally {
    if (-not $LeaveOpen) {
        Request-ExactShutdown
    }
    if ($null -ne $result) {
        $result.settingsHashPreserved = $settingsHashBefore -eq
            (Get-FileHash -LiteralPath $SettingsPath -Algorithm SHA256).Hash
        $result.providersHashPreserved = $providersHashBefore -eq
            (Get-FileHash -LiteralPath $ProvidersPath -Algorithm SHA256).Hash
        $result.gracefulShutdownLeftProcesses =
            @(Get-ExactHelperProcesses).Count
        $parent = Split-Path -Parent $OutputPath
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        $result | ConvertTo-Json -Depth 5 |
            Set-Content -LiteralPath $OutputPath -Encoding utf8NoBOM
    }
}

if ($null -eq $result) { throw 'Production measurement did not complete.' }
[pscustomobject]$result
