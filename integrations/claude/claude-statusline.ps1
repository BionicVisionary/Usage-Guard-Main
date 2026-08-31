$ErrorActionPreference = 'Stop'
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

function ConvertTo-UsageGuardClaudeFilteredJson {
    param([Parameter(Mandatory = $true)][string]$RawInput)

    # Windows PowerShell 5.1's ConvertFrom-Json silently keeps the last value
    # when an object repeats a property. Validate the bounded JSON structure
    # first so required fields can never be made ambiguous that way. Unique,
    # additional provider windows remain forward-compatible and are discarded.
    $script:UsageGuardJsonText = $RawInput
    $script:UsageGuardJsonIndex = 0
    $script:UsageGuardJsonDepth = 0

    function Skip-UsageGuardJsonWhitespace {
        while ($script:UsageGuardJsonIndex -lt $script:UsageGuardJsonText.Length -and
            [char]::IsWhiteSpace($script:UsageGuardJsonText[$script:UsageGuardJsonIndex])) {
            $script:UsageGuardJsonIndex++
        }
    }

    function Read-UsageGuardJsonString {
        if ($script:UsageGuardJsonIndex -ge $script:UsageGuardJsonText.Length -or
            $script:UsageGuardJsonText[$script:UsageGuardJsonIndex] -ne '"') {
            throw 'json string expected'
        }
        $script:UsageGuardJsonIndex++
        $Value = [Text.StringBuilder]::new()
        while ($script:UsageGuardJsonIndex -lt $script:UsageGuardJsonText.Length) {
            $Character = $script:UsageGuardJsonText[$script:UsageGuardJsonIndex++]
            if ($Character -eq '"') {
                return $Value.ToString()
            }
            if ([int]$Character -lt 0x20) { throw 'json string invalid' }
            if ($Character -ne '\') {
                [void]$Value.Append($Character)
                continue
            }
            if ($script:UsageGuardJsonIndex -ge $script:UsageGuardJsonText.Length) {
                throw 'json escape invalid'
            }
            $Escape = $script:UsageGuardJsonText[$script:UsageGuardJsonIndex++]
            switch ($Escape) {
                '"' { [void]$Value.Append('"') }
                '\' { [void]$Value.Append('\') }
                '/' { [void]$Value.Append('/') }
                'b' { [void]$Value.Append([char]8) }
                'f' { [void]$Value.Append([char]12) }
                'n' { [void]$Value.Append([char]10) }
                'r' { [void]$Value.Append([char]13) }
                't' { [void]$Value.Append([char]9) }
                'u' {
                    if ($script:UsageGuardJsonIndex + 4 -gt $script:UsageGuardJsonText.Length) {
                        throw 'json unicode escape invalid'
                    }
                    $Hex = $script:UsageGuardJsonText.Substring(
                        $script:UsageGuardJsonIndex, 4)
                    if ($Hex -notmatch '^[0-9a-fA-F]{4}$') {
                        throw 'json unicode escape invalid'
                    }
                    [void]$Value.Append([char][Convert]::ToInt32($Hex, 16))
                    $script:UsageGuardJsonIndex += 4
                }
                default { throw 'json escape invalid' }
            }
        }
        throw 'json string unterminated'
    }

    function Read-UsageGuardJsonValue {
        Skip-UsageGuardJsonWhitespace
        if ($script:UsageGuardJsonIndex -ge $script:UsageGuardJsonText.Length) {
            throw 'json value expected'
        }
        $Character = $script:UsageGuardJsonText[$script:UsageGuardJsonIndex]
        switch ($Character) {
            '{' { Read-UsageGuardJsonObject; return }
            '[' { Read-UsageGuardJsonArray; return }
            '"' { [void](Read-UsageGuardJsonString); return }
            't' { $Literal = 'true' }
            'f' { $Literal = 'false' }
            'n' { $Literal = 'null' }
            default {
                $Remaining = $script:UsageGuardJsonText.Substring(
                    $script:UsageGuardJsonIndex)
                $Number = [Regex]::Match(
                    $Remaining,
                    '^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?')
                if (-not $Number.Success) { throw 'json value invalid' }
                $script:UsageGuardJsonIndex += $Number.Length
                return
            }
        }
        if ($script:UsageGuardJsonText.Substring(
                $script:UsageGuardJsonIndex).StartsWith(
                $Literal, [StringComparison]::Ordinal)) {
            $script:UsageGuardJsonIndex += $Literal.Length
            return
        }
        throw 'json literal invalid'
    }

    function Read-UsageGuardJsonObject {
        $script:UsageGuardJsonDepth++
        if ($script:UsageGuardJsonDepth -gt 24) {
            $script:UsageGuardJsonDepth--
            throw 'json depth invalid'
        }
        try {
            $script:UsageGuardJsonIndex++
            $Names = [Collections.Generic.HashSet[string]]::new(
                [StringComparer]::Ordinal)
            Skip-UsageGuardJsonWhitespace
            if ($script:UsageGuardJsonIndex -lt $script:UsageGuardJsonText.Length -and
                $script:UsageGuardJsonText[$script:UsageGuardJsonIndex] -eq '}') {
                $script:UsageGuardJsonIndex++
                return
            }
            while ($true) {
                Skip-UsageGuardJsonWhitespace
                $Name = Read-UsageGuardJsonString
                if (-not $Names.Add($Name)) { throw 'duplicate json property' }
                Skip-UsageGuardJsonWhitespace
                if ($script:UsageGuardJsonIndex -ge $script:UsageGuardJsonText.Length -or
                    $script:UsageGuardJsonText[$script:UsageGuardJsonIndex] -ne ':') {
                    throw 'json property separator invalid'
                }
                $script:UsageGuardJsonIndex++
                Read-UsageGuardJsonValue
                Skip-UsageGuardJsonWhitespace
                if ($script:UsageGuardJsonIndex -ge $script:UsageGuardJsonText.Length) {
                    throw 'json object unterminated'
                }
                $Delimiter = $script:UsageGuardJsonText[$script:UsageGuardJsonIndex++]
                if ($Delimiter -eq '}') { return }
                if ($Delimiter -ne ',') { throw 'json object delimiter invalid' }
            }
        }
        finally {
            $script:UsageGuardJsonDepth--
        }
    }

    function Read-UsageGuardJsonArray {
        $script:UsageGuardJsonDepth++
        if ($script:UsageGuardJsonDepth -gt 24) {
            $script:UsageGuardJsonDepth--
            throw 'json depth invalid'
        }
        try {
            $script:UsageGuardJsonIndex++
            Skip-UsageGuardJsonWhitespace
            if ($script:UsageGuardJsonIndex -lt $script:UsageGuardJsonText.Length -and
                $script:UsageGuardJsonText[$script:UsageGuardJsonIndex] -eq ']') {
                $script:UsageGuardJsonIndex++
                return
            }
            while ($true) {
                Read-UsageGuardJsonValue
                Skip-UsageGuardJsonWhitespace
                if ($script:UsageGuardJsonIndex -ge $script:UsageGuardJsonText.Length) {
                    throw 'json array unterminated'
                }
                $Delimiter = $script:UsageGuardJsonText[$script:UsageGuardJsonIndex++]
                if ($Delimiter -eq ']') { return }
                if ($Delimiter -ne ',') { throw 'json array delimiter invalid' }
            }
        }
        finally {
            $script:UsageGuardJsonDepth--
        }
    }

    function Get-UsageGuardRequiredProperty {
        param([object]$InputObject, [string]$Name)
        if ($null -eq $InputObject) { throw 'required field missing' }
        $Properties = @($InputObject.PSObject.Properties | Where-Object {
            $_.Name -ceq $Name
        })
        if ($Properties.Count -ne 1) { throw 'required field missing' }
        return $Properties[0].Value
    }

    Read-UsageGuardJsonValue
    Skip-UsageGuardJsonWhitespace
    if ($script:UsageGuardJsonIndex -ne $script:UsageGuardJsonText.Length) {
        throw 'json trailing data invalid'
    }

    $ParsedInput = $RawInput | ConvertFrom-Json
    $RateLimits = Get-UsageGuardRequiredProperty $ParsedInput 'rate_limits'
    $FiveHour = Get-UsageGuardRequiredProperty $RateLimits 'five_hour'
    $SevenDay = Get-UsageGuardRequiredProperty $RateLimits 'seven_day'
    $Filtered = [ordered]@{
        rate_limits = [ordered]@{
            five_hour = [ordered]@{
                used_percentage = Get-UsageGuardRequiredProperty $FiveHour 'used_percentage'
                resets_at = Get-UsageGuardRequiredProperty $FiveHour 'resets_at'
            }
            seven_day = [ordered]@{
                used_percentage = Get-UsageGuardRequiredProperty $SevenDay 'used_percentage'
                resets_at = Get-UsageGuardRequiredProperty $SevenDay 'resets_at'
            }
        }
    } | ConvertTo-Json -Depth 4 -Compress
    $ParsedInput = $null
    $RateLimits = $null
    $FiveHour = $null
    $SevenDay = $null
    $script:UsageGuardJsonText = $null
    $script:UsageGuardJsonDepth = 0
    return $Filtered
}

