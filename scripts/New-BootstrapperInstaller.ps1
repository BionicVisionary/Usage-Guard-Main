[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageZip,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$Version = '0.004'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$PackageZip = [IO.Path]::GetFullPath($PackageZip)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Source = Join-Path $RepositoryRoot 'installer\UsageGuardBootstrapper.cs'
if (-not (Test-Path -LiteralPath $PackageZip -PathType Leaf) -or
    -not (Test-Path -LiteralPath $Source -PathType Leaf)) {
    throw 'The verified package ZIP or installer source is missing.'
}
if ([IO.Path]::GetExtension($OutputPath) -cne '.exe' -or
    (Test-Path -LiteralPath $OutputPath)) {
    throw 'The installer output must be a new .exe file.'
}
if ($Version -cne '0.004') {
    throw 'Installer source and requested version do not match.'
}

$FrameworkRoot = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
$Compiler = Join-Path $FrameworkRoot 'csc.exe'
$Compression = Join-Path $FrameworkRoot 'System.IO.Compression.dll'
$CompressionFileSystem = Join-Path $FrameworkRoot 'System.IO.Compression.FileSystem.dll'
foreach ($Required in @($Compiler, $Compression, $CompressionFileSystem)) {
    if (-not (Test-Path -LiteralPath $Required -PathType Leaf)) {
        throw 'The built-in Windows .NET Framework compiler is unavailable.'
    }
}

$Work = Join-Path ([IO.Path]::GetTempPath()) `
    ('UsageGuard-Bootstrapper-' + [guid]::NewGuid().ToString('N'))
$Response = Join-Path $Work 'compiler.rsp'
$StdOut = Join-Path $Work 'compiler.stdout.txt'
$StdErr = Join-Path $Work 'compiler.stderr.txt'
try {
    New-Item -ItemType Directory -Path $Work -Force | Out-Null
    @(
        '/nologo',
        '/target:winexe',
        '/optimize+',
        '/platform:anycpu',
        "/out:`"$OutputPath`"",
        "/reference:`"$Compression`"",
        "/reference:`"$CompressionFileSystem`"",
        "/resource:`"$PackageZip`",UsageGuard.Payload.zip",
        "`"$Source`""
    ) | Set-Content -LiteralPath $Response -Encoding unicode

    $StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $StartInfo.FileName = $Compiler
    $StartInfo.Arguments = "@`"$Response`""
    $StartInfo.UseShellExecute = $false
    $StartInfo.CreateNoWindow = $true
    $StartInfo.RedirectStandardOutput = $true
    $StartInfo.RedirectStandardError = $true
    $Process = [Diagnostics.Process]::new()
    try {
        $Process.StartInfo = $StartInfo
        if (-not $Process.Start()) { throw 'Installer compiler did not start.' }
        $OutTask = $Process.StandardOutput.ReadToEndAsync()
        $ErrTask = $Process.StandardError.ReadToEndAsync()
        if (-not $Process.WaitForExit(300000)) {
            $Process.Kill()
            throw 'Installer compilation timed out.'
        }
        $OutTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $StdOut
        $ErrTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $StdErr
        if ($Process.ExitCode -ne 0 -or
            -not (Test-Path -LiteralPath $OutputPath -PathType Leaf)) {
            throw "Installer compilation failed with exit code $($Process.ExitCode)."
        }
    }
    finally {
        $Process.Dispose()
    }

    [pscustomobject]@{
        Installer = $OutputPath
        Length = (Get-Item -LiteralPath $OutputPath).Length
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $OutputPath).Hash
        Signed = $false
        Scope = 'CurrentUser'
        BootstrapRuntime = '.NET Framework 4.x Windows component'
    }
}
finally {
    if (Test-Path -LiteralPath $Work) {
        Remove-Item -LiteralPath $Work -Recurse -Force
    }
}
