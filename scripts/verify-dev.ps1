# Lucid - Developer Verification
# Runs the standard local verification commands for the current repo state.

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$PublishApp
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) { Write-Host "`n>>> $msg" -ForegroundColor Cyan }
function Write-Info($msg) { Write-Host "    $msg" -ForegroundColor Gray }
function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("Command failed with exit code {0}: {1} {2}" -f $LASTEXITCODE, $FilePath, ($Arguments -join " "))
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopDir = Join-Path $repoRoot "lucid-desktop"
$nativeDir = Join-Path $repoRoot "lucid-native"
$solutionPath = Join-Path $desktopDir "Lucid.slnx"
$appProjectPath = Join-Path $desktopDir "Lucid.App\Lucid.App.csproj"
$testProjectPath = Join-Path $desktopDir "Lucid.Tests\Lucid.Tests.csproj"
$includeCheckPath = Join-Path $PSScriptRoot "check-app-source-includes.ps1"

Write-Step "Restore solution"
Push-Location $desktopDir
try {
    Invoke-CheckedCommand -FilePath dotnet -Arguments @("restore", $solutionPath, "-p:Platform=x64")

    Write-Step "Build solution"
    Invoke-CheckedCommand -FilePath dotnet -Arguments @("build", $solutionPath, "-c", $Configuration, "-p:Platform=x64", "--no-restore")

    Write-Step "Check Lucid.App source inclusion policy"
    Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $includeCheckPath)

    Write-Step "Run C# tests"
    Invoke-CheckedCommand -FilePath dotnet -Arguments @("test", $testProjectPath, "-c", $Configuration, "-p:Platform=x64", "--no-restore")

    if ($PublishApp) {
        Write-Step "Publish unpackaged WinUI app"
        Invoke-CheckedCommand -FilePath dotnet -Arguments @("publish", $appProjectPath, "-c", $Configuration, "-p:Platform=x64", "-r", "win-x64", "--self-contained", "true", "-p:WindowsPackageType=None", "--no-restore")
    }
}
finally {
    Pop-Location
}

Write-Step "Run Rust tests"
Push-Location $nativeDir
try {
    Invoke-CheckedCommand -FilePath cargo -Arguments @("test")
}
finally {
    Pop-Location
}

Write-Step "Verification complete"
if ($PublishApp) {
    Write-Info "Build, C# tests, release publish, and Rust tests all ran successfully."
}
else {
    Write-Info "Build, C# tests, and Rust tests all ran successfully."
}
