Set-StrictMode -Version Latest

function Stop-CodexUsageGuardOwnedProcess {
    param([Diagnostics.Process]$Process)

    if ($Process.HasExited) {
        return
    }

    $TaskKillPath = Join-Path $env:SystemRoot 'System32\taskkill.exe'
    if (Test-Path -LiteralPath $TaskKillPath -PathType Leaf) {
        $StopInfo = [Diagnostics.ProcessStartInfo]::new()
        $StopInfo.FileName = $TaskKillPath
        $StopInfo.Arguments = "/PID $($Process.Id) /T /F"
        $StopInfo.UseShellExecute = $false
        $StopInfo.CreateNoWindow = $true
        $StopInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        $StopInfo.RedirectStandardOutput = $true
        $StopInfo.RedirectStandardError = $true
        $Stopper = [Diagnostics.Process]::new()
        try {
            $Stopper.StartInfo = $StopInfo
            if ($Stopper.Start()) {
                [void]$Stopper.StandardOutput.ReadToEnd()
                [void]$Stopper.StandardError.ReadToEnd()
                [void]$Stopper.WaitForExit(5000)
            }
        }
        finally {
            $Stopper.Dispose()
        }
    }

    if (-not $Process.HasExited) {
        $Process.Kill()
        $Process.WaitForExit()
    }
}

function Invoke-CodexUsageGuardProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$Arguments,

        [ValidateRange(1000, 120000)]
        [int]$TimeoutMilliseconds = 30000
    )

    $ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        throw 'codex-usage-guard executable is unavailable'
    }

    $StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $StartInfo.FileName = $ExecutablePath
    $StartInfo.Arguments = $Arguments
    $StartInfo.UseShellExecute = $false
    $StartInfo.CreateNoWindow = $true
    $StartInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $StartInfo.RedirectStandardOutput = $true
    $StartInfo.RedirectStandardError = $true

    $Process = [Diagnostics.Process]::new()
    $Process.StartInfo = $StartInfo
    $Started = $false
    try {
        if (-not $Process.Start()) {
            throw 'codex-usage-guard executable could not be started'
        }
        $Started = $true

        $StandardOutput = $Process.StandardOutput.ReadToEndAsync()
        $StandardError = $Process.StandardError.ReadToEndAsync()
        if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
            try {
                Stop-CodexUsageGuardOwnedProcess -Process $Process
            }
            catch {
                # The process may have exited between the timeout and cleanup.
            }

            throw 'codex-usage-guard executable timed out'
        }

        # A second wait ensures redirected asynchronous reads are fully drained.
        $Process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $Process.ExitCode
            StandardOutput = $StandardOutput.GetAwaiter().GetResult()
            StandardError = $StandardError.GetAwaiter().GetResult()
        }
    }
    finally {
        if ($Started -and -not $Process.HasExited) {
            try {
                Stop-CodexUsageGuardOwnedProcess -Process $Process
            }
            catch {
                # Best-effort cleanup is limited to the child created above.
            }
        }

        $Process.Dispose()
    }
}

