# Lucid - Code Debt Ratchet (ROADMAP C4 / C7 enforcement)
#
# Freezes known code-quality debt at its measured baseline so it can only
# shrink, never grow. Enforced metrics:
#
#   - appServicesConsumerFiles : files reading the AppServices static locator
#                                (C4 — freeze; list-based, so a NEW consumer
#                                file fails even if another file was cleaned)
#   - debugPrintCalls          : Debug.WriteLine / Console.WriteLine call sites (C7)
#   - emptyCatchBlocks         : `catch { }` with an empty body (C7)
#   - asyncVoidMethods         : `async void` occurrences (Phase 4 backlog)
#   - syncOverAsync            : `.Result` / `.Wait()` occurrences (Phase 4 backlog)
#
# All metrics are LEXICAL (regex over source text). They are deliberately the
# same measurement every run, on every machine — direction matters more than
# semantic purity. If a genuine false positive is introduced (e.g. a `.Result`
# property that is not a Task), prefer renaming; only raise the baseline with
# -UpdateBaseline and a written justification in the commit message.
#
# Usage:
#   scripts/check-debt-ratchet.ps1                  # verify against baseline (CI + verify-dev)
#   scripts/check-debt-ratchet.ps1 -UpdateBaseline  # rewrite baseline from current tree
#                                                   # (use after PAYING DOWN debt)

param(
    [switch]$UpdateBaseline
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$scanRoots = @(
    (Join-Path $repoRoot "lucid-desktop\Lucid.App"),
    (Join-Path $repoRoot "lucid-desktop\Lucid.Core")
)
$baselinePath = Join-Path $PSScriptRoot "debt-ratchet-baseline.json"

foreach ($root in $scanRoots) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Scan root not found: $root"
    }
}

$sourceFiles = Get-ChildItem -Path $scanRoots -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }

$appServicesDefinition = "lucid-desktop/Lucid.App/AppServices.cs"
$appServicesConsumers = New-Object System.Collections.Generic.List[string]
$debugPrintCalls = 0
$emptyCatchBlocks = 0
$asyncVoidMethods = 0
$syncOverAsync = 0

foreach ($file in $sourceFiles) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $relative = $file.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')

    if ($relative -ne $appServicesDefinition -and $text -match '\bAppServices\s*\.') {
        $appServicesConsumers.Add($relative)
    }

    $debugPrintCalls += [regex]::Matches($text, '(?<![\w.])(?:System\.Diagnostics\.)?(?:Debug|Console)\.WriteLine\s*\(').Count
    $emptyCatchBlocks += [regex]::Matches($text, 'catch\s*(\([^)]*\))?\s*\{\s*\}').Count
    $asyncVoidMethods += [regex]::Matches($text, '\basync\s+void\b').Count
    $syncOverAsync += [regex]::Matches($text, '\.Result\b|\.Wait\(\s*\)').Count
}

$sortedConsumers = @($appServicesConsumers | Sort-Object)

$current = [ordered]@{
    debugPrintCalls  = $debugPrintCalls
    emptyCatchBlocks = $emptyCatchBlocks
    asyncVoidMethods = $asyncVoidMethods
    syncOverAsync    = $syncOverAsync
}

if ($UpdateBaseline) {
    $baseline = [ordered]@{
        comment                  = "Debt ratchet baseline. Counts may only go DOWN. See scripts/check-debt-ratchet.ps1."
        metrics                  = $current
        appServicesConsumerFiles = $sortedConsumers
    }
    $json = $baseline | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($baselinePath, $json + "`n")
    Write-Host "Baseline written: $baselinePath"
    Write-Host ("  AppServices consumer files: {0}" -f $sortedConsumers.Count)
    foreach ($name in $current.Keys) {
        Write-Host ("  {0}: {1}" -f $name, $current[$name])
    }
    exit 0
}

if (-not (Test-Path -LiteralPath $baselinePath -PathType Leaf)) {
    throw "Baseline not found: $baselinePath. Run with -UpdateBaseline to create it."
}

$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json
$failures = New-Object System.Collections.Generic.List[string]
$improvements = New-Object System.Collections.Generic.List[string]

# Scalar metrics: current must not exceed baseline.
foreach ($name in $current.Keys) {
    $baselineValue = $baseline.metrics.$name
    $currentValue = $current[$name]
    if ($null -eq $baselineValue) {
        $failures.Add("Metric '$name' missing from baseline; regenerate with -UpdateBaseline.")
    }
    elseif ($currentValue -gt $baselineValue) {
        $failures.Add("Metric '$name' increased: baseline $baselineValue, current $currentValue. New debt of this kind is frozen (ROADMAP C4/C7) - fix the new occurrence instead.")
    }
    elseif ($currentValue -lt $baselineValue) {
        $improvements.Add("Metric '$name' improved: baseline $baselineValue, current $currentValue. Tighten the ratchet: run scripts/check-debt-ratchet.ps1 -UpdateBaseline and commit the new baseline.")
    }
}

# AppServices consumers: list-based freeze. Any file not grandfathered fails,
# even if some other file was cleaned up in the same change.
$grandfathered = @{}
foreach ($entry in @($baseline.appServicesConsumerFiles)) { $grandfathered[$entry] = $true }

foreach ($consumer in $sortedConsumers) {
    if (-not $grandfathered.ContainsKey($consumer)) {
        $failures.Add("NEW AppServices consumer: $consumer. The static locator is frozen (ROADMAP C4) - use constructor injection instead of referencing AppServices from new files.")
    }
}

$currentSet = @{}
foreach ($entry in $sortedConsumers) { $currentSet[$entry] = $true }
foreach ($entry in @($baseline.appServicesConsumerFiles)) {
    if (-not $currentSet.ContainsKey($entry)) {
        $improvements.Add("AppServices consumer cleaned or removed: $entry. Tighten the ratchet: run scripts/check-debt-ratchet.ps1 -UpdateBaseline and commit the new baseline.")
    }
}

Write-Host ("Debt ratchet: {0} source files scanned." -f $sourceFiles.Count)
Write-Host ("  AppServices consumer files: {0} (baseline {1})" -f $sortedConsumers.Count, @($baseline.appServicesConsumerFiles).Count)
foreach ($name in $current.Keys) {
    Write-Host ("  {0}: {1} (baseline {2})" -f $name, $current[$name], $baseline.metrics.$name)
}

if ($improvements.Count -gt 0) {
    Write-Host ""
    Write-Host "Improvements detected (not failures):" -ForegroundColor Green
    foreach ($message in $improvements) { Write-Host "  $message" -ForegroundColor Green }
}

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Debt ratchet FAILED:" -ForegroundColor Red
    foreach ($message in $failures) { Write-Host "  $message" -ForegroundColor Red }
    exit 1
}

Write-Host ""
Write-Host "Debt ratchet passed." -ForegroundColor Green
exit 0
