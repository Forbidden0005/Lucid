param(
    [string]$ReleaseMetadataPath,
    [string]$PackagesDirectory
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

function Read-JsonFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return $null
    }

    return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
}

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

$zipPath = Join-Path $packagesDirectory $zipName
$zipHashPath = Join-Path $packagesDirectory $zipHashName
$updateManifestPath = Join-Path $packagesDirectory $updateManifestName

foreach ($requiredPath in @($zipPath, $zipHashPath, $updateManifestPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release artifact missing for update feed generation: $requiredPath"
    }
}

$feedRoot = Join-Path $packagesDirectory "feeds"
New-Item -ItemType Directory -Path $feedRoot -Force | Out-Null

$channelFeedPath = Join-Path $feedRoot ($channel + ".json")
$indexPath = Join-Path $feedRoot "index.json"

$updateManifest = Get-Content -Raw -LiteralPath $updateManifestPath | ConvertFrom-Json
$existingChannelFeed = Read-JsonFile $channelFeedPath
$existingIndex = Read-JsonFile $indexPath

$releaseEntry = [ordered]@{
    version = [string]$metadata.version
    channel = $channel
    runtimeIdentifier = [string]$metadata.runtimeIdentifier
    packaging = [string]$metadata.packaging
    signingMode = [string]$metadata.signingMode
    fileVersion = [string]$metadata.fileVersion
    informationalVersion = [string]$metadata.informationalVersion
    publishedUtc = [DateTime]::UtcNow.ToString("o")
    package = [ordered]@{
        relativePath = $zipName
        sha256RelativePath = $zipHashName
        updateManifestRelativePath = $updateManifestName
        size = [int64]$updateManifest.package.size
        sha256 = [string]$updateManifest.package.sha256
    }
    releaseNotes = [ordered]@{
        relativePath = [string]$metadata.releaseNotes
    }
    support = [ordered]@{
        diagnosticsBundleScript = [string]$updateManifest.support.diagnosticsBundleScript
        supportPolicyDoc = [string]$updateManifest.support.supportPolicyDoc
    }
    installPolicy = [ordered]@{
        requiresUserInitiatedInstall = $true
        allowDowngrade = $false
        migrationStatePath = "migrations/migration-state.json"
    }
}

$existingEntries = @()
if ($existingChannelFeed -and $existingChannelFeed.releases) {
    $existingEntries = @($existingChannelFeed.releases)
}

$mergedEntries = New-Object 'System.Collections.Generic.List[object]'
$mergedEntries.Add($releaseEntry)
foreach ($entry in $existingEntries) {
    if ([string]$entry.version -eq [string]$metadata.version -and
        [string]$entry.runtimeIdentifier -eq [string]$metadata.runtimeIdentifier -and
        [string]$entry.packaging -eq [string]$metadata.packaging) {
        continue
    }

    $mergedEntries.Add($entry)
}

$sortedEntries = @($mergedEntries |
    Sort-Object @{ Expression = { [version]$_.version }; Descending = $true }, @{ Expression = { $_.publishedUtc }; Descending = $true } |
    Select-Object -First 10)

$latestEntry = $sortedEntries[0]

$channelFeed = [ordered]@{
    product = [string]$metadata.product
    channel = $channel
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    discovery = [ordered]@{
        mode = "static-json"
        indexRelativePath = "feeds/index.json"
        channelRelativePath = "feeds/$channel.json"
        packageRootRelativePath = "."
    }
    updatePolicy = [ordered]@{
        checkIntervalHours = 24
        requiresUserInitiatedInstall = $true
        allowDowngrade = $false
        signingMode = [string]$metadata.signingMode
    }
    latest = $latestEntry
    releases = @($sortedEntries)
}

$channelFeed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $channelFeedPath -Encoding utf8

$channelIndexEntries = New-Object 'System.Collections.Generic.List[object]'
if ($existingIndex -and $existingIndex.channels) {
    foreach ($entry in $existingIndex.channels) {
        if ([string]$entry.channel -ne $channel) {
            $channelIndexEntries.Add($entry)
        }
    }
}

$channelIndexEntries.Add([ordered]@{
    channel = $channel
    relativePath = "feeds/$channel.json"
    latestVersion = [string]$latestEntry.version
    runtimeIdentifier = [string]$latestEntry.runtimeIdentifier
    packaging = [string]$latestEntry.packaging
    publishedUtc = [string]$latestEntry.publishedUtc
})

$sortedChannels = $channelIndexEntries | Sort-Object channel

$index = [ordered]@{
    product = [string]$metadata.product
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    discovery = [ordered]@{
        mode = "static-json"
        defaultChannel = $channel
    }
    channels = @($sortedChannels)
}

$index | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $indexPath -Encoding utf8

Write-Info "Generated release update feed"
Write-Info "Channel feed: $channelFeedPath"
Write-Info "Index: $indexPath"