# Dot-sourcing is a test-only way to exercise the pure in-memory filter. It
# exposes no simulation switch and cannot invoke or persist through the helper.
if ($MyInvocation.InvocationName -eq '.') { return }

try {
    # Keep a sanitized unavailable sentinel ready. If Claude Code invokes the
    # status line without both supported quota windows (or with malformed
    # input), the owned helper records only that bounded failure state. This
    # distinguishes "callback reached the helper" from "callback never ran"
    # without persisting raw status-line JSON or unrelated session fields.
    $FilteredInput = '{}'
    $RawInput = $null
    $Builder = $null
    try {
        $Builder = [Text.StringBuilder]::new()
        $Buffer = [char[]]::new(2048)
        $InputBytes = 0
        while (($Read = [Console]::In.Read($Buffer, 0, $Buffer.Length)) -gt 0) {
            $Chunk = [string]::new($Buffer, 0, $Read)
            $InputBytes += [Text.Encoding]::UTF8.GetByteCount($Chunk)
            if ($InputBytes -gt 65536) { throw 'input too large' }
            [void]$Builder.Append($Chunk)
        }
        $RawInput = $Builder.ToString()
        $Builder.Clear() | Out-Null
        $FilteredInput = ConvertTo-UsageGuardClaudeFilteredJson $RawInput
        $RawInput = $null
    }
    catch {
        $RawInput = $null
        if ($null -ne $Builder) {
            $Builder.Clear() | Out-Null
        }
        $FilteredInput = '{}'
    }
    $LocatorPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OpenAI\CodexUsageGuard\installation.json'
    $Locator = Get-Content -Raw -LiteralPath $LocatorPath | ConvertFrom-Json
    if ($Locator.schemaVersion -ne 1 -or
        $Locator.executablePath -isnot [string] -or
        $Locator.executableSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        -not [IO.Path]::IsPathRooted($Locator.executablePath) -or
        [IO.Path]::GetFileName($Locator.executablePath) -cne 'CodexUsageGuard.exe') {
        throw 'locator invalid'
    }
    $Executable = [IO.Path]::GetFullPath($Locator.executablePath)
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $Executable).Hash -cne
        $Locator.executableSha256.ToUpperInvariant()) { throw 'hash mismatch' }
    $Info = [Diagnostics.ProcessStartInfo]::new()
    $Info.FileName = $Executable
    $Info.Arguments = '--claude-statusline-ingest'
    $Info.UseShellExecute = $false
    $Info.CreateNoWindow = $true
    $Info.RedirectStandardInput = $true
    $Info.RedirectStandardOutput = $true
    $Info.RedirectStandardError = $true
    $Process = [Diagnostics.Process]::new()
    $Started = $false
    try {
        $Process.StartInfo = $Info
        if (-not $Process.Start()) { throw 'start failed' }
        $Started = $true
        $Output = $Process.StandardOutput.ReadToEndAsync()
        $Errors = $Process.StandardError.ReadToEndAsync()
        $Process.StandardInput.Write($FilteredInput)
        $Process.StandardInput.Close()
        $FilteredInput = $null
        # The helper is a self-contained single-file build, so its first run
        # after an install extracts and JITs before it can answer. That cold
        # start easily exceeds a two-second budget even though warm runs finish
        # in well under a second, and killing it mid-write is what previously
        # stranded Claude usage at Unknown. Stay bounded, but allow the cold
        # start to finish.
        if (-not $Process.WaitForExit(10000)) {
            Stop-CodexUsageGuardOwnedProcess -Process $Process
            throw 'timeout'
        }
        $Line = $Output.GetAwaiter().GetResult().Trim()
        [void]$Errors.GetAwaiter().GetResult()
        if ($Line -notmatch '^Usage Guard: Claude (usage unavailable|5h [0-9]+(?:\.[0-9])?% \| weekly [0-9]+(?:\.[0-9])?% remaining)$') {
            throw 'output invalid'
        }
        [Console]::Out.WriteLine($Line)
    }
    finally {
        if ($Started -and -not $Process.HasExited) {
            try { Stop-CodexUsageGuardOwnedProcess -Process $Process } catch { }
        }
        $Process.Dispose()
    }
}
catch {
    [Console]::Out.WriteLine('Usage Guard: Claude usage unavailable')
}
exit 0
