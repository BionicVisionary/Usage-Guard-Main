[CmdletBinding()]
param(
    [string]$PackageVersion = '0.002'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$ArtifactRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts'))
$PackageName = "UsageGuard-$PackageVersion-win-x64"
$Stage = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot "$PackageName.stage"))
$Publish = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot "$PackageName.publish"))
$Zip = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot "$PackageName.zip"))
$Installer = [IO.Path]::GetFullPath((Join-Path $ArtifactRoot "UsageGuard-Setup-$PackageVersion.exe"))
$InstallerChecksum = $Installer + '.sha256'
$Project = Join-Path $RepositoryRoot 'src\CodexUsageGuard\CodexUsageGuard.csproj'

foreach ($Path in @($Stage, $Publish)) {
    if (-not $Path.StartsWith($ArtifactRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Unsafe package working path.'
    }
    if (Test-Path -LiteralPath $Path) {
        throw "Package working path already exists: $Path"
    }
}
if (Test-Path -LiteralPath $Zip) {
    throw "Package already exists; refusing to overwrite it: $Zip"
}
if (Test-Path -LiteralPath $Installer) {
    throw "Installer already exists; refusing to overwrite it: $Installer"
}
if (Test-Path -LiteralPath $InstallerChecksum) {
    throw "Installer checksum already exists; refusing to overwrite it: $InstallerChecksum"
}

New-Item -ItemType Directory -Path $ArtifactRoot -Force | Out-Null
try {
    & dotnet publish $Project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None `
        -o $Publish
    if ($LASTEXITCODE -ne 0) {
        throw 'The self-contained Release publish failed.'
    }

    $PublishedExecutable = Join-Path $Publish 'CodexUsageGuard.exe'
    if (-not (Test-Path -LiteralPath $PublishedExecutable -PathType Leaf)) {
        throw 'The self-contained executable was not produced.'
    }

    New-Item -ItemType Directory -Path (Join-Path $Stage 'app') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Stage 'skill\scripts') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Stage 'docs') -Force | Out-Null
    Copy-Item -LiteralPath $PublishedExecutable -Destination (Join-Path $Stage 'app')

    Copy-Item -LiteralPath (Join-Path $RepositoryRoot '.agents\skills\codex-usage-guard\SKILL.md') -Destination (Join-Path $Stage 'skill')
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot '.agents\skills\codex-usage-guard\scripts\check_usage.ps1') -Destination (Join-Path $Stage 'skill\scripts')
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot '.agents\skills\codex-usage-guard\scripts\invoke_guard_process.ps1') -Destination (Join-Path $Stage 'skill\scripts')
    foreach ($Document in @(
        'README.md',
        'CHANGELOG.md',
        'CONTRIBUTING.md',
        'docs\INSTALLATION.md',
        'docs\OPERATING_GUIDE.md',
        'docs\TROUBLESHOOTING.md',
        'docs\INTEGRATION_GUIDE.md',
        'docs\ROLLBACK.md',
        'docs\ARCHITECTURE.md',
        'docs\THREAT_MODEL.md',
        'docs\IMPLEMENTATION_PLAN.md')) {
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot $Document) -Destination (Join-Path $Stage 'docs')
    }
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'scripts\Install-User.ps1') -Destination $Stage
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'scripts\Rollback-User.ps1') -Destination $Stage
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'scripts\package\Install.cmd') -Destination $Stage
    Copy-Item -LiteralPath (Join-Path $RepositoryRoot 'scripts\package\Install-With-Codex-Integration.cmd') -Destination $Stage

    $Manifest = [ordered]@{
        schemaVersion = 1
        product = 'Usage Guard'
        version = $PackageVersion
        platform = 'win-x64'
        selfContained = $true
        appExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $Stage 'app\CodexUsageGuard.exe')).Hash.ToLowerInvariant()
        codexIntegrationOptional = $true
        updateChannelConfigured = $true
        updateRepository = 'https://github.com/BionicVisionary/Usage-Guard-Main'
        credentialFilesIncluded = $false
        usageDataIncluded = $false
    }
    $ManifestJson = $Manifest | ConvertTo-Json
    [IO.File]::WriteAllText(
        (Join-Path $Stage 'manifest.json'),
        $ManifestJson,
        [Text.UTF8Encoding]::new($false))

    Compress-Archive -Path (Join-Path $Stage '*') -DestinationPath $Zip -CompressionLevel Optimal
    $InstallerResult = & (Join-Path $PSScriptRoot 'New-BootstrapperInstaller.ps1') `
        -PackageZip $Zip -OutputPath $Installer -Version $PackageVersion
    $ChecksumLine = "$($InstallerResult.Sha256.ToLowerInvariant())  $([IO.Path]::GetFileName($Installer))"
    [IO.File]::WriteAllText($InstallerChecksum, $ChecksumLine + "`r`n", [Text.Encoding]::ASCII)
    $Hash = Get-FileHash -Algorithm SHA256 -LiteralPath $Zip
    [pscustomobject]@{
        Package = $Zip
        Length = (Get-Item -LiteralPath $Zip).Length
        Sha256 = $Hash.Hash
        AppSha256 = $Manifest.appExecutableSha256
        Installer = $InstallerResult.Installer
        InstallerSha256 = $InstallerResult.Sha256
        InstallerChecksum = $InstallerChecksum
        InstallerSigned = $InstallerResult.Signed
        SelfContained = $true
    }
}
finally {
    if (Test-Path -LiteralPath $Stage) {
        Remove-Item -LiteralPath $Stage -Recurse -Force
    }
    if (Test-Path -LiteralPath $Publish) {
        Remove-Item -LiteralPath $Publish -Recurse -Force
    }
}
