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

# The project uses default SDK compile globbing: every .cs under the project is
# compiled UNLESS it is explicitly removed via <Compile Remove>. (This replaced a
# 483-entry <Compile Include> allow-list that could silently drop new files.)
# Collect the <Compile Remove> entries — the only way a managed source file can be
# left out of the build — and verify each managed-folder file is either compiled or
# a documented intentional exclusion.
$compileRemoves = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($node in $projectXml.Project.ItemGroup.Compile) {
    if ($node.Remove) {
        [void]$compileRemoves.Add((Normalize-RelativePath $node.Remove))
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
Write-Detail "Model: default-glob compile (a file is compiled unless explicitly <Compile Remove>'d)."

foreach ($folder in $managedFolders) {
    $folderPath = Join-Path $appDir $folder
    if (-not (Test-Path $folderPath)) {
        throw "Managed folder not found: $folderPath"
    }

    $allFiles = @(Get-ChildItem -Path $folderPath -Recurse -Filter *.cs | Sort-Object FullName)
    $compiledCount = 0
    $excludedCount = 0

    foreach ($file in $allFiles) {
        $relativePath = Get-RelativePath $appDir $file.FullName

        # Under default globbing the file is compiled unless explicitly removed.
        if (-not $compileRemoves.Contains($relativePath)) {
            $compiledCount++
            continue
        }

        # The file is removed from the build; it must be a documented exclusion.
        if ($intentionalExclusions.Contains($relativePath)) {
            $excludedCount++
            continue
        }

        $unexpectedMissing.Add($relativePath)
    }

    $summary.Add([pscustomobject]@{
        Folder = $folder
        Total = $allFiles.Count
        Compiled = $compiledCount
        IntentionalExclusions = $excludedCount
    })
}

foreach ($row in $summary) {
    Write-Detail ("{0}: total={1}, compiled={2}, intentional exclusions={3}" -f $row.Folder, $row.Total, $row.Compiled, $row.IntentionalExclusions)
}

if ($intentionalExclusions.Count -gt 0) {
    Write-Section "Intentional exclusions"
    foreach ($entry in $intentionalExclusions.GetEnumerator()) {
        Write-Detail ("{0} - {1}" -f $entry.Key, $entry.Value)
    }
}

# Reverse check: every documented exclusion must actually be <Compile Remove>'d,
# otherwise the documentation has drifted from the project file (the file would now
# silently compile despite being documented as excluded).
$documentationDrift = [System.Collections.Generic.List[string]]::new()
foreach ($key in $intentionalExclusions.Keys) {
    if (-not $compileRemoves.Contains((Normalize-RelativePath $key))) {
        $documentationDrift.Add($key)
    }
}

if ($unexpectedMissing.Count -gt 0) {
    Write-Section "Undocumented non-compiled source files"
    foreach ($path in $unexpectedMissing) {
        Write-Host "    $path" -ForegroundColor Red
    }

    throw "Found managed source files removed from the build (<Compile Remove>) that are not documented as intentional exclusions."
}

if ($documentationDrift.Count -gt 0) {
    Write-Section "Documented exclusions that are no longer excluded"
    foreach ($path in $documentationDrift) {
        Write-Host "    $path" -ForegroundColor Red
    }

    throw "Intentional-exclusion documentation has drifted: the above files are documented as excluded but are not <Compile Remove>'d (they now compile)."
}

Write-Section "Source-file inclusion check passed"
Write-Detail "All managed C# files are compiled or documented as intentional <Compile Remove> exclusions."
