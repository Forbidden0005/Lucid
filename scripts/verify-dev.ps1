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
function Resolve-ToolPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName,

        [string[]]$FallbackPaths = @()
    )

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    foreach ($path in $FallbackPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path -PathType Leaf)) {
            return $path
        }
    }

    throw "Required tool not found: $CommandName"
}
function Resolve-VsDevCmdPath {
    $vswhereCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path $env:ProgramFiles "Microsoft Visual Studio\Installer\vswhere.exe")
    )

    foreach ($vswherePath in $vswhereCandidates) {
        if (Test-Path -LiteralPath $vswherePath -PathType Leaf) {
            $installPath = & $vswherePath -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            if (-not [string]::IsNullOrWhiteSpace($installPath)) {
                $candidate = Join-Path $installPath "Common7\Tools\VsDevCmd.bat"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    return $candidate
                }
            }
        }
    }

    $defaultCandidate = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\Common7\Tools\VsDevCmd.bat"
    if (Test-Path -LiteralPath $defaultCandidate -PathType Leaf) {
        return $defaultCandidate
    }

    throw "Visual Studio Developer Command Prompt not found. Install Visual Studio Build Tools with the C++ workload."
}
function Invoke-RustTests {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CargoPath
    )

    if (Get-Command "link.exe" -ErrorAction SilentlyContinue) {
        Invoke-CheckedCommand -FilePath $CargoPath -Arguments @("test")
        return
    }

    $vsDevCmdPath = Resolve-VsDevCmdPath
    $cmdLine = "`"$vsDevCmdPath`" -arch=x64 -host_arch=x64 && `"$CargoPath`" test"
    & cmd.exe /s /c $cmdLine
    if ($LASTEXITCODE -ne 0) {
        throw ("Command failed with exit code {0}: {1}" -f $LASTEXITCODE, $cmdLine)
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopDir = Join-Path $repoRoot "lucid-desktop"
$nativeDir = Join-Path $repoRoot "lucid-native"
$cargoPath = Resolve-ToolPath -CommandName "cargo" -FallbackPaths @((Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"))
$solutionPath = Join-Path $desktopDir "Lucid.slnx"
$appProjectPath = Join-Path $desktopDir "Lucid.App\Lucid.App.csproj"
$testProjectPath = Join-Path $desktopDir "Lucid.Tests\Lucid.Tests.csproj"
$includeCheckPath = Join-Path $PSScriptRoot "check-app-source-includes.ps1"
$releaseMetadataCheckPath = Join-Path $PSScriptRoot "validate-release-metadata.ps1"
$releaseOperationsCheckPath = Join-Path $PSScriptRoot "validate-release-operations.ps1"
$releaseArtifactSignPath = Join-Path $PSScriptRoot "sign-release-artifact.ps1"
$releaseArtifactPrepPath = Join-Path $PSScriptRoot "prepare-release-artifact.ps1"
$releaseSmokePath = Join-Path $PSScriptRoot "run-release-smoke.ps1"
$releaseArtifactVerifyPath = Join-Path $PSScriptRoot "verify-release-artifact.ps1"
$releasePackagePath = Join-Path $PSScriptRoot "package-release-artifact.ps1"
$releasePackageVerifyPath = Join-Path $PSScriptRoot "verify-release-package.ps1"
$releaseInstallerVerifyPath = Join-Path $PSScriptRoot "verify-installer-roundtrip.ps1"
$releaseUpdateManifestPath = Join-Path $PSScriptRoot "generate-release-update-manifest.ps1"
$releaseUpdateManifestVerifyPath = Join-Path $PSScriptRoot "verify-release-update-manifest.ps1"
$releaseUpdateFeedPath = Join-Path $PSScriptRoot "generate-release-update-feed.ps1"
$releaseUpdateFeedVerifyPath = Join-Path $PSScriptRoot "verify-release-update-feed.ps1"
$supportBundleVerifyPath = Join-Path $PSScriptRoot "verify-support-bundle-export.ps1"

Write-Step "Validate release metadata"
Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseMetadataCheckPath)

Write-Step "Validate release operations policy"
Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseOperationsCheckPath)

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

        Write-Step "Sign release artifact"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseArtifactSignPath)

        Write-Step "Run release smoke"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseSmokePath)

        Write-Step "Prepare release artifact"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseArtifactPrepPath)

        Write-Step "Verify release artifact"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseArtifactVerifyPath)

        Write-Step "Package release artifact"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releasePackagePath)

        Write-Step "Verify release package"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releasePackageVerifyPath)

        Write-Step "Generate release update manifest"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseUpdateManifestPath)

        Write-Step "Verify release update manifest"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseUpdateManifestVerifyPath)

        Write-Step "Generate release update feed"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseUpdateFeedPath)

        Write-Step "Verify release update feed"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseUpdateFeedVerifyPath)

        Write-Step "Verify installer round-trip"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $releaseInstallerVerifyPath)

        Write-Step "Verify support bundle export"
        Invoke-CheckedCommand -FilePath powershell.exe -Arguments @("-ExecutionPolicy", "Bypass", "-File", $supportBundleVerifyPath)
    }
}
finally {
    Pop-Location
}

Write-Step "Run Rust tests"
Push-Location $nativeDir
try {
    Invoke-RustTests -CargoPath $cargoPath
}
finally {
    Pop-Location
}

Write-Step "Verification complete"
if ($PublishApp) {
    Write-Info "Build, C# tests, release publish, signing hook, release smoke, artifact preparation, artifact verification, release packaging, package verification, update manifest/feed generation and verification, installer round-trip verification, support-bundle verification, and Rust tests all ran successfully."
}
else {
    Write-Info "Build, C# tests, and Rust tests all ran successfully."
}
