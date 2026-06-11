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
$packagesDirectory = [System.IO.Path]::GetFullPath($PackagesDirectory)

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

if (-not (Test-Path -LiteralPath $packagesDirectory -PathType Container)) {
    throw "Packages directory not found: $packagesDirectory"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json
$productSlug = ([string]$metadata.product).ToLowerInvariant().Replace(' ', '-')
$channel = [string]$metadata.channel
$archiveBaseName = "$productSlug-$([string]$metadata.version)-$channel-$([string]$metadata.runtimeIdentifier)-$([string]$metadata.packaging)"
$zipName = $archiveBaseName + ".zip"
$zipHashName = $zipName + ".sha256"
$updateManifestName = $archiveBaseName + ".update.json"

$feedRoot = Join-Path $packagesDirectory "feeds"
$channelFeedPath = Join-Path $feedRoot ($channel + ".json")
$indexPath = Join-Path $feedRoot "index.json"

foreach ($requiredPath in @($channelFeedPath, $indexPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Release update feed file not found: $requiredPath"
    }
}

$channelFeed = Get-Content -Raw -LiteralPath $channelFeedPath | ConvertFrom-Json
$index = Get-Content -Raw -LiteralPath $indexPath | ConvertFrom-Json

if ([string]$channelFeed.product -ne [string]$metadata.product) {
    throw "Channel feed product does not match release metadata."
}

if ([string]$channelFeed.channel -ne $channel) {
    throw "Channel feed channel does not match release metadata."
}

if ([string]$channelFeed.discovery.indexRelativePath -ne "feeds/index.json") {
    throw "Channel feed indexRelativePath is unexpected."
}

if ([string]$channelFeed.discovery.channelRelativePath -ne "feeds/$channel.json") {
    throw "Channel feed channelRelativePath is unexpected."
}

$latest = $channelFeed.latest
if (-not $latest) {
    throw "Channel feed latest entry is missing."
}

if ([string]$latest.version -ne [string]$metadata.version) {
    throw "Channel feed latest version does not match release metadata."
}

if ([string]$latest.package.relativePath -ne $zipName) {
    throw "Channel feed latest package relativePath does not match current package."
}

if ([string]$latest.package.sha256RelativePath -ne $zipHashName) {
    throw "Channel feed latest package sha256RelativePath does not match current package hash file."
}

if ([string]$latest.package.updateManifestRelativePath -ne $updateManifestName) {
    throw "Channel feed latest package updateManifestRelativePath does not match current update manifest."
}

if ([string]$latest.installPolicy.migrationStatePath -ne "migrations/migration-state.json") {
    throw "Channel feed migrationStatePath is unexpected."
}

$matchingRelease = @($channelFeed.releases | Where-Object {
    ([string]$_.version -eq [string]$metadata.version) -and
    ([string]$_.runtimeIdentifier -eq [string]$metadata.runtimeIdentifier) -and
    ([string]$_.packaging -eq [string]$metadata.packaging)
})

if ($matchingRelease.Count -ne 1) {
    throw "Channel feed must contain exactly one current release entry."
}

$channelIndexEntry = @($index.channels | Where-Object { [string]$_.channel -eq $channel })
if ($channelIndexEntry.Count -ne 1) {
    throw "Feed index must contain exactly one entry for channel '$channel'."
}

$channelIndexEntry = $channelIndexEntry[0]
if ([string]$channelIndexEntry.relativePath -ne "feeds/$channel.json") {
    throw "Feed index relativePath is unexpected for channel '$channel'."
}

if ([string]$channelIndexEntry.latestVersion -ne [string]$metadata.version) {
    throw "Feed index latestVersion does not match release metadata."
}

Write-Info "Verified release update feed"
Write-Info "Channel feed: $channelFeedPath"
Write-Info "Index: $indexPath"
