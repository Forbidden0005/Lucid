param(
    [string]$LucidDataRoot = "$env:LOCALAPPDATA\Lucid",
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\Lucid",
    [string]$CrashDumpRoot = "$env:LOCALAPPDATA\CrashDumps",
    [string]$WerReportRoot = "$env:LOCALAPPDATA\Microsoft\Windows\WER\ReportArchive",
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

function Copy-IfExists(
    [string]$SourcePath,
    [string]$DestinationPath
) {
    if (Test-Path -LiteralPath $SourcePath -PathType Leaf) {
        $destinationFolder = Split-Path -Parent $DestinationPath
        New-Item -ItemType Directory -Path $destinationFolder -Force | Out-Null
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
        return $true
    }

    return $false
}

function Add-ManifestEntry(
    [System.Collections.Generic.List[object]]$Entries,
    [string]$Path,
    [bool]$Present
) {
    $Entries.Add([ordered]@{
        path = $Path
        present = $Present
    })
}

$lucidDataRoot = [System.IO.Path]::GetFullPath($LucidDataRoot)
$installRoot   = [System.IO.Path]::GetFullPath($InstallRoot)
$crashDumpRoot = [System.IO.Path]::GetFullPath($CrashDumpRoot)
$werReportRoot = [System.IO.Path]::GetFullPath($WerReportRoot)

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $lucidDataRoot "Support"
}

$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$bundleRoot = Join-Path $outputDirectory ("lucid-support-" + $timestamp)
$zipPath = $bundleRoot + ".zip"

if (Test-Path -LiteralPath $bundleRoot) {
    Remove-Item -LiteralPath $bundleRoot -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null

$entries = New-Object System.Collections.Generic.List[object]

$settingsPath = Join-Path $lucidDataRoot "settings.json"
$integrityPath = Join-Path $lucidDataRoot "settings.integrity"
$migrationStatePath = Join-Path $lucidDataRoot "Migrations\migration-state.json"
$currentInstallPath = Join-Path $installRoot "current.json"

Add-ManifestEntry -Entries $entries -Path "settings.json" -Present (Copy-IfExists -SourcePath $settingsPath -DestinationPath (Join-Path $bundleRoot "settings.json"))
Add-ManifestEntry -Entries $entries -Path "settings.integrity" -Present (Copy-IfExists -SourcePath $integrityPath -DestinationPath (Join-Path $bundleRoot "settings.integrity"))
Add-ManifestEntry -Entries $entries -Path "migrations/migration-state.json" -Present (Copy-IfExists -SourcePath $migrationStatePath -DestinationPath (Join-Path $bundleRoot "migrations\migration-state.json"))
Add-ManifestEntry -Entries $entries -Path "install/current.json" -Present (Copy-IfExists -SourcePath $currentInstallPath -DestinationPath (Join-Path $bundleRoot "install\current.json"))

$logsPath = Join-Path $lucidDataRoot "logs"
if (Test-Path -LiteralPath $logsPath -PathType Container) {
    $recentLogs = Get-ChildItem -LiteralPath $logsPath -Filter *.log -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 5

    foreach ($logFile in $recentLogs) {
        $destination = Join-Path $bundleRoot ("logs\" + $logFile.Name)
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $logFile.FullName -Destination $destination -Force
        Add-ManifestEntry -Entries $entries -Path ("logs/" + $logFile.Name) -Present $true
    }
}

$dataPath = Join-Path $lucidDataRoot "Data"
if (Test-Path -LiteralPath $dataPath -PathType Container) {
    foreach ($candidate in @("explainmypc.db", "inference-history.json")) {
        $source = Join-Path $dataPath $candidate
        $present = Copy-IfExists -SourcePath $source -DestinationPath (Join-Path $bundleRoot ("data\" + $candidate))
        Add-ManifestEntry -Entries $entries -Path ("data/" + $candidate) -Present $present
    }
}

$diagnosticsManifest = [ordered]@{
    product = "Lucid"
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    lucidDataRoot = $lucidDataRoot
    installRoot = $installRoot
    crashDumpRoot = $crashDumpRoot
    werReportRoot = $werReportRoot
    entries = $entries
}

$crashDumpEntries = @()
if (Test-Path -LiteralPath $crashDumpRoot -PathType Container) {
    $recentCrashDumps = Get-ChildItem -LiteralPath $crashDumpRoot -Filter "Lucid.App*.dmp" -File |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 3

    foreach ($dumpFile in $recentCrashDumps) {
        $destination = Join-Path $bundleRoot ("crash-dumps\" + $dumpFile.Name)
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $dumpFile.FullName -Destination $destination -Force
        Add-ManifestEntry -Entries $entries -Path ("crash-dumps/" + $dumpFile.Name) -Present $true
        $crashDumpEntries += $dumpFile.Name
    }
}

$werEntries = @()
if (Test-Path -LiteralPath $werReportRoot -PathType Container) {
    $recentWerReports = Get-ChildItem -LiteralPath $werReportRoot -Directory |
        Where-Object { $_.Name -like "Lucid.App*" } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 3

    foreach ($werDir in $recentWerReports) {
        $destination = Join-Path $bundleRoot ("wer\" + $werDir.Name)
        Copy-Item -LiteralPath $werDir.FullName -Destination $destination -Recurse -Force
        Add-ManifestEntry -Entries $entries -Path ("wer/" + $werDir.Name) -Present $true
        $werEntries += $werDir.Name
    }
}

$supportPolicy = [ordered]@{
    product = "Lucid"
    generatedUtc = [DateTime]::UtcNow.ToString("o")
    retention = [ordered]@{
        logs = "local-rotation"
        crashDumps = "windows-managed"
        supportBundles = "manual-export"
    }
    includedCategories = @(
        "settings",
        "integrity",
        "migrations",
        "install-state",
        "logs",
        "data",
        "crash-dumps",
        "wer"
    )
    crashArtifacts = [ordered]@{
        crashDumps = $crashDumpEntries
        werReports = $werEntries
    }
}

$diagnosticsManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bundleRoot "support-bundle-manifest.json") -Encoding utf8
$supportPolicy | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $bundleRoot "support-policy.json") -Encoding utf8

Compress-Archive -Path (Join-Path $bundleRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $bundleRoot -Recurse -Force

Write-Info "Exported Lucid support bundle"
Write-Info "Bundle: $zipPath"
