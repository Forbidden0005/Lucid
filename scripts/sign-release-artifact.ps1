param(
    [string]$PublishDirectory,
    [string]$ReleaseMetadataPath
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

function Resolve-SignToolPath {
    $command = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitsRoot) {
        $candidate = Get-ChildItem -LiteralPath $kitsRoot -Recurse -Filter signtool.exe -File |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    throw "signtool.exe not found. Install Windows SDK signing tools or place signtool.exe on PATH."
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

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json
$signingMode = [string]$metadata.signingMode

if ($signingMode -eq "unsigned-ci-artifact") {
    Write-Info "Signing skipped because signingMode is unsigned-ci-artifact."
    return
}

if ($signingMode -ne "authenticode-required") {
    throw "Unsupported signing mode '$signingMode'."
}

$certPath = $env:LUCID_SIGNING_CERT_PATH
$certPassword = $env:LUCID_SIGNING_CERT_PASSWORD
$timestampUrl = if ([string]::IsNullOrWhiteSpace($env:LUCID_SIGNING_TIMESTAMP_URL)) {
    "http://timestamp.digicert.com"
} else {
    $env:LUCID_SIGNING_TIMESTAMP_URL
}

if ([string]::IsNullOrWhiteSpace($certPath)) {
    throw "LUCID_SIGNING_CERT_PATH is required when signingMode=authenticode-required."
}

if ([string]::IsNullOrWhiteSpace($certPassword)) {
    throw "LUCID_SIGNING_CERT_PASSWORD is required when signingMode=authenticode-required."
}

$certPath = [System.IO.Path]::GetFullPath($certPath)
if (-not (Test-Path -LiteralPath $certPath -PathType Leaf)) {
    throw "Signing certificate file not found: $certPath"
}

$signtoolPath = Resolve-SignToolPath

$targets = @(
    (Join-Path $publishDirectory "Lucid.App.exe"),
    (Join-Path $publishDirectory "Lucid.App.dll")
)

foreach ($target in $targets) {
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Signing target not found: $target"
    }
}

foreach ($target in $targets) {
    & $signtoolPath sign /fd SHA256 /tr $timestampUrl /td SHA256 /f $certPath /p $certPassword $target
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for '$target' with exit code $LASTEXITCODE."
    }
}

Write-Info "Signed release artifact targets"
Write-Info "Signing mode: $signingMode"
Write-Info "Timestamp URL: $timestampUrl"
