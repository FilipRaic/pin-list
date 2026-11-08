@echo off
rem Builds PinList.app for Apple Silicon on Windows into publish\mac\PinList.zip (see publish-mac.ps1).
rem On the Mac it still needs one Terminal command to sign it, printed at the end.
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-mac.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. Is the .NET 10 SDK installed?  winget install Microsoft.DotNet.SDK.10
  pause
  exit /b 1
)
pause
