# Lucid - Complete Dev Environment Setup
# Run by double-clicking setup.bat
# Installs EVERYTHING needed to build and run Lucid on a fresh Windows machine.

$ErrorActionPreference = "Continue"

function Write-Step($n, $msg) { Write-Host "`n>>> [$n] $msg" -ForegroundColor Cyan }
function Write-OK($msg)       { Write-Host "    [OK] $msg" -ForegroundColor Green }
function Write-Skip($msg)     { Write-Host "    [--] Already installed: $msg" -ForegroundColor Yellow }
function Write-Warn($msg)     { Write-Host "    [!!] $msg" -ForegroundColor Red }
function Write-Info($msg)     { Write-Host "    $msg" -ForegroundColor Gray }

function Refresh-Path {
    $env:PATH = ([System.Environment]::GetEnvironmentVariable("PATH", "Machine")) + ";" +
                ([System.Environment]::GetEnvironmentVariable("PATH", "User"))
}

Write-Host ""
Write-Host "  ============================================================" -ForegroundColor Cyan
Write-Host "   LUCID - Dev Environment Setup" -ForegroundColor Cyan
Write-Host "   Installs everything needed to build and run on this machine" -ForegroundColor Cyan
Write-Host "  ============================================================" -ForegroundColor Cyan
Write-Host ""

# Admin notice
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "  Note: Not running as Administrator. Some installs may require elevation." -ForegroundColor Yellow
    Write-Host ""
}

# -----------------------------------------------------------------------------
# STEP 1: Windows version check
# -----------------------------------------------------------------------------
Write-Step "1/9" "Windows Version Check"
$build = [System.Environment]::OSVersion.Version.Build
Write-Info "Windows build: $build"
if ($build -lt 19041) {
    Write-Warn "Build $build is too old. WinUI 3 requires Windows 10 build 19041+ (version 2004 / May 2020 Update)."
    Write-Warn "Please update Windows then re-run this script."
    Write-Host "`nPress any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}
Write-OK "Windows build $build - meets minimum requirement (19041+)"

# -----------------------------------------------------------------------------
# STEP 2: winget check
# -----------------------------------------------------------------------------
Write-Step "2/9" "winget (Windows Package Manager)"
$wg = Get-Command winget -ErrorAction SilentlyContinue
if (-not $wg) {
    Write-Warn "winget not found. Install App Installer from the Microsoft Store."
    Write-Warn "Direct link: https://aka.ms/getwinget"
    Write-Host "`nPress any key to exit..." -ForegroundColor Gray
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    exit 1
}
$wgVer = & winget --version 2>$null
Write-OK "winget $wgVer"

# -----------------------------------------------------------------------------
# STEP 3: Visual C++ Redistributable 2015-2022 x64
#   Required by: WinUI 3, .NET native runtime, Windows App SDK
# -----------------------------------------------------------------------------
Write-Step "3/9" "Visual C++ Redistributable 2015-2022 x64"
$vcKey = "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\X64"
$vcInstalled = (Test-Path $vcKey) -and ((Get-ItemProperty $vcKey -ErrorAction SilentlyContinue).Installed -eq 1)
if ($vcInstalled) {
    $vcVer = (Get-ItemProperty $vcKey -ErrorAction SilentlyContinue).Version
    Write-Skip "VC++ Redist $vcVer"
} else {
    winget install Microsoft.VCRedist.2015+.x64 --silent --accept-package-agreements --accept-source-agreements
    Write-OK "Visual C++ Redistributable 2015-2022 x64 installed"
}

# -----------------------------------------------------------------------------
# STEP 4: Git
#   Required by: source control, NuGet source resolution
# -----------------------------------------------------------------------------
Write-Step "4/9" "Git for Windows"
Refresh-Path
$gitCmd = Get-Command git -ErrorAction SilentlyContinue
if ($gitCmd) {
    $gitVer = & git --version 2>$null
    Write-Skip $gitVer
} else {
    winget install Git.Git --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path
    Write-OK "Git installed"
}

# -----------------------------------------------------------------------------
# STEP 5: .NET 8 SDK
#   Required by: dotnet build, dotnet restore, C# compilation
# -----------------------------------------------------------------------------
Write-Step "5/9" ".NET 8 SDK"
Refresh-Path
$sdk8 = $null
$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetCmd) {
    $allSdks = & dotnet --list-sdks 2>$null
    $sdk8 = $allSdks | Where-Object { $_ -like "8.*" }
}
if ($sdk8) {
    Write-Skip ".NET 8 SDK: $($sdk8 -join ' | ')"
} else {
    winget install Microsoft.DotNet.SDK.8 --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path
    Write-OK ".NET 8 SDK installed"
}

