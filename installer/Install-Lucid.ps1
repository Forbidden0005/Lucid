param(
    [string]$PackageRoot,
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\Lucid",
    [string]$LucidDataRoot = "$env:LOCALAPPDATA\Lucid",
    [switch]$NoStartMenuShortcut,
    [switch]$NoDesktopShortcut,
    [switch]$Force,
    [switch]$AllowDowngrade,
    [switch]$RemoveOlderVersions
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$path) {
    return [System.IO.Path]::GetFullPath($path)
}

function Assert-WithinRoot([string]$rootPath, [string]$candidatePath) {
    $rootFull = Resolve-FullPath $rootPath
    $candidateFull = Resolve-FullPath $candidatePath
    $rootWithSeparator = $rootFull.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidateFull.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase) -and
        $candidateFull -ne $rootFull) {
        throw "Refusing to operate outside install root. Root='$rootFull' Candidate='$candidateFull'"
    }
}

function New-ShortcutFile(
    [string]$ShortcutPath,
    [string]$TargetPath,
    [string]$Arguments = "",
    [string]$WorkingDirectory = ""
) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    if ($Arguments) { $shortcut.Arguments = $Arguments }
    if ($WorkingDirectory) { $shortcut.WorkingDirectory = $WorkingDirectory }
    $shortcut.Save()
}

function Get-InstalledVersionDirectories([string]$rootPath) {
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
        return @()
    }

    return Get-ChildItem -LiteralPath $rootPath -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending
}

if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    $PackageRoot = Split-Path -Parent $PSScriptRoot
}

$packageRoot = Resolve-FullPath $PackageRoot
$installRoot = Resolve-FullPath $InstallRoot

$sourceAppRoot = Join-Path $packageRoot "app"
$sourceInstallerRoot = Join-Path $packageRoot "installer"
$sourceDocsRoot = Join-Path $packageRoot "docs"
$manifestPath = Join-Path $sourceAppRoot "release-artifact-manifest.json"

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Package manifest not found: $manifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$version = [string]$manifest.version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Package manifest is missing version."
}

$versionRoot = Join-Path $installRoot $version
$targetAppRoot = Join-Path $versionRoot "app"
$targetInstallerRoot = Join-Path $versionRoot "installer"
$targetDocsRoot = Join-Path $versionRoot "docs"
$currentStatePath = Join-Path $installRoot "current.json"
$migrationScript = Join-Path $targetInstallerRoot "Migrate-LucidData.ps1"

Assert-WithinRoot -rootPath $installRoot -candidatePath $versionRoot

$installedVersions = Get-InstalledVersionDirectories -rootPath $installRoot
$highestInstalledVersion = $null
if ($installedVersions.Count -gt 0) {
    $highestInstalledVersion = [version]$installedVersions[0].Name
}

if ($highestInstalledVersion -and -not $AllowDowngrade -and ([version]$version -lt $highestInstalledVersion)) {
    throw "Refusing to install older Lucid version $version while newer version $highestInstalledVersion is already installed. Re-run with -AllowDowngrade to proceed intentionally."
}

if (Test-Path -LiteralPath $versionRoot) {
    if (-not $Force) {
        throw "Lucid version $version is already installed at $versionRoot. Re-run with -Force to replace it."
    }

    Remove-Item -LiteralPath $versionRoot -Recurse -Force
}

if ($RemoveOlderVersions) {
    foreach ($existingVersionDir in $installedVersions) {
        if ($existingVersionDir.Name -ne $version) {
            Assert-WithinRoot -rootPath $installRoot -candidatePath $existingVersionDir.FullName
            Remove-Item -LiteralPath $existingVersionDir.FullName -Recurse -Force
        }
    }
}

New-Item -ItemType Directory -Path $targetAppRoot -Force | Out-Null
New-Item -ItemType Directory -Path $targetInstallerRoot -Force | Out-Null
if (Test-Path -LiteralPath $sourceDocsRoot -PathType Container) {
    New-Item -ItemType Directory -Path $targetDocsRoot -Force | Out-Null
}

Get-ChildItem -LiteralPath $sourceAppRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $targetAppRoot -Recurse -Force
}

Get-ChildItem -LiteralPath $sourceInstallerRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $targetInstallerRoot -Recurse -Force
}

if (Test-Path -LiteralPath $sourceDocsRoot -PathType Container) {
    Get-ChildItem -LiteralPath $sourceDocsRoot -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $targetDocsRoot -Recurse -Force
    }
}

$appExecutable = Join-Path $targetAppRoot "Lucid.App.exe"
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) {
    throw "Installed app executable missing: $appExecutable"
}

if (-not (Test-Path -LiteralPath $migrationScript -PathType Leaf)) {
    throw "Installed migration script missing: $migrationScript"
}

& powershell.exe -ExecutionPolicy Bypass -File $migrationScript -LucidDataRoot $LucidDataRoot -InstallRoot $installRoot -TargetVersion $version
if ($LASTEXITCODE -ne 0) {
    throw "Lucid data migration failed with exit code $LASTEXITCODE."
}

$startMenuFolder = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Lucid"
$desktopFolder = [Environment]::GetFolderPath("Desktop")
$startMenuShortcut = Join-Path $startMenuFolder "Lucid.lnk"
$uninstallShortcut = Join-Path $startMenuFolder "Uninstall Lucid.lnk"
$desktopShortcut = Join-Path $desktopFolder "Lucid.lnk"
$uninstallScript = Join-Path $targetInstallerRoot "Uninstall-Lucid.ps1"
$powershellPath = Join-Path $PSHOME "powershell.exe"

if (-not $NoStartMenuShortcut) {
    New-Item -ItemType Directory -Path $startMenuFolder -Force | Out-Null
    New-ShortcutFile -ShortcutPath $startMenuShortcut -TargetPath $appExecutable -WorkingDirectory $targetAppRoot
    New-ShortcutFile -ShortcutPath $uninstallShortcut -TargetPath $powershellPath -Arguments "-ExecutionPolicy Bypass -File `"$uninstallScript`" -InstallRoot `"$installRoot`" -Version `"$version`"" -WorkingDirectory $targetInstallerRoot
}

if (-not $NoDesktopShortcut) {
    New-ShortcutFile -ShortcutPath $desktopShortcut -TargetPath $appExecutable -WorkingDirectory $targetAppRoot
}

$currentState = [ordered]@{
    product = "Lucid"
    version = $version
    installedUtc = [DateTime]::UtcNow.ToString("o")
    appExecutable = $appExecutable
    installRoot = $versionRoot
    lucidDataRoot = (Resolve-FullPath $LucidDataRoot)
    migrationStatePath = (Join-Path (Resolve-FullPath $LucidDataRoot) "Migrations\migration-state.json")
    isDowngrade = [bool]($highestInstalledVersion -and ([version]$version -lt $highestInstalledVersion))
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
$currentState | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $currentStatePath -Encoding utf8

Write-Host "Installed Lucid $version to $versionRoot"
Write-Host "Executable: $appExecutable"
