param(
    [switch]$BuildSetupExe,
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$verifyScript = Join-Path $scriptRoot "verify-dev.ps1"

& powershell.exe -ExecutionPolicy Bypass -File $verifyScript -Configuration Release -PublishApp
if ($LASTEXITCODE -ne 0) {
    throw "Release verification failed."
}

if ($BuildSetupExe) {
    $buildSetupScript = Join-Path $scriptRoot "build-setup-exe.ps1"
    $setupArgs = @("-ExecutionPolicy", "Bypass", "-File", $buildSetupScript)
    if (-not [string]::IsNullOrWhiteSpace($IsccPath)) {
        $setupArgs += @("-IsccPath", $IsccPath)
    }

    & powershell.exe @setupArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Setup exe build failed."
    }
}
