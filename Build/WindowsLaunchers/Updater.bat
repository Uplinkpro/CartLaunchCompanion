@echo off
setlocal
start "" /d "%~dp0System\Windows-x64" "%~dp0System\Windows-x64\CartLaunchCompanion.Desktop.exe" --check-for-updates
exit /b 0
