param(
    [string]$PublishDirectory,
    [string]$ReleaseMetadataPath,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repoRoot "lucid-desktop\Lucid.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
}

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $ReleaseMetadataPath = Join-Path $repoRoot "release\release-metadata.json"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release\packages"
}

$publishDirectory    = [System.IO.Path]::GetFullPath($PublishDirectory)
$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)
$outputDirectory     = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
    throw "Publish directory not found: $publishDirectory"
}

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json

$productSlug = ([string]$metadata.product).ToLowerInvariant().Replace(' ', '-')
$channel     = [string]$metadata.channel
$version     = [string]$metadata.version
$rid         = [string]$metadata.runtimeIdentifier
$packaging   = [string]$metadata.packaging
$releaseNotesPath = Join-Path $repoRoot ([string]$metadata.releaseNotes)
$supportPolicyPath = Join-Path $repoRoot "docs\support-and-crash-policy.md"
$installerSource = Join-Path $repoRoot "installer"

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$archiveBaseName = "$productSlug-$version-$channel-$rid-$packaging"
$zipPath         = Join-Path $outputDirectory ($archiveBaseName + ".zip")
$hashPath        = Join-Path $outputDirectory ($archiveBaseName + ".zip.sha256")
$stagingParent   = Join-Path $outputDirectory "_staging"
$stagingRoot     = Join-Path $stagingParent $archiveBaseName

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path -LiteralPath $hashPath) {
    Remove-Item -LiteralPath $hashPath -Force
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

$stagingAppRoot = Join-Path $stagingRoot "app"
$stagingInstallerRoot = Join-Path $stagingRoot "installer"
$stagingDocsRoot = Join-Path $stagingRoot "docs"

New-Item -ItemType Directory -Path $stagingAppRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingInstallerRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDocsRoot -Force | Out-Null

Get-ChildItem -LiteralPath $publishDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stagingAppRoot -Recurse -Force
}

if (-not (Test-Path -LiteralPath $installerSource -PathType Container)) {
    throw "Installer source directory not found: $installerSource"
}

Get-ChildItem -LiteralPath $installerSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $stagingInstallerRoot -Recurse -Force
}

if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
    throw "Release notes file not found: $releaseNotesPath"
}

Copy-Item -LiteralPath $releaseNotesPath -Destination (Join-Path $stagingDocsRoot "RELEASE-NOTES.md") -Force

if (-not (Test-Path -LiteralPath $supportPolicyPath -PathType Leaf)) {
    throw "Support policy file not found: $supportPolicyPath"
}

Copy-Item -LiteralPath $supportPolicyPath -Destination (Join-Path $stagingDocsRoot "SUPPORT-AND-CRASH-POLICY.md") -Force

Push-Location $stagingRoot
try {
    Compress-Archive -Path * -DestinationPath $zipPath -CompressionLevel Optimal
}
finally {
    Pop-Location
}

$zipHash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
Set-Content -LiteralPath $hashPath -Value ($zipHash.Hash.ToLowerInvariant() + " *" + [System.IO.Path]::GetFileName($zipPath)) -Encoding ascii

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

if (Test-Path -LiteralPath $stagingParent -PathType Container) {
    $remaining = Get-ChildItem -LiteralPath $stagingParent -Force
    if ($remaining.Count -eq 0) {
        Remove-Item -LiteralPath $stagingParent -Force
    }
}

Write-Info "Packaged release artifact"
Write-Info "Archive: $zipPath"
Write-Info "Checksum: $hashPath"
