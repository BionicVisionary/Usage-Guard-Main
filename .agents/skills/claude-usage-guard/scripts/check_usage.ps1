$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($args.Count -ne 0) {
    [Console]::Error.WriteLine('claude-usage-guard accepts no arguments')
    exit 64
}

$LocatorPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OpenAI\CodexUsageGuard\installation.json'
$ProcessHelper = Join-Path $PSScriptRoot 'invoke_guard_process.ps1'
try {
    $LocatorFile = Get-Item -LiteralPath $LocatorPath
    if ($LocatorFile.Length -gt 4096) { throw 'locator too large' }
    $Locator = Get-Content -Raw -LiteralPath $LocatorPath | ConvertFrom-Json
    if ($Locator.schemaVersion -ne 1 -or
        $Locator.executablePath -isnot [string] -or
        $Locator.executableSha256 -notmatch '^[a-fA-F0-9]{64}$' -or
        -not [IO.Path]::IsPathRooted($Locator.executablePath) -or
        [IO.Path]::GetFileName($Locator.executablePath) -cne 'CodexUsageGuard.exe') {
        throw 'locator invalid'
    }
    $Executable = [IO.Path]::GetFullPath($Locator.executablePath)
    if (-not (Test-Path -LiteralPath $Executable -PathType Leaf) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $Executable).Hash -cne
            $Locator.executableSha256.ToUpperInvariant()) {
        throw 'executable provenance invalid'
    }
    . $ProcessHelper
    $Result = Invoke-CodexUsageGuardProcess -ExecutablePath $Executable `
        -Arguments '--provider-guard-check claude' -TimeoutMilliseconds 5000
    $Decision = ConvertFrom-ClaudeUsageGuardDecisionOutput `
        -StandardOutput $Result.StandardOutput -ExitCode $Result.ExitCode
    [Console]::Out.WriteLine(($Decision | ConvertTo-Json -Compress -Depth 5))
    exit $Result.ExitCode
}
catch {
    [Console]::Error.WriteLine('claude-usage-guard check failed safely')
    exit 2
}
