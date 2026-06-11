param(
    [string]$PublishDirectory,
    [string]$ReleaseMetadataPath
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

function Get-RelativeArtifactPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootPath,

        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )

    $rootWithSeparator = $RootPath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $FilePath.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "File '$FilePath' is not rooted under '$RootPath'."
    }

    return $FilePath.Substring($rootWithSeparator.Length).Replace('\', '/')
}

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repoRoot "lucid-desktop\Lucid.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
}

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $ReleaseMetadataPath = Join-Path $repoRoot "release\release-metadata.json"
}

$publishDirectory    = [System.IO.Path]::GetFullPath($PublishDirectory)
$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)

if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
    throw "Publish directory not found: $publishDirectory"
}

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

$manifestPath  = Join-Path $publishDirectory "release-artifact-manifest.json"
$checksumsPath = Join-Path $publishDirectory "RELEASE-SHA256.txt"
$smokeResultPath = Join-Path $publishDirectory "release-smoke-result.json"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Artifact manifest not found: $manifestPath"
}

if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw "Checksum file not found: $checksumsPath"
}

$releaseMetadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json
$manifest        = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json

$expectedFields = @("product", "version", "fileVersion", "informationalVersion", "channel", "runtimeIdentifier", "packaging", "signingMode")
foreach ($field in $expectedFields) {
    if ([string]$manifest.$field -ne [string]$releaseMetadata.$field) {
        throw "Manifest field '$field' does not match release metadata."
    }
}

if ([bool]$manifest.signingRequiredBeforeDistribution -ne [bool]$releaseMetadata.signingRequiredBeforeDistribution) {
    throw "Manifest signingRequiredBeforeDistribution does not match release metadata."
}

$entryPointPath = Join-Path $publishDirectory ([string]$manifest.entryPoint)
if (-not (Test-Path -LiteralPath $entryPointPath -PathType Leaf)) {
    throw "Artifact entry point is missing: $entryPointPath"
}

if (-not [string]::IsNullOrWhiteSpace([string]$manifest.smokeResultFile)) {
    $declaredSmokeResultPath = Join-Path $publishDirectory ([string]$manifest.smokeResultFile)
    if (Test-Path -LiteralPath $declaredSmokeResultPath -PathType Leaf) {
        $smokeResultPath = $declaredSmokeResultPath
    }
}

$artifactFiles = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object { $_.Name -notin @("release-artifact-manifest.json", "RELEASE-SHA256.txt") } |
    Sort-Object FullName

$manifestFilesByPath = @{}
foreach ($file in $manifest.files) {
    $manifestFilesByPath[[string]$file.path] = $file
}

if ($artifactFiles.Count -ne [int]$manifest.fileCount) {
    throw "Artifact file count mismatch. Manifest=$($manifest.fileCount) Actual=$($artifactFiles.Count)"
}

$totalBytes = [int64]0
$computedChecksums = New-Object System.Collections.Generic.List[string]

foreach ($artifactFile in $artifactFiles) {
    $relativePath = Get-RelativeArtifactPath -RootPath $publishDirectory -FilePath $artifactFile.FullName
    if (-not $manifestFilesByPath.ContainsKey($relativePath)) {
        throw "Artifact file is missing from manifest: $relativePath"
    }

    $manifestEntry = $manifestFilesByPath[$relativePath]
    if ([int64]$manifestEntry.size -ne [int64]$artifactFile.Length) {
        throw "Size mismatch for $relativePath."
    }

    $hash = Get-FileHash -LiteralPath $artifactFile.FullName -Algorithm SHA256
    $sha  = $hash.Hash.ToLowerInvariant()
    if ([string]$manifestEntry.sha256 -ne $sha) {
        throw "SHA256 mismatch for $relativePath."
    }

    $computedChecksums.Add($sha + " *" + $relativePath)
    $totalBytes += $artifactFile.Length
}

if ($totalBytes -ne [int64]$manifest.totalBytes) {
    throw "Artifact totalBytes mismatch. Manifest=$($manifest.totalBytes) Actual=$totalBytes"
}

$storedChecksumLines = Get-Content -LiteralPath $checksumsPath
if ($storedChecksumLines.Count -ne $computedChecksums.Count) {
    throw "Checksum line count mismatch. Stored=$($storedChecksumLines.Count) Actual=$($computedChecksums.Count)"
}

for ($i = 0; $i -lt $computedChecksums.Count; $i++) {
    if ($storedChecksumLines[$i] -ne $computedChecksums[$i]) {
        throw "Checksum file mismatch at line $($i + 1)."
    }
}

if (Test-Path -LiteralPath $smokeResultPath -PathType Leaf) {
    $smokeResult = Get-Content -Raw -LiteralPath $smokeResultPath | ConvertFrom-Json
    $smokeStatus = [string]$smokeResult.status
    if ($smokeStatus -notin @("passed", "skipped")) {
        throw "Release smoke result is not acceptable. Status=$smokeStatus"
    }
}

if ([string]$manifest.signingMode -eq "authenticode-required") {
    $filesRequiringSignature = @(
        (Join-Path $publishDirectory "Lucid.App.exe"),
        (Join-Path $publishDirectory "Lucid.App.dll")
    )

    foreach ($signedFile in $filesRequiringSignature) {
        if (-not (Test-Path -LiteralPath $signedFile -PathType Leaf)) {
            throw "Signed artifact verification target missing: $signedFile"
        }

        $signature = Get-AuthenticodeSignature -FilePath $signedFile
        if ($signature.Status -ne "Valid") {
            throw "Authenticode signature is not valid for '$signedFile'. Status=$($signature.Status)"
        }
    }
}

Write-Info "Verified release artifact"
Write-Info "Publish directory: $publishDirectory"
Write-Info "Files verified: $($artifactFiles.Count)"
Write-Info "Signing mode: $($manifest.signingMode)"
