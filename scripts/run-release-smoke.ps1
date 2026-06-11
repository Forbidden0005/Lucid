param(
    [string]$PublishDirectory,
    [int]$StartupTimeoutSeconds = 20,
    [int]$MinimumAliveSeconds = 5,
    [switch]$SkipIfNonInteractive = $true
)

$ErrorActionPreference = "Stop"

function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }

$repoRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $repoRoot "lucid-desktop\Lucid.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"
}

$publishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
    throw "Publish directory not found: $publishDirectory"
}

$appPath = Join-Path $publishDirectory "Lucid.App.exe"
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) {
    throw "Lucid.App.exe not found in publish directory: $publishDirectory"
}

$smokeResultPath = Join-Path $publishDirectory "release-smoke-result.json"

if ($SkipIfNonInteractive -and -not [Environment]::UserInteractive) {
    $skippedResult = [ordered]@{
        status      = "skipped"
        reason      = "non-interactive-environment"
        generatedUtc = [DateTime]::UtcNow.ToString("o")
        entryPoint  = "Lucid.App.exe"
    }

    $skippedResult | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $smokeResultPath -Encoding utf8
    Write-Info "Skipped release smoke run in a non-interactive environment."
    return
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $appPath
$startInfo.WorkingDirectory = $publishDirectory
$startInfo.UseShellExecute = $true

$process = $null
$startedAt = [DateTime]::UtcNow
$windowDetected = $false
$status = "failed"
$failureReason = $null

try {
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($process -eq $null) {
        throw "Failed to start Lucid.App.exe."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()

        if ($process.HasExited) {
            throw "Lucid.App.exe exited before the smoke threshold. ExitCode=$($process.ExitCode)"
        }

        if ($process.MainWindowHandle -ne 0) {
            $windowDetected = $true
            break
        }
    }

    if (-not $windowDetected) {
        $process.Refresh()
        if ($process.HasExited) {
            throw "Lucid.App.exe exited before a main window was detected. ExitCode=$($process.ExitCode)"
        }
    }

    Start-Sleep -Seconds $MinimumAliveSeconds
    $process.Refresh()
    if ($process.HasExited) {
        throw "Lucid.App.exe did not remain alive for the minimum smoke interval. ExitCode=$($process.ExitCode)"
    }

    $status = "passed"
}
catch {
    $failureReason = $_.Exception.Message
    throw
}
finally {
    if ($process -ne $null) {
        try {
            $process.Refresh()
            if (-not $process.HasExited) {
                if ($process.MainWindowHandle -ne 0) {
                    [void]$process.CloseMainWindow()
                    if (-not $process.WaitForExit(10000)) {
                        $process.Kill()
                        $process.WaitForExit()
                    }
                }
                else {
                    $process.Kill()
                    $process.WaitForExit()
                }
            }
        }
        catch {
        }
    }

    $finishedAt = [DateTime]::UtcNow
    $result = [ordered]@{
        status                = $status
        generatedUtc          = $finishedAt.ToString("o")
        entryPoint            = "Lucid.App.exe"
        startupTimeoutSeconds = $StartupTimeoutSeconds
        minimumAliveSeconds   = $MinimumAliveSeconds
        windowDetected        = $windowDetected
        startedUtc            = $startedAt.ToString("o")
        durationSeconds       = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        failureReason         = $failureReason
    }

    $result | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $smokeResultPath -Encoding utf8
}

Write-Info "Release smoke run passed"
Write-Info "Window detected: $windowDetected"
Write-Info "Result file: $smokeResultPath"
