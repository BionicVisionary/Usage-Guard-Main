[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][long]$Hwnd,
    [Parameter(Mandatory = $true)][string]$ExpectedClientPath,
    [Parameter(Mandatory = $true)][string]$ApprovedStableDeviceId,
    [Parameter(Mandatory = $true)][int]$ExpectedLeft,
    [Parameter(Mandatory = $true)][int]$ExpectedTop,
    [Parameter(Mandatory = $true)][int]$ExpectedWidth,
    [Parameter(Mandatory = $true)][int]$ExpectedHeight,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ModuleRoot = $PSScriptRoot
Import-Module (Join-Path $ModuleRoot 'SandboxWindowPolicy.psm1') -Force
Import-Module (Join-Path $ModuleRoot 'SandboxHostNative.psm1') -Force

$Display = Select-UsageGuardApprovedDisplay `
    -Displays @(Get-UsageGuardDisplayInventory) `
    -ApprovedStableDeviceId $ApprovedStableDeviceId `
    -ExpectedLeft $ExpectedLeft -ExpectedTop $ExpectedTop `
    -ExpectedWidth $ExpectedWidth -ExpectedHeight $ExpectedHeight

Save-UsageGuardSandboxClientCapture -ProcessId $ProcessId -Hwnd $Hwnd `
    -ExpectedClientPath $ExpectedClientPath -ApprovedDisplay $Display `
    -OutputPath ([IO.Path]::GetFullPath($OutputPath))

[pscustomobject]@{
    schemaVersion = 1
    processId = $ProcessId
    hwnd = $Hwnd
    stableDisplayId = $Display.StableDeviceId
    captureSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $OutputPath).Hash.ToLowerInvariant()
} | ConvertTo-Json -Compress
