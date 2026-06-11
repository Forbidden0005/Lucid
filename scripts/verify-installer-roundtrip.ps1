param(
    [string]$ReleaseMetadataPath,
    [string]$PackagesDirectory,
    [string]$WorkingRoot
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

function Set-PackageVersion([string]$packageRoot, [string]$version) {
    $manifestPath = Join-Path $packageRoot "app\release-artifact-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Package manifest not found for version rewrite: $manifestPath"
    }

    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $manifest.version = $version
    $manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
}

function Invoke-PowerShellWithCapture([string[]]$arguments) {
    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()

    try {
        $process = Start-Process -FilePath powershell.exe -ArgumentList $arguments -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = (Get-Content -Raw -LiteralPath $stdoutPath)
            StdErr = (Get-Content -Raw -LiteralPath $stderrPath)
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ReleaseMetadataPath)) {
    $ReleaseMetadataPath = Join-Path $repoRoot "release\release-metadata.json"
}

if ([string]::IsNullOrWhiteSpace($PackagesDirectory)) {
    $PackagesDirectory = Join-Path $repoRoot "release\packages"
}

if ([string]::IsNullOrWhiteSpace($WorkingRoot)) {
    $WorkingRoot = Join-Path $repoRoot "release\installer-test"
}

$releaseMetadataPath = [System.IO.Path]::GetFullPath($ReleaseMetadataPath)
$packagesDirectory   = [System.IO.Path]::GetFullPath($PackagesDirectory)
$workingRoot         = [System.IO.Path]::GetFullPath($WorkingRoot)

if (-not (Test-Path -LiteralPath $releaseMetadataPath -PathType Leaf)) {
    throw "Release metadata not found: $releaseMetadataPath"
}

$metadata = Get-Content -Raw -LiteralPath $releaseMetadataPath | ConvertFrom-Json
$productSlug = ([string]$metadata.product).ToLowerInvariant().Replace(' ', '-')
$archiveBaseName = "$productSlug-$([string]$metadata.version)-$([string]$metadata.channel)-$([string]$metadata.runtimeIdentifier)-$([string]$metadata.packaging)"
$zipPath = Join-Path $packagesDirectory ($archiveBaseName + ".zip")

if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Release package not found: $zipPath"
}

if (Test-Path -LiteralPath $workingRoot) {
    Remove-Item -LiteralPath $workingRoot -Recurse -Force
}

$extractRoot = Join-Path $workingRoot "package"
$installRoot = Join-Path $workingRoot "installed"
$lucidDataRoot = Join-Path $workingRoot "Lucid"
$upgradeRoot = Join-Path $workingRoot "upgrade-package"
$downgradeRoot = Join-Path $workingRoot "downgrade-package"

New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force

New-Item -ItemType Directory -Path $lucidDataRoot -Force | Out-Null
Set-Content -LiteralPath (Join-Path $lucidDataRoot "operation-history.json") -Value '[]' -Encoding utf8
Set-Content -LiteralPath (Join-Path $lucidDataRoot "inference-history.json") -Value '[]' -Encoding utf8
Set-Content -LiteralPath (Join-Path $lucidDataRoot "explainmypc.db") -Value 'legacy-db' -Encoding utf8

$installScript = Join-Path $extractRoot "installer\Install-Lucid.ps1"
$uninstallScript = Join-Path $extractRoot "installer\Uninstall-Lucid.ps1"

if (-not (Test-Path -LiteralPath $installScript -PathType Leaf)) {
    throw "Install script not found in extracted package: $installScript"
}

if (-not (Test-Path -LiteralPath $uninstallScript -PathType Leaf)) {
    throw "Uninstall script not found in extracted package: $uninstallScript"
}

& powershell.exe -ExecutionPolicy Bypass -File $installScript -PackageRoot $extractRoot -InstallRoot $installRoot -LucidDataRoot $lucidDataRoot -NoStartMenuShortcut -NoDesktopShortcut
if ($LASTEXITCODE -ne 0) {
    throw "Installer script failed with exit code $LASTEXITCODE."
}

$versionRoot = Join-Path $installRoot ([string]$metadata.version)
$installedExe = Join-Path $versionRoot "app\Lucid.App.exe"
$currentStatePath = Join-Path $installRoot "current.json"
$migrationStatePath = Join-Path $lucidDataRoot "Migrations\migration-state.json"

if (-not (Test-Path -LiteralPath $installedExe -PathType Leaf)) {
    throw "Installed executable missing after installer run: $installedExe"
}

if (-not (Test-Path -LiteralPath $currentStatePath -PathType Leaf)) {
    throw "current.json missing after installer run: $currentStatePath"
}

foreach ($requiredPath in @(
    (Join-Path $lucidDataRoot "Data\explainmypc.db"),
    (Join-Path $lucidDataRoot "Data\inference-history.json"),
    (Join-Path $lucidDataRoot "History\operation-history.json"),
    $migrationStatePath
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Expected migrated Lucid data artifact missing after install: $requiredPath"
    }
}

foreach ($legacyPath in @(
    (Join-Path $lucidDataRoot "explainmypc.db"),
    (Join-Path $lucidDataRoot "inference-history.json"),
    (Join-Path $lucidDataRoot "operation-history.json")
)) {
    if (Test-Path -LiteralPath $legacyPath -PathType Leaf) {
        throw "Legacy Lucid data artifact still exists after migration: $legacyPath"
    }
}

