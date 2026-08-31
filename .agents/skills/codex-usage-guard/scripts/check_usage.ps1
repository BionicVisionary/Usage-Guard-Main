$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($args.Count -ne 0) {
    [Console]::Error.WriteLine('codex-usage-guard accepts no arguments')
    exit 64
}

$DefaultExecutable = Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex\tools\codex-usage-guard\CodexUsageGuard.exe'
$LocatorPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OpenAI\CodexUsageGuard\installation.json'
$ProcessHelper = Join-Path $PSScriptRoot 'invoke_guard_process.ps1'

function Resolve-CodexUsageGuardExecutable {
    if (-not (Test-Path -LiteralPath $LocatorPath -PathType Leaf)) {
        return $DefaultExecutable
    }

    try {
        $LocatorFile = Get-Item -LiteralPath $LocatorPath
        if ($LocatorFile.Length -gt 4096) {
            throw 'locator is too large'
        }
        $Locator = Get-Content -Raw -LiteralPath $LocatorPath | ConvertFrom-Json
        $PropertyNames = @($Locator.PSObject.Properties.Name | Sort-Object)
        if (($PropertyNames -join ',') -ne 'executablePath,executableSha256,schemaVersion') {
            throw 'locator schema is not exact'
        }
        if ($Locator.schemaVersion -ne 1 -or
            $Locator.executablePath -isnot [string] -or
            $Locator.executableSha256 -isnot [string] -or
            -not [IO.Path]::IsPathRooted($Locator.executablePath) -or
            [IO.Path]::GetFileName($Locator.executablePath) -cne 'CodexUsageGuard.exe' -or
            $Locator.executableSha256 -notmatch '^[a-fA-F0-9]{64}$') {
            throw 'locator values are invalid'
        }

        $Resolved = [IO.Path]::GetFullPath($Locator.executablePath)
        if (-not (Test-Path -LiteralPath $Resolved -PathType Leaf)) {
            throw 'located executable is unavailable'
        }
        $ActualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Resolved).Hash
        if ($ActualHash -cne $Locator.executableSha256.ToUpperInvariant()) {
            throw 'located executable hash changed'
        }
        return $Resolved
    }
    catch {
        [Console]::Error.WriteLine('codex-usage-guard installation locator is invalid')
        exit 2
    }
}

$CodexGuardExecutable = Resolve-CodexUsageGuardExecutable

if (-not (Test-Path -LiteralPath $CodexGuardExecutable -PathType Leaf)) {
    [Console]::Error.WriteLine('codex-usage-guard executable is unavailable')
    exit 2
}
if (-not (Test-Path -LiteralPath $ProcessHelper -PathType Leaf)) {
    [Console]::Error.WriteLine('codex-usage-guard process helper is unavailable')
    exit 2
}

. $ProcessHelper

try {
    $Result = Invoke-CodexUsageGuardProcess `
        -ExecutablePath $CodexGuardExecutable `
        -Arguments '--guard-check' `
        -TimeoutMilliseconds 40000
}
catch {
    [Console]::Error.WriteLine('codex-usage-guard check failed safely')
    exit 2
}

try {
    $ValidatedDecision = ConvertFrom-CodexUsageGuardDecisionOutput `
        -StandardOutput $Result.StandardOutput `
        -ExitCode $Result.ExitCode
}
catch {
    [Console]::Error.WriteLine('codex-usage-guard returned an invalid decision')
    exit 2
}

[Console]::Out.WriteLine($ValidatedDecision)
exit $Result.ExitCode
