[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProcessHelper,

    [Parameter(Mandatory = $true)]
    [string]$WinExe,

    [ValidateSet('Capture', 'Validate', 'ValidateClaude')]
    [string]$Mode = 'Capture',

    [string]$DecisionJson,

    [int]$DecisionExitCode = 0
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. $ProcessHelper
if ($Mode -eq 'Validate') {
    ConvertFrom-CodexUsageGuardDecisionOutput `
        -StandardOutput $DecisionJson `
        -ExitCode $DecisionExitCode
}
elseif ($Mode -eq 'ValidateClaude') {
    ConvertFrom-ClaudeUsageGuardDecisionOutput `
        -StandardOutput $DecisionJson `
        -ExitCode $DecisionExitCode | ConvertTo-Json -Compress -Depth 5
}
else {
    $Result = Invoke-CodexUsageGuardProcess `
        -ExecutablePath $WinExe `
        -Arguments '--unsupported-wrapper-test-argument' `
        -TimeoutMilliseconds 5000

    $Result | ConvertTo-Json -Compress
}
