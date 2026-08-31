Set-StrictMode -Version Latest

function Stop-CodexUsageGuardOwnedProcess {
    param([Diagnostics.Process]$Process)

    if ($Process.HasExited) { return }
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
        finally { $Stopper.Dispose() }
    }
    if (-not $Process.HasExited) {
        $Process.Kill()
        $Process.WaitForExit()
    }
}

function Invoke-CodexUsageGuardProcess {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$Arguments,
        [ValidateRange(1000, 30000)][int]$TimeoutMilliseconds = 5000)
    $StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $StartInfo.FileName = [IO.Path]::GetFullPath($ExecutablePath)
    $StartInfo.Arguments = $Arguments
    $StartInfo.UseShellExecute = $false
    $StartInfo.CreateNoWindow = $true
    $StartInfo.RedirectStandardOutput = $true
    $StartInfo.RedirectStandardError = $true
    $Process = [Diagnostics.Process]::new()
    $Started = $false
    try {
        $Process.StartInfo = $StartInfo
        if (-not $Process.Start()) { throw 'start failed' }
        $Started = $true
        $Out = $Process.StandardOutput.ReadToEndAsync()
        $Err = $Process.StandardError.ReadToEndAsync()
        if (-not $Process.WaitForExit($TimeoutMilliseconds)) {
            Stop-CodexUsageGuardOwnedProcess -Process $Process
            throw 'timeout'
        }
        $Process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $Process.ExitCode
            StandardOutput = $Out.GetAwaiter().GetResult()
            StandardError = $Err.GetAwaiter().GetResult()
        }
    }
    finally {
        if ($Started -and -not $Process.HasExited) {
            try {
                Stop-CodexUsageGuardOwnedProcess -Process $Process
            }
            catch {
                # Best effort is limited to the child created above.
            }
        }
        $Process.Dispose()
    }
}

function ConvertFrom-ClaudeUsageGuardDecisionOutput {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$StandardOutput,
        [Parameter(Mandatory = $true)][int]$ExitCode)

    if ([Text.Encoding]::UTF8.GetByteCount($StandardOutput) -gt 32768) {
        throw 'decision too large'
    }
    $Decision = $StandardOutput | ConvertFrom-Json -ErrorAction Stop
    if ($null -eq $Decision -or $Decision -is [Array] -or
        $Decision -is [string]) { throw 'decision shape invalid' }
    $Names = @($Decision.PSObject.Properties.Name | Sort-Object)
    $LiveDecision = $Decision.PSObject.Properties['decision'] -and
        $Decision.decision -in @('normal','warning','safe_wrap')
    $Expected = @('confidence','criticalBufferReached','decision','finishCurrentCheckpointOnly','freshness','provider','source','startNewPhaseAllowed','windows')
    if ($LiveDecision) {
        $Expected = @($Expected + 'controllingWindow' | Sort-Object)
    }
    if (($Names -join ',') -ne ($Expected -join ',') -or
        $Decision.provider -ne 'claude_code' -or
        $Decision.decision -notin @('normal','warning','safe_wrap','unknown','override_active') -or
        $Decision.source -notin @('claude_statusline','user_override') -or
        $Decision.confidence -notin @('none','high') -or
        $Decision.freshness -notin @('unknown','observed_now','configured') -or
        $Decision.criticalBufferReached -isnot [bool] -or
        $Decision.startNewPhaseAllowed -isnot [bool] -or
        $Decision.finishCurrentCheckpointOnly -isnot [bool]) {
        throw 'decision invalid'
    }

    if ($Decision.decision -in @('normal','warning','safe_wrap')) {
        if ($Decision.source -ne 'claude_statusline' -or
            $Decision.confidence -ne 'high' -or
            $Decision.freshness -ne 'observed_now' -or
            @($Decision.windows).Count -ne 2) { throw 'live decision invalid' }
        $WindowKinds = @()
        foreach ($Window in @($Decision.windows)) {
            $WindowNames = @($Window.PSObject.Properties.Name | Sort-Object)
            if (($WindowNames -join ',') -ne 'kind,observedAtUtc,remainingPercent,resetsAtUtc' -or
                $Window.kind -notin @('five_hour','weekly') -or
                $Window.remainingPercent -isnot [ValueType] -or
                $Window.remainingPercent -is [bool] -or
                [decimal]$Window.remainingPercent -lt 0 -or
                [decimal]$Window.remainingPercent -gt 100) {
                throw 'window invalid'
            }
            $ObservedAt = [DateTimeOffset]::MinValue
            $ResetsAt = [DateTimeOffset]::MinValue
            if (-not [DateTimeOffset]::TryParse(
                    [string]$Window.observedAtUtc,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::RoundtripKind,
                    [ref]$ObservedAt) -or
                -not [DateTimeOffset]::TryParse(
                    [string]$Window.resetsAtUtc,
                    [Globalization.CultureInfo]::InvariantCulture,
                    [Globalization.DateTimeStyles]::RoundtripKind,
                    [ref]$ResetsAt) -or
                $ResetsAt -le $ObservedAt) {
                throw 'window timestamp invalid'
            }
            $WindowKinds += $Window.kind
        }
        if (@($WindowKinds | Sort-Object -Unique).Count -ne 2 -or
            $Decision.controllingWindow -notin $WindowKinds) {
            throw 'window set invalid'
        }
    }
    elseif (@($Decision.windows).Count -ne 0 -or
        $null -ne $Decision.PSObject.Properties['controllingWindow']) {
        throw 'non-live decision exposed windows'
    }

    if ($Decision.decision -eq 'unknown' -and
        ($Decision.source -ne 'claude_statusline' -or
         $Decision.confidence -ne 'none' -or
         $Decision.freshness -ne 'unknown')) {
        throw 'unknown provenance invalid'
    }
    if ($Decision.decision -eq 'override_active' -and
        ($Decision.source -ne 'user_override' -or
         $Decision.confidence -ne 'high' -or
         $Decision.freshness -ne 'configured')) {
        throw 'override provenance invalid'
    }
    if ($Decision.decision -in @('unknown','safe_wrap') -and
        ($Decision.startNewPhaseAllowed -or -not $Decision.finishCurrentCheckpointOnly)) {
        throw 'fail-closed decision invalid'
    }
    if ($Decision.criticalBufferReached -and
        $Decision.decision -ne 'safe_wrap') { throw 'critical decision invalid' }
    if ($Decision.decision -in @('normal','warning','override_active') -and
        (-not $Decision.startNewPhaseAllowed -or $Decision.finishCurrentCheckpointOnly)) {
        throw 'allow decision invalid'
    }
    $ExpectedExit = if ($Decision.decision -eq 'safe_wrap') { 3 } elseif ($Decision.decision -eq 'unknown') { 2 } else { 0 }
    if ($ExitCode -ne $ExpectedExit) { throw 'exit code invalid' }
    return $Decision
}
