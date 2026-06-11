param(
    [string]$LucidDataRoot = "$env:LOCALAPPDATA\Lucid",
    [string]$InstallRoot = "$env:LOCALAPPDATA\Programs\Lucid",
    [string]$TargetVersion
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$path) {
    return [System.IO.Path]::GetFullPath($path)
}

function Ensure-Directory([string]$path) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

function Copy-BackupIfExists(
    [string]$SourcePath,
    [string]$BackupRoot,
    [string]$RelativeDestination
) {
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        return $false
    }

    $backupPath = Join-Path $BackupRoot $RelativeDestination
    $backupDir = Split-Path -Parent $backupPath
    Ensure-Directory $backupDir
    Copy-Item -LiteralPath $SourcePath -Destination $backupPath -Force
    return $true
}

function Move-LegacyFileIfNeeded(
    [string]$SourcePath,
    [string]$DestinationPath,
    [string]$BackupRoot,
    [string]$RelativeBackupPath,
    [System.Collections.Generic.List[object]]$Steps
) {
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        $Steps.Add([ordered]@{
            action = "move"
            source = $SourcePath
            destination = $DestinationPath
            result = "not-present"
        })
        return
    }

    if (Test-Path -LiteralPath $DestinationPath -PathType Leaf) {
        $Steps.Add([ordered]@{
            action = "move"
            source = $SourcePath
            destination = $DestinationPath
            result = "destination-exists"
        })
        return
    }

    Copy-BackupIfExists -SourcePath $SourcePath -BackupRoot $BackupRoot -RelativeDestination $RelativeBackupPath | Out-Null

    $destinationDir = Split-Path -Parent $DestinationPath
    Ensure-Directory $destinationDir
    Move-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force

    $Steps.Add([ordered]@{
        action = "move"
        source = $SourcePath
        destination = $DestinationPath
        result = "moved"
    })
}

if ([string]::IsNullOrWhiteSpace($TargetVersion)) {
    throw "TargetVersion is required."
}

$lucidDataRoot = Resolve-FullPath $LucidDataRoot
$installRoot = Resolve-FullPath $InstallRoot

$dataDir = Join-Path $lucidDataRoot "Data"
$historyDir = Join-Path $lucidDataRoot "History"
$logsDir = Join-Path $lucidDataRoot "logs"
$supportDir = Join-Path $lucidDataRoot "Support"
$startupBackupDir = Join-Path $lucidDataRoot "StartupBackups"
$migrationsDir = Join-Path $lucidDataRoot "Migrations"
$migrationBackupsDir = Join-Path $migrationsDir "Backups"
$migrationStatePath = Join-Path $migrationsDir "migration-state.json"

foreach ($directory in @(
    $lucidDataRoot,
    $dataDir,
    $historyDir,
    $logsDir,
    $supportDir,
    $startupBackupDir,
    $migrationsDir,
    $migrationBackupsDir
)) {
    Ensure-Directory $directory
}

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$backupRoot = Join-Path $migrationBackupsDir ($timestamp + "-" + $TargetVersion)
Ensure-Directory $backupRoot

$steps = New-Object 'System.Collections.Generic.List[object]'

foreach ($pathInfo in @(
    @{ Source = (Join-Path $lucidDataRoot "explainmypc.db"); Destination = (Join-Path $dataDir "explainmypc.db"); Backup = "legacy-root\explainmypc.db" },
    @{ Source = (Join-Path $lucidDataRoot "explainmypc.db-wal"); Destination = (Join-Path $dataDir "explainmypc.db-wal"); Backup = "legacy-root\explainmypc.db-wal" },
    @{ Source = (Join-Path $lucidDataRoot "explainmypc.db-shm"); Destination = (Join-Path $dataDir "explainmypc.db-shm"); Backup = "legacy-root\explainmypc.db-shm" },
    @{ Source = (Join-Path $lucidDataRoot "inference-history.json"); Destination = (Join-Path $dataDir "inference-history.json"); Backup = "legacy-root\inference-history.json" },
    @{ Source = (Join-Path $lucidDataRoot "operation-history.json"); Destination = (Join-Path $historyDir "operation-history.json"); Backup = "legacy-root\operation-history.json" }
)) {
    Move-LegacyFileIfNeeded -SourcePath $pathInfo.Source -DestinationPath $pathInfo.Destination -BackupRoot $backupRoot -RelativeBackupPath $pathInfo.Backup -Steps $steps
}

$currentState = $null
if (Test-Path -LiteralPath $migrationStatePath -PathType Leaf) {
    try {
        $currentState = Get-Content -Raw -LiteralPath $migrationStatePath | ConvertFrom-Json
    }
    catch {
        $currentState = $null
    }
}

$history = New-Object System.Collections.Generic.List[object]
if ($currentState -and $currentState.history) {
    foreach ($entry in $currentState.history) {
        $history.Add($entry)
    }
}

$runRecord = [ordered]@{
    migratedUtc = [DateTime]::UtcNow.ToString("o")
    targetVersion = $TargetVersion
    installRoot = $installRoot
    backupRoot = $backupRoot
    steps = $steps
}

$history.Add($runRecord)
while ($history.Count -gt 10) {
    $history.RemoveAt(0)
}

$state = [ordered]@{
    product = "Lucid"
    dataSchemaVersion = 1
    lastMigratedVersion = $TargetVersion
    lastMigratedUtc = $runRecord.migratedUtc
    installRoot = $installRoot
    lucidDataRoot = $lucidDataRoot
    canonicalDirectories = @(
        "Data",
        "History",
        "logs",
        "Support",
        "StartupBackups",
        "Migrations"
    )
    history = $history
}

$state | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $migrationStatePath -Encoding utf8

Write-Host "Lucid data migration complete for version $TargetVersion"
Write-Host "Migration state: $migrationStatePath"
