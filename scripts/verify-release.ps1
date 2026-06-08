$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$verifyScript = Join-Path $scriptRoot "verify-dev.ps1"

& powershell.exe -ExecutionPolicy Bypass -File $verifyScript -Configuration Release -PublishApp
if ($LASTEXITCODE -ne 0) {
    throw "Release verification failed."
}
