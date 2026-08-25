@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Portable.ps1"
if errorlevel 1 (
    echo.
    echo Portable publish failed.
    pause
    exit /b 1
)
echo.
echo Release packages are in the versioned artifacts folder.
pause
