param(
    [string]$PublishDirectory,
    [string]$ReleaseMetadataPath,
    [string]$SmokeChecklistPath
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

if ([string]::IsNullOrWhiteSpace($SmokeChecklistPath)) {
    $SmokeChecklistPath = Join-Path $repoRoot "docs\releases\smoke-test-checklist.md"
}

$publishDirectory   = [System.IO.Path]::GetFullPath($PublishDirectory)
$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)
$smokeChecklistPath = [System.IO.Path]::GetFullPath($SmokeChecklistPath)

if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
    throw "Publish directory not found: $publishDirectory"
}

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

if (-not (Test-Path -LiteralPath $smokeChecklistPath -PathType Leaf)) {
    throw "Smoke checklist not found: $smokeChecklistPath"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json

$requiredFiles = @(
    "Lucid.App.exe",
    "Lucid.App.dll",
    "Lucid.App.deps.json",
    "Lucid.App.runtimeconfig.json",
    "resources.pri"
)

foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $publishDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required publish output missing: $requiredFile"
    }
}

$artifactChecklistName = "SMOKE-TEST-CHECKLIST.md"
$artifactChecklistPath = Join-Path $publishDirectory $artifactChecklistName
Copy-Item -LiteralPath $smokeChecklistPath -Destination $artifactChecklistPath -Force

$checksumsFileName = "RELEASE-SHA256.txt"
$manifestFileName  = "release-artifact-manifest.json"
$smokeResultFileName = "release-smoke-result.json"
$checksumsPath     = Join-Path $publishDirectory $checksumsFileName
$manifestPath      = Join-Path $publishDirectory $manifestFileName

$files = Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object { $_.Name -notin @($checksumsFileName, $manifestFileName) } |
    Sort-Object FullName

$checksumLines = New-Object System.Collections.Generic.List[string]
$manifestFiles = New-Object System.Collections.Generic.List[object]
$totalBytes    = [int64]0
$publishRootWithSeparator = $publishDirectory.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar

foreach ($file in $files) {
    if ($file.FullName.StartsWith($publishRootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relativePath = $file.FullName.Substring($publishRootWithSeparator.Length).Replace('\', '/')
    }
    else {
        throw "File '$($file.FullName)' is not rooted under publish directory '$publishDirectory'."
    }

    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    $checksumLines.Add($hash.Hash.ToLowerInvariant() + " *" + $relativePath)
    $manifestFiles.Add([ordered]@{
        path   = $relativePath
        size   = $file.Length
        sha256 = $hash.Hash.ToLowerInvariant()
    })
    $totalBytes += $file.Length
}

Set-Content -LiteralPath $checksumsPath -Value $checksumLines -Encoding ascii

$manifest = [ordered]@{
    product               = [string]$metadata.product
    version               = [string]$metadata.version
    fileVersion           = [string]$metadata.fileVersion
    informationalVersion  = [string]$metadata.informationalVersion
    channel               = [string]$metadata.channel
    runtimeIdentifier     = [string]$metadata.runtimeIdentifier
    packaging             = [string]$metadata.packaging
    signingMode           = [string]$metadata.signingMode
    signingRequiredBeforeDistribution = [bool]$metadata.signingRequiredBeforeDistribution
    generatedUtc          = [DateTime]::UtcNow.ToString("o")
    entryPoint            = "Lucid.App.exe"
    checksumsFile         = $checksumsFileName
    smokeChecklist        = $artifactChecklistName
    smokeResultFile       = $smokeResultFileName
    fileCount             = $manifestFiles.Count
    totalBytes            = $totalBytes
    files                 = $manifestFiles
}

$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Info "Prepared release artifact"
Write-Info "Publish directory: $publishDirectory"
Write-Info "Files hashed: $($manifestFiles.Count)"
Write-Info "Checksums: $checksumsFileName"
Write-Info "Manifest: $manifestFileName"
Write-Info "Smoke checklist: $artifactChecklistName"
