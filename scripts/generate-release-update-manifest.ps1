param(
    [string]$ReleaseMetadataPath,
    [string]$PackagesDirectory
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $ReleaseMetadataPath = Join-Path $repoRoot "release\release-metadata.json"
}

if ([string]::IsNullOrWhiteSpace($PackagesDirectory)) {
    $PackagesDirectory = Join-Path $repoRoot "release\packages"
}

$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)
$packagesDirectory   = [System.IO.Path]::GetFullPath($PackagesDirectory)

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

if (-not (Test-Path -LiteralPath $packagesDirectory -PathType Container)) {
    throw "Packages directory not found: $packagesDirectory"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json
$productSlug = ([string]$metadata.product).ToLowerInvariant().Replace(' ', '-')
$archiveBaseName = "$productSlug-$([string]$metadata.version)-$([string]$metadata.channel)-$([string]$metadata.runtimeIdentifier)-$([string]$metadata.packaging)"
$zipName = $archiveBaseName + ".zip"
$zipPath = Join-Path $packagesDirectory $zipName
$hashPath = Join-Path $packagesDirectory ($zipName + ".sha256")
$updateManifestPath = Join-Path $packagesDirectory ($archiveBaseName + ".update.json")

if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Release package not found: $zipPath"
}

if (-not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
    throw "Release package checksum not found: $hashPath"
}

$hashLine = Get-Content -LiteralPath $hashPath | Select-Object -First 1
$sha256 = $hashLine.Split(' ')[0]
$releaseNotesPath = [string]$metadata.releaseNotes

$manifest = [ordered]@{
    product = [string]$metadata.product
    version = [string]$metadata.version
    channel = [string]$metadata.channel
    runtimeIdentifier = [string]$metadata.runtimeIdentifier
    packaging = [string]$metadata.packaging
    signingMode = [string]$metadata.signingMode
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    package = [ordered]@{
        fileName = $zipName
        sha256 = $sha256
        size = (Get-Item -LiteralPath $zipPath).Length
        relativePath = $zipName
    }
    releaseNotes = [ordered]@{
        relativePath = $releaseNotesPath
    }
    discovery = [ordered]@{
        indexRelativePath = "feeds/index.json"
        channelFeedRelativePath = "feeds/$([string]$metadata.channel).json"
    }
    support = [ordered]@{
        diagnosticsBundleScript = "installer/Export-LucidSupportBundle.ps1"
        supportPolicyDoc = "docs/SUPPORT-AND-CRASH-POLICY.md"
    }
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $updateManifestPath -Encoding utf8

Write-Info "Generated release update manifest"
Write-Info "Manifest: $updateManifestPath"