$migrationState = Get-Content -Raw -LiteralPath $migrationStatePath | ConvertFrom-Json
if ([string]$migrationState.lastMigratedVersion -ne [string]$metadata.version) {
    throw "Migration state did not record the installed version $($metadata.version)."
}

Copy-Item -LiteralPath $extractRoot -Destination $upgradeRoot -Recurse -Force
Set-PackageVersion -packageRoot $upgradeRoot -version "0.1.1"

& powershell.exe -ExecutionPolicy Bypass -File $installScript -PackageRoot $upgradeRoot -InstallRoot $installRoot -LucidDataRoot $lucidDataRoot -NoStartMenuShortcut -NoDesktopShortcut
if ($LASTEXITCODE -ne 0) {
    throw "Upgrade installer script failed with exit code $LASTEXITCODE."
}

$upgradedVersionRoot = Join-Path $installRoot "0.1.1"
$upgradedExe = Join-Path $upgradedVersionRoot "app\Lucid.App.exe"
if (-not (Test-Path -LiteralPath $upgradedExe -PathType Leaf)) {
    throw "Upgraded executable missing after installer run: $upgradedExe"
}

$currentState = Get-Content -Raw -LiteralPath $currentStatePath | ConvertFrom-Json
if ([string]$currentState.version -ne "0.1.1") {
    throw "current.json did not advance to upgraded version 0.1.1."
}

$migrationState = Get-Content -Raw -LiteralPath $migrationStatePath | ConvertFrom-Json
if ([string]$migrationState.lastMigratedVersion -ne "0.1.1") {
    throw "Migration state did not advance to upgraded version 0.1.1."
}

if (-not (Test-Path -LiteralPath $versionRoot -PathType Container)) {
    throw "Previous version directory should remain available after upgrade: $versionRoot"
}

Copy-Item -LiteralPath $extractRoot -Destination $downgradeRoot -Recurse -Force
Set-PackageVersion -packageRoot $downgradeRoot -version "0.0.9"

$downgradeBlockResult = Invoke-PowerShellWithCapture -arguments @(
    "-ExecutionPolicy", "Bypass",
    "-File", $installScript,
    "-PackageRoot", $downgradeRoot,
    "-InstallRoot", $installRoot,
    "-LucidDataRoot", $lucidDataRoot,
    "-NoStartMenuShortcut",
    "-NoDesktopShortcut"
)
if ($downgradeBlockResult.ExitCode -eq 0) {
    throw "Downgrade install unexpectedly succeeded without -AllowDowngrade."
}

if (($downgradeBlockResult.StdErr + $downgradeBlockResult.StdOut) -notmatch "Refusing to install older Lucid version") {
    throw "Downgrade install failed, but not with the expected downgrade-protection message."
}

& powershell.exe -ExecutionPolicy Bypass -File $installScript -PackageRoot $downgradeRoot -InstallRoot $installRoot -LucidDataRoot $lucidDataRoot -NoStartMenuShortcut -NoDesktopShortcut -AllowDowngrade
if ($LASTEXITCODE -ne 0) {
    throw "Downgrade installer script failed with -AllowDowngrade. Exit code: $LASTEXITCODE."
}

$downgradedVersionRoot = Join-Path $installRoot "0.0.9"
if (-not (Test-Path -LiteralPath $downgradedVersionRoot -PathType Container)) {
    throw "Downgraded version directory missing after allowed downgrade: $downgradedVersionRoot"
}

$currentState = Get-Content -Raw -LiteralPath $currentStatePath | ConvertFrom-Json
if ([string]$currentState.version -ne "0.0.9") {
    throw "current.json did not move to downgraded version 0.0.9."
}

if (-not [bool]$currentState.isDowngrade) {
    throw "current.json should record downgrade intent for an allowed downgrade install."
}

$migrationState = Get-Content -Raw -LiteralPath $migrationStatePath | ConvertFrom-Json
if ([string]$migrationState.lastMigratedVersion -ne "0.0.9") {
    throw "Migration state did not record the downgraded version 0.0.9."
}

& powershell.exe -ExecutionPolicy Bypass -File $uninstallScript -InstallRoot $installRoot -Version ([string]$metadata.version)
if ($LASTEXITCODE -ne 0) {
    throw "Uninstall script failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $versionRoot) {
    throw "Installed version directory still exists after uninstall: $versionRoot"
}

$currentState = Get-Content -Raw -LiteralPath $currentStatePath | ConvertFrom-Json
if ([string]$currentState.version -ne "0.1.1") {
    throw "current.json did not roll forward to 0.1.1 after removing 0.1.0."
}

& powershell.exe -ExecutionPolicy Bypass -File $uninstallScript -InstallRoot $installRoot -RemoveAllVersions
if ($LASTEXITCODE -ne 0) {
    throw "RemoveAllVersions uninstall failed with exit code $LASTEXITCODE."
}

if (Test-Path -LiteralPath $currentStatePath) {
    throw "current.json still exists after removing all installed versions: $currentStatePath"
}

Write-Info "Verified installer round-trip"
Write-Info "Extract root: $extractRoot"
Write-Info "Install root: $installRoot"
