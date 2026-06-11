param(
    [string]$WorkingRoot
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($WorkingRoot)) {
    $WorkingRoot = Join-Path $repoRoot "release\support-bundle-test"
}

$workingRoot = [System.IO.Path]::GetFullPath($WorkingRoot)
if (Test-Path -LiteralPath $workingRoot) {
    Remove-Item -LiteralPath $workingRoot -Recurse -Force
}

$lucidDataRoot = Join-Path $workingRoot "Lucid"
$installRoot = Join-Path $workingRoot "Programs\Lucid"
$crashDumpRoot = Join-Path $workingRoot "CrashDumps"
$werReportRoot = Join-Path $workingRoot "WER\ReportArchive"
$bundleOutputRoot = Join-Path $workingRoot "bundles"

New-Item -ItemType Directory -Path (Join-Path $lucidDataRoot "logs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $lucidDataRoot "Data") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $lucidDataRoot "Migrations") -Force | Out-Null
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
New-Item -ItemType Directory -Path $crashDumpRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $werReportRoot "Lucid.App_1234567890") -Force | Out-Null

Set-Content -LiteralPath (Join-Path $lucidDataRoot "settings.json") -Value '{ "SchemaVersion": 1 }' -Encoding utf8
Set-Content -LiteralPath (Join-Path $lucidDataRoot "settings.integrity") -Value 'integrity' -Encoding utf8
Set-Content -LiteralPath (Join-Path $lucidDataRoot "Migrations\migration-state.json") -Value '{ "lastMigratedVersion": "0.1.0" }' -Encoding utf8
Set-Content -LiteralPath (Join-Path $lucidDataRoot "logs\lucid-2026-06-08.log") -Value 'log-entry' -Encoding utf8
Set-Content -LiteralPath (Join-Path $lucidDataRoot "Data\inference-history.json") -Value '[]' -Encoding utf8
Set-Content -LiteralPath (Join-Path $installRoot "current.json") -Value '{ "version": "0.1.0" }' -Encoding utf8
Set-Content -LiteralPath (Join-Path $crashDumpRoot "Lucid.App.exe.1234.dmp") -Value 'dump-bytes' -Encoding utf8
Set-Content -LiteralPath (Join-Path $werReportRoot "Lucid.App_1234567890\Report.wer") -Value 'wer-data' -Encoding utf8

$scriptPath = Join-Path $repoRoot "installer\Export-LucidSupportBundle.ps1"
& powershell.exe -ExecutionPolicy Bypass -File $scriptPath -LucidDataRoot $lucidDataRoot -InstallRoot $installRoot -CrashDumpRoot $crashDumpRoot -WerReportRoot $werReportRoot -OutputDirectory $bundleOutputRoot
if ($LASTEXITCODE -ne 0) {
    throw "Support bundle export failed with exit code $LASTEXITCODE."
}

$bundleZip = Get-ChildItem -LiteralPath $bundleOutputRoot -Filter *.zip -File | Select-Object -First 1
if (-not $bundleZip) {
    throw "Support bundle zip was not created."
}

$extractRoot = Join-Path $workingRoot "bundle-extract"
Expand-Archive -LiteralPath $bundleZip.FullName -DestinationPath $extractRoot -Force

$requiredFiles = @(
    "settings.json",
    "settings.integrity",
    "migrations/migration-state.json",
    "install/current.json",
    "logs/lucid-2026-06-08.log",
    "data/inference-history.json",
    "crash-dumps/Lucid.App.exe.1234.dmp",
    "wer/Lucid.App_1234567890/Report.wer",
    "support-bundle-manifest.json",
    "support-policy.json"
)

foreach ($requiredFile in $requiredFiles) {
    $requiredPath = Join-Path $extractRoot $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Support bundle is missing '$requiredFile'."
    }
}

Write-Info "Verified support bundle export"
Write-Info "Bundle: $($bundleZip.FullName)"
