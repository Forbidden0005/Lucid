@echo off
cd /d "C:\Users\tyler\ExplainMyPC\lucid-desktop\Lucid.App\bin\x64\Debug\net8.0-windows10.0.19041.0"

if not exist "Lucid.App.exe" (
    echo.
    echo [ERROR] Lucid.App.exe not found.
    echo Build the project first using setup.bat or build_vs.bat
    echo.
    pause
    exit /b 1
)

echo Starting Lucid...
Lucid.App.exe
set ERR=%ERRORLEVEL%

if %ERR% NEQ 0 (
    echo.
    echo [ERROR] Lucid exited with error code %ERR%
    echo.
    pause
)