function ConvertFrom-CodexUsageGuardDecisionOutput {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$StandardOutput,

        [Parameter(Mandatory = $true)]
        [int]$ExitCode
    )

    if ([string]::IsNullOrWhiteSpace($StandardOutput)) {
        throw 'codex-usage-guard returned no decision'
    }

    try {
        $Decision = $StandardOutput | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw 'codex-usage-guard returned malformed output'
    }

    if ($null -eq $Decision -or
        $Decision -is [Array] -or
        $Decision -is [string] -or
        @($Decision.PSObject.Properties).Count -eq 0) {
        throw 'codex-usage-guard returned an invalid decision shape'
    }

    $AllowedProperties = @(
        'decision',
        'underlyingDecision',
        'reason',
        'source',
        'remainingPercent',
        'observedAtUtc',
        'resetsAtUtc',
        'confidence',
        'freshness',
        'error',
        'startNewPhaseAllowed',
        'finishCurrentCheckpointOnly',
        'resetDetected',
        'sourceProvenance',
        'isSuccessfulLiveObservation',
        'controllingWindow',
        'windows',
        'resumeRecommendation',
        'resetsAtLocalDisplay'
    )
    $RequiredProperties = @(
        'decision',
        'underlyingDecision',
        'reason',
        'source',
        'observedAtUtc',
        'confidence',
        'freshness',
        'startNewPhaseAllowed',
        'finishCurrentCheckpointOnly',
        'resetDetected',
        'sourceProvenance',
        'isSuccessfulLiveObservation',
        'resumeRecommendation'
    )
    $ActualProperties = @($Decision.PSObject.Properties.Name)
    if (@($ActualProperties | Where-Object { $_ -notin $AllowedProperties }).Count -ne 0 -or
        @($RequiredProperties | Where-Object { $_ -notin $ActualProperties }).Count -ne 0) {
        throw 'codex-usage-guard returned an unsupported decision schema'
    }

    if ($Decision.decision -notin @('normal', 'warning', 'safe_wrap', 'unknown', 'override_active', 'provenance_mismatch') -or
        $Decision.source -notin @('live_app_server', 'genuine_live_latch', 'user_override', 'unavailable') -or
        $Decision.confidence -notin @('none', 'medium', 'high') -or
        $Decision.freshness -notin @('unknown', 'observed_now', 'stale') -or
        $Decision.startNewPhaseAllowed -isnot [bool] -or
        $Decision.finishCurrentCheckpointOnly -isnot [bool] -or
        $Decision.resetDetected -isnot [bool] -or
        $Decision.isSuccessfulLiveObservation -isnot [bool]) {
        throw 'codex-usage-guard returned invalid decision values'
    }

    if (-not (Test-CodexUsageGuardTimestamp $Decision.observedAtUtc)) {
        throw 'codex-usage-guard returned an invalid observation time'
    }

    $Provenance = $Decision.sourceProvenance
    if ($null -eq $Provenance) {
        throw 'codex-usage-guard returned invalid source provenance'
    }
    $ProvenanceProperties = @($Provenance.PSObject.Properties.Name)
    if (
        $ProvenanceProperties.Count -ne 3 -or
        @($ProvenanceProperties | Where-Object {
            $_ -notin @('distribution', 'codexCliVersion', 'executableSha256')
        }).Count -ne 0 -or
        [string]::IsNullOrWhiteSpace([string]$Provenance.distribution) -or
        [string]::IsNullOrWhiteSpace([string]$Provenance.codexCliVersion) -or
        ([string]$Provenance.executableSha256) -notmatch '^[0-9A-Fa-f]{64}$') {
        throw 'codex-usage-guard returned invalid source provenance'
    }

    $HasRemaining = 'remainingPercent' -in $ActualProperties
    $HasReset = 'resetsAtUtc' -in $ActualProperties
    $HasResetLocalDisplay = 'resetsAtLocalDisplay' -in $ActualProperties
    if ($HasRemaining -and
        ($Decision.remainingPercent -isnot [ValueType] -or
         $Decision.remainingPercent -is [bool] -or
         [decimal]$Decision.remainingPercent -lt 0 -or
         [decimal]$Decision.remainingPercent -gt 100)) {
        throw 'codex-usage-guard returned an invalid percentage'
    }
    if ($HasReset) {
        if (-not (Test-CodexUsageGuardTimestamp $Decision.resetsAtUtc) -or
            -not $HasResetLocalDisplay -or
            [string]::IsNullOrWhiteSpace([string]$Decision.resetsAtLocalDisplay)) {
            throw 'codex-usage-guard returned an invalid reset time'
        }
    }

    $HasWindows = 'windows' -in $ActualProperties
    $HasControllingWindow = 'controllingWindow' -in $ActualProperties
    if ($HasWindows) {
        $Windows = @($Decision.windows)
        if ($Windows.Count -notin @(0, 2)) {
            throw 'codex-usage-guard returned invalid quota windows'
        }
        $Kinds = @()
        foreach ($Window in $Windows) {
            $WindowProperties = @($Window.PSObject.Properties.Name)
            if ($WindowProperties.Count -ne 4 -or
                @($WindowProperties | Where-Object {
                    $_ -notin @('kind', 'remainingPercent', 'resetsAtUtc', 'resetsAtLocalDisplay')
                }).Count -ne 0 -or
                $Window.kind -notin @('five_hour', 'weekly') -or
                $Window.remainingPercent -isnot [ValueType] -or
                $Window.remainingPercent -is [bool] -or
                [decimal]$Window.remainingPercent -lt 0 -or
                [decimal]$Window.remainingPercent -gt 100 -or
                -not (Test-CodexUsageGuardTimestamp $Window.resetsAtUtc) -or
                [string]::IsNullOrWhiteSpace([string]$Window.resetsAtLocalDisplay)) {
                throw 'codex-usage-guard returned invalid quota windows'
            }
            $Kinds += [string]$Window.kind
        }
        if ($Windows.Count -eq 2 -and
            (@($Kinds | Select-Object -Unique).Count -ne 2 -or
             'five_hour' -notin $Kinds -or 'weekly' -notin $Kinds)) {
            throw 'codex-usage-guard returned invalid quota windows'
        }
    }
    if ($HasControllingWindow -and
        $Decision.controllingWindow -notin @('five_hour', 'weekly')) {
        throw 'codex-usage-guard returned invalid controlling window'
    }

    $Resume = $Decision.resumeRecommendation
    if ($null -eq $Resume) {
        throw 'codex-usage-guard returned invalid resume metadata'
    }
    $ResumeProperties = @($Resume.PSObject.Properties.Name)
    $RequiredResumeProperties = @(
        'status',
        'reason',
        'providerJitterMarginSeconds',
        'oneShotWakeUpOptIn',
        'constrainingWindows'
    )
    if (@($ResumeProperties | Where-Object {
            $_ -notin @('status', 'reason', 'recommendedAtUtc',
                'recommendedAtLocalDisplay', 'resetIdentity',
                'providerJitterMarginSeconds', 'oneShotWakeUpOptIn',
                'constrainingWindows')
        }).Count -ne 0 -or
        @($RequiredResumeProperties | Where-Object {
            $_ -notin $ResumeProperties
        }).Count -ne 0) {
        throw 'codex-usage-guard returned invalid resume metadata'
    }
    if ($Resume.status -notin @('not_required', 'recommended', 'unavailable') -or
        $Resume.reason -notin @('decision_allows_work', 'user_override_active',
            'five_hour_constraint', 'weekly_constraint',
            'all_constraining_windows', 'reset_data_unavailable',
            'reset_data_stale') -or
        $Resume.providerJitterMarginSeconds -isnot [ValueType] -or
        $Resume.providerJitterMarginSeconds -is [bool] -or
        [int]$Resume.providerJitterMarginSeconds -lt 1 -or
        [int]$Resume.providerJitterMarginSeconds -gt 600 -or
        $Resume.oneShotWakeUpOptIn -isnot [bool]) {
        throw 'codex-usage-guard returned invalid resume metadata'
    }
    $ResumeWindows = @($Resume.constrainingWindows)
    if ($ResumeWindows.Count -gt 2) {
        throw 'codex-usage-guard returned invalid resume windows'
    }
    $ResumeKinds = @()
    foreach ($ResumeWindow in $ResumeWindows) {
        $Properties = @($ResumeWindow.PSObject.Properties.Name)
        if ($Properties.Count -ne 3 -or
            @($Properties | Where-Object {
                $_ -notin @('kind', 'resetsAtUtc', 'resetsAtLocalDisplay')
            }).Count -ne 0 -or
            $ResumeWindow.kind -notin @('five_hour', 'weekly') -or
            -not (Test-CodexUsageGuardTimestamp $ResumeWindow.resetsAtUtc) -or
            [string]::IsNullOrWhiteSpace([string]$ResumeWindow.resetsAtLocalDisplay)) {
            throw 'codex-usage-guard returned invalid resume windows'
        }
        $ResumeKinds += [string]$ResumeWindow.kind
    }
    if (@($ResumeKinds | Select-Object -Unique).Count -ne $ResumeKinds.Count) {
        throw 'codex-usage-guard returned duplicate resume windows'
    }
    $ResumeRecommended = $Resume.status -eq 'recommended'
    if ($ResumeRecommended) {
        if ($Decision.decision -ne 'safe_wrap' -or
            'recommendedAtUtc' -notin $ResumeProperties -or
            'recommendedAtLocalDisplay' -notin $ResumeProperties -or
            'resetIdentity' -notin $ResumeProperties -or
            -not (Test-CodexUsageGuardTimestamp $Resume.recommendedAtUtc) -or
            [string]::IsNullOrWhiteSpace([string]$Resume.recommendedAtLocalDisplay) -or
            ([string]$Resume.resetIdentity) -notmatch '^[0-9a-f]{24}$' -or
            $ResumeWindows.Count -notin @(1, 2) -or
            $Decision.source -notin @('live_app_server', 'genuine_live_latch') -or
            $Decision.confidence -ne 'high' -or
            $Decision.freshness -ne 'observed_now' -or
            -not $Decision.isSuccessfulLiveObservation -or
            -not $HasWindows -or @($Decision.windows).Count -ne 2 -or
            -not $HasControllingWindow) {
            throw 'codex-usage-guard returned untrusted resume metadata'
        }
    }
    elseif ($ResumeWindows.Count -ne 0 -or
        'recommendedAtUtc' -in $ResumeProperties -or
        'recommendedAtLocalDisplay' -in $ResumeProperties -or
        'resetIdentity' -in $ResumeProperties) {
        throw 'codex-usage-guard returned contradictory resume metadata'
    }

    switch ($Decision.decision) {
        { $_ -in @('normal', 'warning') } {
            if ($Decision.source -ne 'live_app_server' -or
                $Decision.confidence -ne 'high' -or
                $Decision.freshness -ne 'observed_now' -or
                -not $HasRemaining -or -not $HasReset -or
                -not $HasWindows -or @($Decision.windows).Count -ne 2 -or
                -not $HasControllingWindow -or
                -not $Decision.isSuccessfulLiveObservation -or
                -not $Decision.startNewPhaseAllowed -or
                $Decision.finishCurrentCheckpointOnly -or
                $ExitCode -ne 0) {
                throw 'codex-usage-guard returned a contradictory live decision'
            }
        }
        'safe_wrap' {
            $FreshLive = $Decision.source -in @(
                    'live_app_server', 'genuine_live_latch') -and
                $Decision.confidence -eq 'high' -and
                $Decision.freshness -eq 'observed_now' -and
                $HasRemaining -and $HasReset -and
                $HasWindows -and @($Decision.windows).Count -eq 2 -and
                $HasControllingWindow -and
                $Decision.isSuccessfulLiveObservation
            $GenuineLatch = $Decision.source -eq 'genuine_live_latch' -and
                -not $ResumeRecommended
            if ((-not $FreshLive -and -not $GenuineLatch) -or
                $Decision.startNewPhaseAllowed -or
                -not $Decision.finishCurrentCheckpointOnly -or
                $ExitCode -ne 3) {
                throw 'codex-usage-guard returned a contradictory SafeWrap decision'
            }
        }
        'override_active' {
            if ($Decision.source -ne 'user_override' -or
                -not $Decision.startNewPhaseAllowed -or
                $Decision.finishCurrentCheckpointOnly -or
                $ExitCode -ne 0) {
                throw 'codex-usage-guard returned a contradictory override decision'
            }
        }
        default {
            if ($Decision.startNewPhaseAllowed -or
                -not $Decision.finishCurrentCheckpointOnly -or
                $ExitCode -ne 2) {
                throw 'codex-usage-guard returned a contradictory fail-closed decision'
            }
        }
    }

    if ($Decision.decision -in @('normal', 'warning', 'override_active') -and
        $Resume.status -ne 'not_required') {
        throw 'codex-usage-guard returned contradictory resume status'
    }
    if ($Decision.decision -in @('unknown', 'provenance_mismatch') -and
        $Resume.status -ne 'unavailable') {
        throw 'codex-usage-guard returned contradictory resume status'
    }

    $Decision | ConvertTo-Json -Compress -Depth 4
}

function Test-CodexUsageGuardTimestamp {
    param([object]$Value)

    if ($Value -is [DateTimeOffset] -or $Value -is [DateTime]) {
        return $true
    }

    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    $Parsed = [DateTimeOffset]::MinValue
    [DateTimeOffset]::TryParse(
        $Value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$Parsed)
}
