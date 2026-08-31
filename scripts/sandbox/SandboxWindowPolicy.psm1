Set-StrictMode -Version Latest

function Select-UsageGuardApprovedDisplay {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Displays,
        [Parameter(Mandatory = $true)][string]$ApprovedStableDeviceId,
        [Parameter(Mandatory = $true)][int]$ExpectedLeft,
        [Parameter(Mandatory = $true)][int]$ExpectedTop,
        [Parameter(Mandatory = $true)][int]$ExpectedWidth,
        [Parameter(Mandatory = $true)][int]$ExpectedHeight
    )

    $Matches = @($Displays | Where-Object {
        $_.Connected -eq $true -and
        $_.StableDeviceId -is [string] -and
        $_.StableDeviceId.Equals($ApprovedStableDeviceId, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($Matches.Count -ne 1) {
        throw 'The approved non-primary display is absent or ambiguous.'
    }
    $Display = $Matches[0]
    if ($Display.Primary -eq $true -or
        [int]$Display.WorkingLeft -ne $ExpectedLeft -or
        [int]$Display.WorkingTop -ne $ExpectedTop -or
        [int]$Display.WorkingWidth -ne $ExpectedWidth -or
        [int]$Display.WorkingHeight -ne $ExpectedHeight -or
        $ExpectedWidth -lt 640 -or $ExpectedHeight -lt 480) {
        throw 'The approved display state changed or is unsafe.'
    }
    return $Display
}

function Select-UsageGuardSandboxClientTarget {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object[]]$Clients,
        [Parameter(Mandatory = $true)][object[]]$Windows,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][int[]]$BaselineClientPids,
        [Parameter(Mandatory = $true)][int]$LauncherPid,
        [Parameter(Mandatory = $true)][DateTimeOffset]$LaunchStartedAtUtc,
        [Parameter(Mandatory = $true)][string]$ExpectedClientPath
    )

    $ExpectedClientPath = [IO.Path]::GetFullPath($ExpectedClientPath)
    $Eligible = @($Clients | Where-Object {
        $_.ProcessId -is [ValueType] -and
        [int]$_.ProcessId -notin $BaselineClientPids -and
        $_.ExecutablePath -is [string] -and
        [IO.Path]::GetFullPath([string]$_.ExecutablePath).Equals(
            $ExpectedClientPath,
            [StringComparison]::OrdinalIgnoreCase) -and
        $_.CreatedAtUtc -is [DateTimeOffset] -and
        [DateTimeOffset]$_.CreatedAtUtc -ge $LaunchStartedAtUtc.AddSeconds(-2) -and
        @($_.AncestorProcessIds) -contains $LauncherPid
    })
    if ($Eligible.Count -ne 1) {
        throw 'Exactly one newly owned Windows Sandbox client was not proven.'
    }

    $Client = $Eligible[0]
    $OwnedWindows = @($Windows | Where-Object {
        [int64]$_.Hwnd -ne 0 -and
        [int]$_.ProcessId -eq [int]$Client.ProcessId -and
        $_.Visible -eq $true
    })
    if ($OwnedWindows.Count -ne 1) {
        throw 'Exactly one visible top-level window for the owned Sandbox client was not proven.'
    }

    [pscustomobject]@{
        ProcessId = [int]$Client.ProcessId
        ExecutablePath = $ExpectedClientPath
        Hwnd = [int64]$OwnedWindows[0].Hwnd
        LauncherPid = $LauncherPid
        CreatedAtUtc = [DateTimeOffset]$Client.CreatedAtUtc
    }
}

function Assert-UsageGuardSandboxStateUnchanged {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$OriginalTarget,
        [Parameter(Mandatory = $true)]$CurrentTarget,
        [Parameter(Mandatory = $true)]$OriginalDisplay,
        [Parameter(Mandatory = $true)]$CurrentDisplay
    )

    if ([int]$OriginalTarget.ProcessId -ne [int]$CurrentTarget.ProcessId -or
        [int64]$OriginalTarget.Hwnd -ne [int64]$CurrentTarget.Hwnd -or
        -not ([string]$OriginalTarget.ExecutablePath).Equals(
            [string]$CurrentTarget.ExecutablePath,
            [StringComparison]::OrdinalIgnoreCase) -or
        -not ([string]$OriginalDisplay.StableDeviceId).Equals(
            [string]$CurrentDisplay.StableDeviceId,
            [StringComparison]::OrdinalIgnoreCase) -or
        [int]$OriginalDisplay.WorkingLeft -ne [int]$CurrentDisplay.WorkingLeft -or
        [int]$OriginalDisplay.WorkingTop -ne [int]$CurrentDisplay.WorkingTop -or
        [int]$OriginalDisplay.WorkingWidth -ne [int]$CurrentDisplay.WorkingWidth -or
        [int]$OriginalDisplay.WorkingHeight -ne [int]$CurrentDisplay.WorkingHeight) {
        throw 'The Sandbox target or approved display changed during QA.'
    }
}

Export-ModuleMember -Function @(
    'Select-UsageGuardApprovedDisplay',
    'Select-UsageGuardSandboxClientTarget',
    'Assert-UsageGuardSandboxStateUnchanged'
)
