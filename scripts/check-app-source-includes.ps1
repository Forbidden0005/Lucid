$ErrorActionPreference = "Stop"

function Normalize-RelativePath([string]$path) {
    $normalizedPath = $path.Replace("/", "\")
    while ($normalizedPath.StartsWith(".\")) {
        $normalizedPath = $normalizedPath.Substring(2)
    }

    while ($normalizedPath.StartsWith("\")) {
        $normalizedPath = $normalizedPath.Substring(1)
    }

    return $normalizedPath
}

function Get-RelativePath([string]$basePath, [string]$fullPath) {
    $resolvedBasePath = (Resolve-Path $basePath).Path
    $resolvedFullPath = (Resolve-Path $fullPath).Path

    if (-not $resolvedFullPath.StartsWith($resolvedBasePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is not under base path: $resolvedFullPath"
    }

    $relativePath = $resolvedFullPath.Substring($resolvedBasePath.Length)
    return Normalize-RelativePath $relativePath
}

function Write-Section([string]$message) {
    Write-Host "`n>>> $message" -ForegroundColor Cyan
}

function Write-Detail([string]$message) {
    Write-Host "    $message" -ForegroundColor Gray
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$appDir = Join-Path $repoRoot "lucid-desktop\Lucid.App"
$projectPath = Join-Path $appDir "Lucid.App.csproj"

if (-not (Test-Path $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content $projectPath

$compileIncludes = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($node in $projectXml.Project.ItemGroup.Compile) {
    if ($node.Include) {
        [void]$compileIncludes.Add((Normalize-RelativePath $node.Include))
    }
}

$managedFolders = @(
    "ViewModels",
    "Services",
    "Core"
)

$intentionalExclusions = [ordered]@{
    "ViewModels\ShellViewModel.cs" = "Legacy shell/navigation prototype that depends on non-live Core namespaces."
    "ViewModels\SystemIssueViewModel.cs" = "Legacy presentation wrapper tied to excluded model flow and retained for reference."
    "Services\MockTelemetryService.cs" = "Intentional mock retained as reference while runtime uses WindowsTelemetryService."
}

$unexpectedMissing = [System.Collections.Generic.List[string]]::new()
$summary = [System.Collections.Generic.List[pscustomobject]]::new()

Write-Section "Check Lucid.App source-file inclusion policy"
Write-Detail "Project: $projectPath"

foreach ($folder in $managedFolders) {
    $folderPath = Join-Path $appDir $folder
    if (-not (Test-Path $folderPath)) {
        throw "Managed folder not found: $folderPath"
    }

    $allFiles = @(Get-ChildItem -Path $folderPath -Recurse -Filter *.cs | Sort-Object FullName)
    $includedCount = 0
    $excludedCount = 0

    foreach ($file in $allFiles) {
        $relativePath = Get-RelativePath $appDir $file.FullName
        if ($compileIncludes.Contains($relativePath)) {
            $includedCount++
            continue
        }

        if ($intentionalExclusions.Contains($relativePath)) {
            $excludedCount++
            continue
        }

        $unexpectedMissing.Add($relativePath)
    }

    $summary.Add([pscustomobject]@{
        Folder = $folder
        Total = $allFiles.Count
        Included = $includedCount
        IntentionalExclusions = $excludedCount
    })
}

foreach ($row in $summary) {
    Write-Detail ("{0}: total={1}, included={2}, intentional exclusions={3}" -f $row.Folder, $row.Total, $row.Included, $row.IntentionalExclusions)
}

if ($intentionalExclusions.Count -gt 0) {
    Write-Section "Intentional exclusions"
    foreach ($entry in $intentionalExclusions.GetEnumerator()) {
        Write-Detail ("{0} - {1}" -f $entry.Key, $entry.Value)
    }
}

if ($unexpectedMissing.Count -gt 0) {
    Write-Section "Unexpected non-compiled source files"
    foreach ($path in $unexpectedMissing) {
        Write-Host "    $path" -ForegroundColor Red
    }

    throw "Found source files under managed folders that are not compiled or explicitly documented as intentional exclusions."
}

Write-Section "Source-file inclusion check passed"
Write-Detail "All managed C# files are compiled or intentionally documented."