# -----------------------------------------------------------------------------
# STEP 6: Visual Studio 2022 Build Tools + required workloads
#   Required by: MSBuild, XamlPreCompile (WinUI 3 XAML), Windows App SDK tooling
#
#   Workloads installed:
#     ManagedDesktopBuildTools  - MSBuild, Roslyn, .NET desktop build support
#     Windows11SDK.22621        - Windows 11 SDK headers and tools
#     WindowsAppSDK.Cs          - WinUI 3 / Windows App SDK XAML compiler
# -----------------------------------------------------------------------------
Write-Step "6/9" "Visual Studio 2022 Build Tools + Workloads"

$vsWhere     = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsInstaller = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vs_installer.exe"

$workloads = @(
    "Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools",
    "Microsoft.VisualStudio.Component.Windows11SDK.22621",
    "Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs"
)

$vsPath = $null
if (Test-Path $vsWhere) {
    $vsPath = & $vsWhere -latest -property installationPath 2>$null
}

if ($vsPath) {
    Write-Info "Visual Studio found at: $vsPath"
    Write-Info "Ensuring required workloads are installed (may take a few minutes)..."
    if (Test-Path $vsInstaller) {
        $modArgs = @("modify", "--installPath", $vsPath, "--quiet", "--norestart")
        foreach ($w in $workloads) { $modArgs += "--add"; $modArgs += $w }
        $proc = Start-Process -FilePath $vsInstaller -ArgumentList $modArgs -Wait -PassThru -NoNewWindow
        if ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 3010) {
            Write-OK "VS workloads verified/updated (exit: $($proc.ExitCode))"
        } else {
            Write-Info "VS installer returned $($proc.ExitCode) - workloads likely already present, continuing"
        }
    } else {
        Write-Skip "VS present but installer not available for workload check"
    }
} else {
    Write-Info "Visual Studio not found. Installing VS 2022 Build Tools..."
    Write-Info "(Large download - this can take 10-30 minutes on first install)"
    $addArgs = ($workloads | ForEach-Object { "--add $_" }) -join " "
    $override = "--quiet --norestart $addArgs"
    winget install Microsoft.VisualStudio.2022.BuildTools --silent --accept-package-agreements --accept-source-agreements --override $override
    Write-OK "VS 2022 Build Tools installed with required workloads"
}

# -----------------------------------------------------------------------------
# STEP 7: Windows App SDK Runtime 1.5
#   Required by: running the unpackaged app
# -----------------------------------------------------------------------------
Write-Step "7/9" "Windows App SDK Runtime 1.5"
winget install Microsoft.WindowsAppRuntime.1.5 --silent --accept-package-agreements --accept-source-agreements
Write-OK "Windows App SDK Runtime 1.5 ensured"

# -----------------------------------------------------------------------------
# STEP 7b: Ollama (local AI engine for the companion chat)
#   Free, runs entirely on this machine, no account or API key needed.
#   Model: llama3.2:3b (~2GB download, one-time, works on any modern PC)
# -----------------------------------------------------------------------------
Write-Step "7b" "Ollama (local AI engine)"

$ollamaExe = Get-Command ollama -ErrorAction SilentlyContinue
if (-not $ollamaExe) {
    $ollamaExe = Get-Command "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe" -ErrorAction SilentlyContinue
}

if ($ollamaExe) {
    Write-Skip "Ollama already installed"
} else {
    Write-Info "Downloading and installing Ollama..."
    Write-Info "(Ollama powers the AI chat - free, local, nothing leaves your machine)"
    winget install Ollama.Ollama --silent --accept-package-agreements --accept-source-agreements
    Refresh-Path
    Write-OK "Ollama installed"
}

# Pull the AI model (llama3.2:3b - good quality, works on any modern machine)
Write-Info "Checking for llama3.2:3b AI model..."
$ollamaCmd = Get-Command ollama -ErrorAction SilentlyContinue
if (-not $ollamaCmd) {
    $ollamaPath = "$env:LOCALAPPDATA\Programs\Ollama\ollama.exe"
    if (Test-Path $ollamaPath) { $ollamaCmd = $ollamaPath }
}

