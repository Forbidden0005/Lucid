param(
    [string]$ReleaseMetadataPath,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $ReleaseMetadataPath = Join-Path $repoRoot "release\release-metadata.json"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release\packages"
}

$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)
$outputDirectory     = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    throw "Release package output directory not found: $outputDirectory"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json

$productSlug = ([string]$metadata.product).ToLowerInvariant().Replace(' ', '-')
$archiveBaseName = "$productSlug-$([string]$metadata.version)-$([string]$metadata.channel)-$([string]$metadata.runtimeIdentifier)-$([string]$metadata.packaging)"
$zipPath  = Join-Path $outputDirectory ($archiveBaseName + ".zip")
$hashPath = Join-Path $outputDirectory ($archiveBaseName + ".zip.sha256")

if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Release package not found: $zipPath"
}

if (-not (Test-Path -LiteralPath $hashPath -PathType Leaf)) {
    throw "Release package checksum not found: $hashPath"
}

$computedHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedLine = $computedHash + " *" + [System.IO.Path]::GetFileName($zipPath)
$storedLine   = (Get-Content -LiteralPath $hashPath | Select-Object -First 1)

if ($storedLine -ne $expectedLine) {
    throw "Release package checksum mismatch."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $requiredEntries = @(
        "app/Lucid.App.exe",
        "app/Lucid.App.dll",
        "app/RELEASE-SHA256.txt",
        "app/release-artifact-manifest.json",
        "app/release-smoke-result.json",
        "app/SMOKE-TEST-CHECKLIST.md",
        "installer/Install-Lucid.ps1",
        "installer/Uninstall-Lucid.ps1",
        "installer/Migrate-LucidData.ps1",
        "installer/Export-LucidSupportBundle.ps1",
        "docs/RELEASE-NOTES.md",
        "docs/SUPPORT-AND-CRASH-POLICY.md"
    )

    $entryNames = $archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') }
    foreach ($requiredEntry in $requiredEntries) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Release package is missing required entry '$requiredEntry'."
        }
    }
}
finally {
    $archive.Dispose()
}

Write-Info "Verified release package"
Write-Info "Archive: $zipPath"
Write-Info "Entries: $($entryNames.Count)"
