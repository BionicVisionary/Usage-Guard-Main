[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PolicyModule
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module $PolicyModule -Force

function Assert-Throws {
    param([scriptblock]$Action)
    try { & $Action; throw 'Expected a fail-closed exception.' }
    catch {
        if ($_.Exception.Message -ceq 'Expected a fail-closed exception.') { throw }
    }
}

$Display = [pscustomobject]@{
    DeviceName = '\\.\DISPLAY1'
    StableDeviceId = 'MONITOR\APPROVED\UNIT'
    Connected = $true
    Primary = $false
    WorkingLeft = -1920
    WorkingTop = 0
    WorkingWidth = 1920
    WorkingHeight = 1040
}
$Primary = [pscustomobject]@{
    DeviceName = '\\.\DISPLAY2'
    StableDeviceId = 'MONITOR\PRIMARY\UNIT'
    Connected = $true
    Primary = $true
    WorkingLeft = 0
    WorkingTop = 0
    WorkingWidth = 1920
    WorkingHeight = 1040
}
$SelectedDisplay = Select-UsageGuardApprovedDisplay -Displays @($Primary, $Display) `
    -ApprovedStableDeviceId $Display.StableDeviceId -ExpectedLeft -1920 `
    -ExpectedTop 0 -ExpectedWidth 1920 -ExpectedHeight 1040
if ($SelectedDisplay.StableDeviceId -cne $Display.StableDeviceId) {
    throw 'The approved display was not selected exactly.'
}
Assert-Throws {
    Select-UsageGuardApprovedDisplay -Displays @($Primary) `
        -ApprovedStableDeviceId $Display.StableDeviceId -ExpectedLeft -1920 `
        -ExpectedTop 0 -ExpectedWidth 1920 -ExpectedHeight 1040
}
Assert-Throws {
    Select-UsageGuardApprovedDisplay -Displays @($Display) `
        -ApprovedStableDeviceId $Display.StableDeviceId -ExpectedLeft -1600 `
        -ExpectedTop 0 -ExpectedWidth 1920 -ExpectedHeight 1040
}
Assert-Throws {
    Select-UsageGuardApprovedDisplay -Displays @($Primary) `
        -ApprovedStableDeviceId $Primary.StableDeviceId -ExpectedLeft 0 `
        -ExpectedTop 0 -ExpectedWidth 1920 -ExpectedHeight 1040
}

$Started = [DateTimeOffset]::Parse('2026-08-26T01:00:00Z')
$ExpectedPath = 'C:\Windows\System32\WindowsSandboxClient.exe'
$Clients = @(
    [pscustomobject]@{
        ProcessId = 701
        ExecutablePath = 'C:\Windows\System32\not-sandbox.exe'
        CreatedAtUtc = $Started.AddSeconds(1)
        AncestorProcessIds = @(500)
    },
    [pscustomobject]@{
        ProcessId = 702
        ExecutablePath = $ExpectedPath
        CreatedAtUtc = $Started.AddSeconds(1)
        AncestorProcessIds = @(500)
    }
)
$Windows = @(
    [pscustomobject]@{ ProcessId = 701; Hwnd = 1001L; Visible = $true },
    [pscustomobject]@{ ProcessId = 702; Hwnd = 1002L; Visible = $true },
    [pscustomobject]@{ ProcessId = 999; Hwnd = 1003L; Visible = $true }
)
$Target = Select-UsageGuardSandboxClientTarget -Clients $Clients -Windows $Windows `
    -BaselineClientPids @() -LauncherPid 500 -LaunchStartedAtUtc $Started `
    -ExpectedClientPath $ExpectedPath
if ($Target.ProcessId -ne 702 -or $Target.Hwnd -ne 1002L) {
    throw 'A non-Sandbox process or window was selected.'
}
Assert-Throws {
    Select-UsageGuardSandboxClientTarget -Clients @($Clients[0]) -Windows $Windows `
        -BaselineClientPids @() -LauncherPid 500 -LaunchStartedAtUtc $Started `
        -ExpectedClientPath $ExpectedPath
}
Assert-Throws {
    Select-UsageGuardSandboxClientTarget -Clients @($Clients[1]) -Windows $Windows `
        -BaselineClientPids @(702) -LauncherPid 500 -LaunchStartedAtUtc $Started `
        -ExpectedClientPath $ExpectedPath
}
Assert-Throws {
    $ChangedDisplay = $Display.PSObject.Copy()
    $ChangedDisplay.WorkingWidth = 1600
    Assert-UsageGuardSandboxStateUnchanged -OriginalTarget $Target `
        -CurrentTarget $Target -OriginalDisplay $Display `
        -CurrentDisplay $ChangedDisplay
}
Assert-Throws {
    $ChangedTarget = $Target.PSObject.Copy()
    $ChangedTarget.Hwnd = 2002L
    Assert-UsageGuardSandboxStateUnchanged -OriginalTarget $Target `
        -CurrentTarget $ChangedTarget -OriginalDisplay $Display `
        -CurrentDisplay $Display
}

[Console]::Out.WriteLine('PASS Sandbox window policy tests')
