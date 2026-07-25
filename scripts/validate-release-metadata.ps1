$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$metadataPath = Join-Path $repoRoot "release\release-metadata.json"

if (-not (Test-Path $propsPath)) {
    throw "Missing version source file: $propsPath"
}

if (-not (Test-Path $metadataPath)) {
    throw "Missing release metadata file: $metadataPath"
}

[xml]$props = Get-Content -Raw $propsPath
$metadata = Get-Content -Raw $metadataPath | ConvertFrom-Json

$version = [string]$metadata.version
$fileVersion = [string]$metadata.fileVersion
$informationalVersion = [string]$metadata.informationalVersion
$channel = [string]$metadata.channel
$packaging = [string]$metadata.packaging
$runtimeIdentifier = [string]$metadata.runtimeIdentifier
$signingMode = [string]$metadata.signingMode
$releaseNotes = Join-Path $repoRoot ([string]$metadata.releaseNotes)

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "release-metadata.json version must be SemVer core (x.y.z). Found: $version"
}

if ($fileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "release-metadata.json fileVersion must be four-part numeric version. Found: $fileVersion"
}

if ([string]::IsNullOrWhiteSpace($informationalVersion)) {
    throw "release-metadata.json informationalVersion must not be blank."
}

if ($informationalVersion -notlike "$version*") {
    throw "informationalVersion must begin with version. Found: $informationalVersion"
}

if ($channel -notin @("preview", "stable", "internal")) {
    throw "Unsupported release channel '$channel'."
}

if ($packaging -ne "unpackaged-self-contained") {
    throw "Current release path only supports packaging 'unpackaged-self-contained'. Found: $packaging"
}

if ($runtimeIdentifier -ne "win-x64") {
    throw "Current release path only supports runtimeIdentifier 'win-x64'. Found: $runtimeIdentifier"
}

if ($signingMode -notin @("unsigned-ci-artifact", "authenticode-required")) {
    throw "Unsupported signingMode '$signingMode'."
}

if ($signingMode -eq "unsigned-ci-artifact" -and -not [bool]$metadata.signingRequiredBeforeDistribution) {
    throw "unsigned-ci-artifact requires signingRequiredBeforeDistribution=true."
}

if (-not (Test-Path $releaseNotes)) {
    throw "Referenced release notes file does not exist: $releaseNotes"
}

$project = $props.Project

if (-not $project.PropertyGroup) {
    throw "Directory.Build.props is missing a PropertyGroup."
}

# Directory.Build.props carries several PropertyGroups (versioning, warning
# ratchet, ...), so resolve each property by name across all of them rather
# than indexing one group. Reading `.Version` off the collection would join
# every group's value and silently produce a padded string.
function Get-PropsValue([string]$name) {
    $nodes = @($project.SelectNodes("PropertyGroup/$name"))

    if ($nodes.Count -gt 1) {
        throw "Directory.Build.props declares <$name> $($nodes.Count) times; it must be declared exactly once."
    }

    if ($nodes.Count -eq 0) { return "" }

    return [string]$nodes[0].InnerText
}

$propVersion = Get-PropsValue "Version"
$propAssemblyVersion = Get-PropsValue "AssemblyVersion"
$propFileVersion = Get-PropsValue "FileVersion"
$propInformationalVersion = Get-PropsValue "InformationalVersion"

if ($propVersion -ne $version) {
    throw "Directory.Build.props Version '$propVersion' does not match release metadata version '$version'."
}

if ($propFileVersion -ne $fileVersion) {
    throw "Directory.Build.props FileVersion '$propFileVersion' does not match release metadata fileVersion '$fileVersion'."
}

if ($propInformationalVersion -ne $informationalVersion) {
    throw "Directory.Build.props InformationalVersion '$propInformationalVersion' does not match release metadata informationalVersion '$informationalVersion'."
}

if ($propAssemblyVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    throw "Directory.Build.props AssemblyVersion must be four-part numeric version. Found: $propAssemblyVersion"
}

Write-Info "Release metadata validated"
Write-Info "Version: $version"
Write-Info "FileVersion: $fileVersion"
Write-Info "Channel: $channel"
Write-Info "SigningMode: $signingMode"
