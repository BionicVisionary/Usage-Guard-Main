[CmdletBinding(DefaultParameterSetName = 'Run')]
param(
    [Parameter(ParameterSetName = 'List')][switch]$ListDisplays,
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][string]$ApprovedStableDeviceId,
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][int]$ExpectedLeft,
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][int]$ExpectedTop,
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][int]$ExpectedWidth,
    [Parameter(Mandatory = $true, ParameterSetName = 'Run')][int]$ExpectedHeight
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$ArtifactRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts'))
$PolicyModule = Join-Path $PSScriptRoot 'SandboxWindowPolicy.psm1'
$NativeModule = Join-Path $PSScriptRoot 'SandboxHostNative.psm1'
Import-Module $PolicyModule -Force
Import-Module $NativeModule -Force

if ($ListDisplays) {
    @(Get-UsageGuardDisplayInventory | Select-Object DeviceName, StableDeviceId,
        Connected, Primary, WorkingLeft, WorkingTop, WorkingWidth, WorkingHeight) |
        ConvertTo-Json -Depth 3
    exit 0
}

$SandboxExecutable = Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'
$ClientExecutable = Join-Path $env:SystemRoot 'System32\WindowsSandboxClient.exe'
$Template = Join-Path $RepositoryRoot 'sandbox\UsageGuard-QA.wsb.template'
$GuestDriver = Join-Path $PSScriptRoot 'Run-GuestQa.ps1'
foreach ($Required in @($SandboxExecutable, $ClientExecutable, $Template, $GuestDriver)) {
    if (-not (Test-Path -LiteralPath $Required -PathType Leaf)) {
        throw 'Windows Sandbox or a required QA harness file is unavailable.'
    }
}
$Signature = Get-AuthenticodeSignature -LiteralPath $ClientExecutable
if ($Signature.Status -ne 'Valid' -or
    $Signature.SignerCertificate.Subject -notmatch 'Microsoft Windows') {
    throw 'The Windows Sandbox client provenance is not trusted.'
}

$ApprovedDisplay = Select-UsageGuardApprovedDisplay `
    -Displays @(Get-UsageGuardDisplayInventory) `
    -ApprovedStableDeviceId $ApprovedStableDeviceId `
    -ExpectedLeft $ExpectedLeft -ExpectedTop $ExpectedTop `
    -ExpectedWidth $ExpectedWidth -ExpectedHeight $ExpectedHeight

$RunId = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' +
    [guid]::NewGuid().ToString('N').Substring(0, 8)
