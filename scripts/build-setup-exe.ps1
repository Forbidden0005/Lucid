param(
    [string]$PackageZipPath,
    [string]$ReleaseMetadataPath,
    [string]$OutputDirectory,
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $ReleaseMetadataPath = Join-Path $repoRoot "release\release-metadata.json"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release\setup"
}

$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)
if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json
$productSlug = ([string]$metadata.product).ToLowerInvariant().Replace(' ', '-')
$version = [string]$metadata.version
$channel = [string]$metadata.channel
$rid = [string]$metadata.runtimeIdentifier
$packaging = [string]$metadata.packaging

if ([string]::IsNullOrWhiteSpace($productSlug)) {
    throw "Release metadata is missing product."
}
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Release metadata is missing version."
}
if ([string]::IsNullOrWhiteSpace($channel)) {
    throw "Release metadata is missing channel."
}
if ([string]::IsNullOrWhiteSpace($rid)) {
    throw "Release metadata is missing runtimeIdentifier."
}
if ([string]::IsNullOrWhiteSpace($packaging)) {
    throw "Release metadata is missing packaging."
}

if ([string]::IsNullOrWhiteSpace($PackageZipPath)) {
    $packagesDirectory = Join-Path $repoRoot "release\packages"
    if (-not (Test-Path -LiteralPath $packagesDirectory -PathType Container)) {
        throw "Packages directory not found: $packagesDirectory. Run package-release-artifact.ps1 first."
    }

    $expectedPackageName = "$productSlug-$version-$channel-$rid-$packaging.zip"
    $PackageZipPath = Join-Path $packagesDirectory $expectedPackageName
}

$packageZipPath = [System.IO.Path]::GetFullPath($PackageZipPath)
$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path -LiteralPath $packageZipPath -PathType Leaf)) {
    throw "Package zip not found: $packageZipPath"
}

if ([string]::IsNullOrWhiteSpace($IsccPath)) {
    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $IsccPath = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $IsccPath) {
        $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
        if ($fromPath) { $IsccPath = $fromPath.Source }
    }
    if (-not $IsccPath) {
        throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 or pass -IsccPath."
    }
}

$setupScript = Join-Path $repoRoot "installer\Lucid-Setup.iss"
if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf)) {
    throw "Setup script not found: $setupScript"
}

$stagingRoot = Join-Path $outputDirectory ".staging"

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

try {
    Write-Info "Extracting package: $packageZipPath"
    Expand-Archive -LiteralPath $packageZipPath -DestinationPath $stagingRoot -Force

    $manifestPath = Join-Path $stagingRoot "app\release-artifact-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Extracted package is missing app\release-artifact-manifest.json: $packageZipPath"
    }

    $installScriptPath = Join-Path $stagingRoot "installer\Install-Lucid.ps1"
    if (-not (Test-Path -LiteralPath $installScriptPath -PathType Leaf)) {
        throw "Extracted package is missing installer\Install-Lucid.ps1: $packageZipPath"
    }

    $packageManifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $packageVersion = [string]$packageManifest.version
    $packageChannel = [string]$packageManifest.channel
    if ([string]::IsNullOrWhiteSpace($packageVersion)) {
        throw "Package manifest is missing version."
    }
    if ([string]::IsNullOrWhiteSpace($packageChannel)) {
        throw "Package manifest is missing channel."
    }
    if ($packageVersion -ne $version) {
        throw "Package version '$packageVersion' does not match release metadata version '$version'."
    }
    if ($packageChannel -ne $channel) {
        throw "Package channel '$packageChannel' does not match release metadata channel '$channel'."
    }

    $outputBaseName = "Lucid-Setup-$version-$channel"
    $setupExePath = Join-Path $outputDirectory "$outputBaseName.exe"

    Write-Info "Compiling setup exe: $outputBaseName.exe (Inno Setup: $IsccPath)"
    & $IsccPath `
        "/DAppVersion=$version" `
        "/DChannel=$channel" `
        "/DPayloadDir=$stagingRoot" `
        "/DOutputDir=$outputDirectory" `
        "/DOutputBaseName=$outputBaseName" `
        "/Qp" `
        $setupScript
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC.exe failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $setupExePath -PathType Leaf)) {
        throw "Setup exe missing after compile: $setupExePath"
    }

    $setupHash = (Get-FileHash -LiteralPath $setupExePath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath "$setupExePath.sha256" -Value "$setupHash *$outputBaseName.exe" -Encoding ascii

    $setupSizeMb = [math]::Round((Get-Item -LiteralPath $setupExePath).Length / 1MB, 1)
    Write-Host "Setup exe created: $setupExePath ($setupSizeMb MB)"
    Write-Host "SHA-256: $setupHash"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
