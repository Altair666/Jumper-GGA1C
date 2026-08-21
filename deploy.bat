@echo off
setlocal enabledelayedexpansion

set APP_DLL=AltiumSync.dll
set SRC_DIR=%~dp0
set PUB_DIR=%SRC_DIR%publish

echo.
echo === AltiumSync - rebuild and run ===
echo.

:: --- Step 1: Stop running process ---
echo [1/4] Stopping running process...

for /f "tokens=5" %%i in ('netstat -ano 2^>nul ^| findstr ":5000 "') do (
    set PORT_PID=%%i
)
if defined PORT_PID (
    echo     Killing PID !PORT_PID! on port 5000...
    taskkill /PID !PORT_PID! /F >nul 2>&1
)

echo     Waiting 4 seconds...
timeout /t 4 /nobreak >nul
echo.

:: --- Step 2: Clean old build ---
echo [2/4] Cleaning old build...
if exist "%PUB_DIR%" (
    rmdir /s /q "%PUB_DIR%" 2>nul
    timeout /t 2 /nobreak >nul
    rmdir /s /q "%PUB_DIR%" 2>nul
    echo     Done.
) else (
    echo     publish folder not found.
)
:: DB stored at C:\AltiumSync\sync.db - settings persist between deploys
echo.

:: --- Step 3: Build ---
echo [3/4] Building...
echo.

cd /d "%SRC_DIR%"
dotnet publish -c Release -o "%PUB_DIR%" --nologo
set BUILD_ERR=%errorlevel%

if %BUILD_ERR% neq 0 (
    echo.
    echo *** BUILD FAILED ***
    echo.
    pause
    exit /b 1
)

echo.
echo     Build OK.
echo.

:: --- Step 4: Run ---
echo [4/4] Starting...
echo.
echo     URL: http://localhost:5000
echo     Press Ctrl+C to stop
echo.
echo ======================================
echo.

cd /d "%PUB_DIR%"
dotnet "%APP_DLL%"

echo.
echo Server stopped.
pause