if ($ollamaCmd) {
    # Start Ollama serve in background if not already running
    $ollamaRunning = $false
    try {
        $resp = Invoke-WebRequest -Uri "http://localhost:11434/api/tags" -TimeoutSec 3 -ErrorAction Stop
        $ollamaRunning = $true
        Write-Info "Ollama server already running"
    } catch {
        Write-Info "Starting Ollama server..."
        Start-Process -FilePath ($ollamaCmd.Source ?? $ollamaCmd) -ArgumentList "serve" -WindowStyle Hidden
        Start-Sleep -Seconds 3
    }

    # Check if model is already pulled
    try {
        $tags = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -TimeoutSec 5 -ErrorAction Stop
        $hasModel = $tags.models | Where-Object { $_.name -like "*llama3.2*" }
        if ($hasModel) {
            Write-Skip "llama3.2:3b model already downloaded"
        } else {
            Write-Info "Downloading llama3.2:3b model (~2GB - this is a one-time download)..."
            Write-Info "This may take 5-15 minutes depending on your connection speed."
            & ($ollamaCmd.Source ?? $ollamaCmd) pull llama3.2:3b
            Write-OK "llama3.2:3b model ready"
        }
    } catch {
        Write-Info "Could not verify model status - you can pull it manually:"
        Write-Info "  ollama pull llama3.2:3b"
    }
} else {
    Write-Warn "Ollama not found in PATH after install - restart and run: ollama pull llama3.2:3b"
}

# -----------------------------------------------------------------------------
# STEP 8: NuGet restore
# -----------------------------------------------------------------------------
Write-Step "8/9" "NuGet package restore"
$slnx = "C:\Users\tyler\ExplainMyPC\lucid-desktop\Lucid.slnx"
Refresh-Path

if (-not (Test-Path $slnx)) {
    Write-Warn "Solution not found: $slnx"
    Write-Warn "If the repo is not cloned yet, run:"
    Write-Warn "  git clone https://github.com/Forbidden0005/ExplainMyPC C:\Users\tyler\ExplainMyPC"
} else {
    $dotnetExe = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnetExe) {
        & dotnet restore $slnx
        if ($LASTEXITCODE -eq 0) {
            Write-OK "NuGet packages restored"
        } else {
            Write-Warn "dotnet restore returned $LASTEXITCODE - see output above"
        }
    } else {
        Write-Warn "dotnet not in PATH yet - close this window, reopen, and re-run setup.bat"
    }
}

# -----------------------------------------------------------------------------
# STEP 9: Build (Debug x64)
# -----------------------------------------------------------------------------
Write-Step "9/9" "Build (Debug, x64)"

$vsPath = $null
if (Test-Path $vsWhere) {
    $vsPath = & $vsWhere -latest -property installationPath 2>$null
}
$msbuildExe = $null
if ($vsPath) {
    $candidate = "$vsPath\MSBuild\Current\Bin\amd64\MSBuild.exe"
    if (Test-Path $candidate) { $msbuildExe = $candidate }
}

if (Test-Path $slnx) {
    if ($msbuildExe) {
        Write-Info "Using MSBuild: $msbuildExe"
        & $msbuildExe $slnx /p:Platform=x64 /p:Configuration=Debug /nologo /m
    } else {
        Write-Info "MSBuild not found - using dotnet build"
        Write-Info "(If XAML errors occur later, run build_vs.bat to regenerate the intermediate XAML DLL)"
        $dotnetExe = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($dotnetExe) {
            & dotnet build $slnx -c Debug -p:Platform=x64
        } else {
            Write-Warn "dotnet not in PATH - close this window, reopen, and re-run setup.bat"
        }
    }

    if ($LASTEXITCODE -eq 0) {
        Write-OK "Build succeeded"
    } else {
        Write-Warn "Build returned $LASTEXITCODE - see output above"
        Write-Warn "If XAML errors appear, run: C:\Users\tyler\build_vs.bat"
    }
} else {
    Write-Warn "Cannot build - solution not found. Clone the repo first."
}

# -----------------------------------------------------------------------------
# Done
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "  ============================================================" -ForegroundColor Green
Write-Host "   Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "   To launch Lucid:" -ForegroundColor Green
Write-Host "   C:\Users\tyler\ExplainMyPC\lucid-desktop\Launch.bat" -ForegroundColor Green
Write-Host "  ============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Press any key to close..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
