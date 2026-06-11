param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\Lucid",
    [string]$Version,
    [switch]$RemoveAllVersions
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

function Get-InstalledVersionDirectories([string]$rootPath) {
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
        return @()
    }

    return Get-ChildItem -LiteralPath $rootPath -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending
}

$installRoot = Resolve-FullPath $InstallRoot
$currentStatePath = Join-Path $installRoot "current.json"

if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) {
    Write-Host "Lucid is not installed at $installRoot"
    return
}

$versionsToRemove = @()
if ($RemoveAllVersions) {
    $versionsToRemove = Get-InstalledVersionDirectories -rootPath $installRoot
}
elseif (-not [string]::IsNullOrWhiteSpace($Version)) {
    $candidate = Join-Path $installRoot $Version
    if (Test-Path -LiteralPath $candidate -PathType Container) {
        $versionsToRemove = @(Get-Item -LiteralPath $candidate)
    }
}
elseif (Test-Path -LiteralPath $currentStatePath -PathType Leaf) {
    $currentState = Get-Content -Raw -LiteralPath $currentStatePath | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace([string]$currentState.version)) {
        $candidate = Join-Path $installRoot ([string]$currentState.version)
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            $versionsToRemove = @(Get-Item -LiteralPath $candidate)
        }
    }
}

if ($versionsToRemove.Count -eq 0) {
    $versionsToRemove = Get-InstalledVersionDirectories -rootPath $installRoot | Select-Object -First 1
}

foreach ($versionDir in $versionsToRemove) {
    Assert-WithinRoot -rootPath $installRoot -candidatePath $versionDir.FullName
    Remove-Item -LiteralPath $versionDir.FullName -Recurse -Force
}

$startMenuFolder = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Lucid"
$startMenuShortcut = Join-Path $startMenuFolder "Lucid.lnk"
$uninstallShortcut = Join-Path $startMenuFolder "Uninstall Lucid.lnk"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Lucid.lnk"

foreach ($path in @($startMenuShortcut, $uninstallShortcut, $desktopShortcut)) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}

if (Test-Path -LiteralPath $startMenuFolder -PathType Container) {
    $remainingStartMenuEntries = Get-ChildItem -LiteralPath $startMenuFolder -Force
    if ($remainingStartMenuEntries.Count -eq 0) {
        Remove-Item -LiteralPath $startMenuFolder -Force
    }
}

$remainingVersions = Get-InstalledVersionDirectories -rootPath $installRoot
if ($remainingVersions.Count -gt 0) {
    $nextVersion = $remainingVersions[0]
    $currentState = [ordered]@{
        product = "Lucid"
        version = $nextVersion.Name
        installedUtc = [DateTime]::UtcNow.ToString("o")
        appExecutable = (Join-Path $nextVersion.FullName "app\Lucid.App.exe")
    }
    $currentState | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $currentStatePath -Encoding utf8
}
elseif (Test-Path -LiteralPath $currentStatePath -PathType Leaf) {
    Remove-Item -LiteralPath $currentStatePath -Force
}

if ((Get-ChildItem -LiteralPath $installRoot -Force | Measure-Object).Count -eq 0) {
    Remove-Item -LiteralPath $installRoot -Force
}

Write-Host "Uninstalled Lucid from $installRoot"
