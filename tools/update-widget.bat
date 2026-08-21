@echo off
setlocal
:: Refresh local debug copy of the Grist widget from its own repository.
:: Source repo: https://github.com/Altair666/widget_report_grist
:: Run this after every widget change lands on GitHub, then restart AltiumSync.

set WIDGET_REPO=C:\widget_report_grist
set TARGET=%~dp0..\wwwroot\widget\index.html

if not exist "%WIDGET_REPO%\.git" (
    echo Widget repo not found at %WIDGET_REPO%
    echo Clone it first: git clone https://github.com/Altair666/widget_report_grist.git %WIDGET_REPO%
    exit /b 1
)

echo [1/3] Pulling latest widget...
cd /d "%WIDGET_REPO%"
git pull --ff-only
if errorlevel 1 (
    echo *** git pull failed ***
    exit /b 1
)

echo [2/3] Copying index.html into wwwroot\widget ...
copy /y "%WIDGET_REPO%\index.html" "%TARGET%" >nul
if errorlevel 1 (
    echo *** copy failed ***
    exit /b 1
)

echo [3/3] Widget version now in local copy:
findstr /c:"const VERSION" "%TARGET%"

echo.
echo Done. Now run deploy.bat to republish AltiumSync.
endlocal
