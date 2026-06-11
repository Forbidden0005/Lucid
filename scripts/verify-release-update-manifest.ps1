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

if (-not (Test-Path -LiteralPath $updateManifestPath -PathType Leaf)) {
    throw "Release update manifest not found: $updateManifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $updateManifestPath | ConvertFrom-Json
$expectedHash = (Get-Content -LiteralPath $hashPath | Select-Object -First 1).Split(' ')[0]

if ([string]$manifest.product -ne [string]$metadata.product) {
    throw "Update manifest product does not match release metadata."
}

if ([string]$manifest.version -ne [string]$metadata.version) {
    throw "Update manifest version does not match release metadata."
}

if ([string]$manifest.package.fileName -ne $zipName) {
    throw "Update manifest package fileName does not match expected zip name."
}

if ([string]$manifest.package.sha256 -ne $expectedHash) {
    throw "Update manifest package sha256 does not match package checksum."
}

if ([int64]$manifest.package.size -ne (Get-Item -LiteralPath $zipPath).Length) {
    throw "Update manifest package size does not match zip size."
}

if ([string]$manifest.discovery.indexRelativePath -ne "feeds/index.json") {
    throw "Update manifest discovery indexRelativePath is unexpected."
}

if ([string]$manifest.discovery.channelFeedRelativePath -ne "feeds/$([string]$metadata.channel).json") {
    throw "Update manifest discovery channelFeedRelativePath is unexpected."
}

if ([string]$manifest.support.diagnosticsBundleScript -ne "installer/Export-LucidSupportBundle.ps1") {
    throw "Update manifest diagnosticsBundleScript path is unexpected."
}

if ([string]$manifest.support.supportPolicyDoc -ne "docs/SUPPORT-AND-CRASH-POLICY.md") {
    throw "Update manifest supportPolicyDoc path is unexpected."
}

Write-Info "Verified release update manifest"
Write-Info "Manifest: $updateManifestPath"
