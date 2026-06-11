param(
    [string]$ReleaseMetadataPath,
    [string]$OperationsPolicyPath
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $ReleaseMetadataPath = Join-Path $repoRoot "release\release-metadata.json"
}

if ([string]::IsNullOrWhiteSpace($OperationsPolicyPath)) {
    $OperationsPolicyPath = Join-Path $repoRoot "release\release-operations.json"
}

$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)
$operationsPolicyPath = [System.IO.Path]::GetFullPath($OperationsPolicyPath)

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

if (-not (Test-Path -LiteralPath $operationsPolicyPath -PathType Leaf)) {
    throw "Release operations policy not found: $operationsPolicyPath"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json
$policy = Get-Content -Raw -LiteralPath $operationsPolicyPath | ConvertFrom-Json

if ([string]$policy.product -ne [string]$metadata.product) {
    throw "Release operations product does not match release metadata."
}

$channel = [string]$metadata.channel
$matchingChannels = @($policy.channels | Where-Object { [string]$_.name -eq $channel })
if ($matchingChannels.Count -ne 1) {
    throw "Release operations policy must contain exactly one entry for channel '$channel'."
}

$channelPolicy = $matchingChannels[0]
if ([string]$channelPolicy.feedIndex -ne "release/packages/feeds/index.json") {
    throw "Channel '$channel' feedIndex is unexpected."
}

if ([string]$channelPolicy.feedFile -ne "release/packages/feeds/$channel.json") {
    throw "Channel '$channel' feedFile is unexpected."
}

if ([string]$channelPolicy.ownerRole -notin @("release-engineering")) {
    throw "Channel '$channel' ownerRole is unsupported."
}

if ([string]$channelPolicy.rolloutPolicy -notin @("manual-full-channel")) {
    throw "Channel '$channel' rolloutPolicy is unsupported."
}

if ([string]$policy.defaultChannel -notin @("preview", "stable", "internal")) {
    throw "Default channel is unsupported."
}

if ([string]$metadata.signingMode -notin @($policy.signingPolicy.allowedModes)) {
    throw "Release metadata signingMode is not allowed by release operations policy."
}

foreach ($relativeDocPath in @(
    [string]$policy.supportPolicy.supportPolicyDoc,
    [string]$policy.supportPolicy.triageGuideDoc,
    [string]$policy.releaseChecklistDoc,
    [string]$policy.updatePublicationDoc,
    [string]$policy.supportPolicy.bundleExportScript
)) {
    $fullPath = Join-Path $repoRoot $relativeDocPath
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Release operations references missing path '$relativeDocPath'."
    }
}

$symbolStorePath = Join-Path $repoRoot ([string]$policy.symbolPolicy.storePath)
if (-not (Test-Path -LiteralPath $symbolStorePath -PathType Container)) {
    throw "Release operations symbol store path not found: $symbolStorePath"
}

Write-Info "Release operations policy validated"
Write-Info "Channel: $channel"
Write-Info "Operations policy: $operationsPolicyPath"