$StageRoot = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot "sandbox-input-$RunId"))
$EvidenceRoot = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot "sandbox-evidence-$RunId"))
$Prefix = $ArtifactRoot.TrimEnd('\') + '\'
foreach ($Path in @($StageRoot, $EvidenceRoot)) {
    if (-not $Path.StartsWith($Prefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $Path)) {
        throw 'A Sandbox QA working path is unsafe or already exists.'
    }
}
New-Item -ItemType Directory -Path $StageRoot, $EvidenceRoot -Force | Out-Null

$BaselineClientPids = @(Get-UsageGuardSandboxClientSnapshot | ForEach-Object ProcessId)
$Launcher = $null
$OwnedTarget = $null
try {
    $AppStage = Join-Path $StageRoot 'app'
    $TestStage = Join-Path $StageRoot 'tests'
    & dotnet publish (Join-Path $RepositoryRoot 'src\CodexUsageGuard\CodexUsageGuard.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -o $AppStage
    if ($LASTEXITCODE -ne 0) { throw 'The Sandbox application publish failed.' }
    & dotnet publish (Join-Path $RepositoryRoot 'tests\CodexUsageGuard.Tests\CodexUsageGuard.Tests.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:DebugType=None -o $TestStage
    if ($LASTEXITCODE -ne 0) { throw 'The Sandbox test publish failed.' }

    Copy-Item -LiteralPath $GuestDriver -Destination $StageRoot
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'scripts\Install-User.ps1') -Destination $StageRoot
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'scripts\Rollback-User.ps1') -Destination $StageRoot
    $AppExecutable = Join-Path $AppStage 'CodexUsageGuard.exe'
    $TestExecutable = Join-Path $TestStage 'CodexUsageGuard.Tests.exe'
    $Manifest = [ordered]@{
        schemaVersion = 1
        appExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $AppExecutable).Hash.ToLowerInvariant()
        testExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $TestExecutable).Hash.ToLowerInvariant()
        credentialsIncluded = $false
        networkRequired = $false
    }
    [IO.File]::WriteAllText(
        (Join-Path $StageRoot 'sandbox-input-manifest.json'),
        ($Manifest | ConvertTo-Json),
        [Text.UTF8Encoding]::new($false))

    $TemplateText = [IO.File]::ReadAllText($Template)
    $ConfigurationText = $TemplateText.Replace(
        '{{INPUT_HOST_FOLDER}}',
        [Security.SecurityElement]::Escape($StageRoot))
    $ConfigurationText = $ConfigurationText.Replace(
        '{{EVIDENCE_HOST_FOLDER}}',
        [Security.SecurityElement]::Escape($EvidenceRoot))
    if ($ConfigurationText.IndexOf('{{', [StringComparison]::Ordinal) -ge 0) {
        throw 'The Sandbox configuration template was not fully rendered.'
    }
    $ConfigurationPath = Join-Path $StageRoot 'UsageGuard-QA.wsb'
    [IO.File]::WriteAllText(
        $ConfigurationPath,
        $ConfigurationText,
        [Text.UTF8Encoding]::new($false))

    $LaunchStartedAtUtc = [DateTimeOffset]::UtcNow
    $Launcher = Start-Process -FilePath $SandboxExecutable `
        -ArgumentList ('"' + $ConfigurationPath + '"') -PassThru
    $TargetDeadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
    do {
        Start-Sleep -Milliseconds 250
        try {
            $OwnedTarget = Select-UsageGuardSandboxClientTarget `
                -Clients @(Get-UsageGuardSandboxClientSnapshot) `
                -Windows @(Get-UsageGuardTopLevelWindows) `
                -BaselineClientPids $BaselineClientPids `
                -LauncherPid $Launcher.Id `
                -LaunchStartedAtUtc $LaunchStartedAtUtc `
                -ExpectedClientPath $ClientExecutable
        }
        catch {
            $OwnedTarget = $null
        }
    } while ($null -eq $OwnedTarget -and [DateTimeOffset]::UtcNow -lt $TargetDeadline)
    if ($null -eq $OwnedTarget) {
        throw 'The exact newly owned Sandbox client did not become ready.'
    }

    $CurrentDisplay = Select-UsageGuardApprovedDisplay `
        -Displays @(Get-UsageGuardDisplayInventory) `
        -ApprovedStableDeviceId $ApprovedStableDeviceId `
        -ExpectedLeft $ExpectedLeft -ExpectedTop $ExpectedTop `
        -ExpectedWidth $ExpectedWidth -ExpectedHeight $ExpectedHeight
    Assert-UsageGuardSandboxStateUnchanged -OriginalTarget $OwnedTarget `
        -CurrentTarget $OwnedTarget -OriginalDisplay $ApprovedDisplay `
        -CurrentDisplay $CurrentDisplay
    $WindowWidth = [Math]::Min(1522, $ExpectedWidth - 40)
    $WindowHeight = [Math]::Min(806, $ExpectedHeight - 40)
    $WindowLeft = $ExpectedLeft + [Math]::Max(0, [int](($ExpectedWidth - $WindowWidth) / 2))
    $WindowTop = $ExpectedTop + [Math]::Max(0, [int](($ExpectedHeight - $WindowHeight) / 2))
    [UsageGuard.SandboxQa.NativeMethods]::Place(
        $OwnedTarget.Hwnd,
        $WindowLeft,
        $WindowTop,
        $WindowWidth,
        $WindowHeight)
    [void](Assert-UsageGuardFrameContained -Hwnd $OwnedTarget.Hwnd -Display $ApprovedDisplay)

    [UsageGuard.SandboxQa.NativeMethods]::Minimize($OwnedTarget.Hwnd)
    $ReadyPath = Join-Path $EvidenceRoot 'ready-for-host-capture.json'
    $ResultPath = Join-Path $EvidenceRoot 'qa-result.json'
    $ReadyDeadline = [DateTimeOffset]::UtcNow.AddMinutes(10)
    while (-not (Test-Path -LiteralPath $ReadyPath -PathType Leaf) -and
        [DateTimeOffset]::UtcNow -lt $ReadyDeadline) {
        if (Test-Path -LiteralPath $ResultPath -PathType Leaf) {
            throw 'The isolated guest QA failed before host-capture readiness.'
        }
        if (-not (Get-Process -Id $OwnedTarget.ProcessId -ErrorAction SilentlyContinue)) {
            throw 'The owned Sandbox client exited before guest evidence was ready.'
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-Path -LiteralPath $ReadyPath -PathType Leaf)) {
        throw 'The bounded isolated QA run did not reach host-capture readiness.'
    }

    $RevalidatedTarget = Select-UsageGuardSandboxClientTarget `
        -Clients @(Get-UsageGuardSandboxClientSnapshot) `
        -Windows @(Get-UsageGuardTopLevelWindows) `
        -BaselineClientPids $BaselineClientPids `
        -LauncherPid $Launcher.Id `
        -LaunchStartedAtUtc $LaunchStartedAtUtc `
        -ExpectedClientPath $ClientExecutable
    $RevalidatedDisplay = Select-UsageGuardApprovedDisplay `
        -Displays @(Get-UsageGuardDisplayInventory) `
        -ApprovedStableDeviceId $ApprovedStableDeviceId `
        -ExpectedLeft $ExpectedLeft -ExpectedTop $ExpectedTop `
        -ExpectedWidth $ExpectedWidth -ExpectedHeight $ExpectedHeight
    Assert-UsageGuardSandboxStateUnchanged -OriginalTarget $OwnedTarget `
        -CurrentTarget $RevalidatedTarget -OriginalDisplay $ApprovedDisplay `
        -CurrentDisplay $RevalidatedDisplay
    [UsageGuard.SandboxQa.NativeMethods]::RestoreNoActivate($OwnedTarget.Hwnd)
    [UsageGuard.SandboxQa.NativeMethods]::Place(
        $OwnedTarget.Hwnd,
        $WindowLeft,
        $WindowTop,
        $WindowWidth,
        $WindowHeight)
    $HostCapture = Join-Path $EvidenceRoot 'host-exact-sandbox-client.png'
    & (Join-Path $PSScriptRoot 'Capture-SandboxClientWindow.ps1') `
        -ProcessId $OwnedTarget.ProcessId -Hwnd $OwnedTarget.Hwnd `
        -ExpectedClientPath $ClientExecutable `
        -ApprovedStableDeviceId $ApprovedStableDeviceId `
        -ExpectedLeft $ExpectedLeft -ExpectedTop $ExpectedTop `
        -ExpectedWidth $ExpectedWidth -ExpectedHeight $ExpectedHeight `
        -OutputPath $HostCapture | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $EvidenceRoot 'host-capture-complete.flag'),
        'validated-exact-window',
        [Text.UTF8Encoding]::new($false))

    $ResultDeadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
    while (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf) -and
        [DateTimeOffset]::UtcNow -lt $ResultDeadline) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) {
        throw 'The isolated QA result did not arrive.'
    }
    $Result = Get-Content -Raw -LiteralPath $ResultPath | ConvertFrom-Json
    if ($Result.schemaVersion -ne 1 -or $Result.status -cne 'passed' -or
        $Result.syntheticTestCount -lt 1 -or
        $Result.installVerified -ne $true -or $Result.rollbackVerified -ne $true -or
        $Result.networkEnabled -ne $false -or $Result.modelTaskCreated -ne $false) {
        throw 'The isolated QA result failed validation.'
    }

    $HostReport = [ordered]@{
        schemaVersion = 1
        status = 'passed'
        sandboxClientPid = $OwnedTarget.ProcessId
        sandboxClientHwnd = $OwnedTarget.Hwnd
        clientExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ClientExecutable).Hash.ToLowerInvariant()
        approvedDisplayStableId = $ApprovedDisplay.StableDeviceId
        approvedWorkingArea = @($ExpectedLeft, $ExpectedTop, $ExpectedWidth, $ExpectedHeight)
        minimizedGuestQaCompleted = $true
        exactRestoredClientCaptureSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $HostCapture).Hash.ToLowerInvariant()
        startupSplashMayAppearBeforeFinalHwnd = $true
        hostInputUsed = $false
        completedAtUtc = [DateTimeOffset]::UtcNow
    }
    [IO.File]::WriteAllText(
        (Join-Path $EvidenceRoot 'host-report.json'),
        ($HostReport | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
    $OwnedExitDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    while ((Get-Process -Id $OwnedTarget.ProcessId -ErrorAction SilentlyContinue) -and
        [DateTimeOffset]::UtcNow -lt $OwnedExitDeadline) {
        Start-Sleep -Milliseconds 250
    }
    $HostReport | ConvertTo-Json -Depth 5
}
finally {
    if ($null -ne $OwnedTarget) {
        $Owned = Get-CimInstance Win32_Process -Filter "ProcessId = $($OwnedTarget.ProcessId)" -ErrorAction SilentlyContinue
        if ($null -ne $Owned -and
            [string]$Owned.ExecutablePath -and
            [IO.Path]::GetFullPath([string]$Owned.ExecutablePath).Equals(
                [IO.Path]::GetFullPath($ClientExecutable),
                [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Process -Id $OwnedTarget.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
    if ($null -ne $Launcher) {
        $Launcher.Dispose()
    }
    if (Test-Path -LiteralPath $StageRoot) {
        $ResolvedStage = [IO.Path]::GetFullPath($StageRoot)
        if (-not $ResolvedStage.StartsWith($Prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing unsafe Sandbox stage cleanup.'
        }
        $CleanupDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
        do {
            try {
                Remove-Item -LiteralPath $ResolvedStage -Recurse -Force
            }
            catch [IO.IOException], [UnauthorizedAccessException] {
                if ([DateTimeOffset]::UtcNow -ge $CleanupDeadline) { throw }
                Start-Sleep -Milliseconds 250
            }
        } while (Test-Path -LiteralPath $ResolvedStage)
    }
}
