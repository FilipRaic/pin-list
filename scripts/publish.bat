@echo off
rem Builds a Release version into publish\PinList.exe and starts it.
rem Closes a running instance first so the exe is not locked. Data is already saved.
cd /d "%~dp0.."
taskkill /im PinList.exe /f >nul 2>&1
echo Building PinList...
dotnet publish src\PinList\PinList.csproj -c Release -f net10.0-windows -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo.
  echo Build failed. Is the .NET 10 SDK installed?  winget install Microsoft.DotNet.SDK.10
  pause
  exit /b 1
)
echo.
echo Done: %cd%\publish\PinList.exe
start "" "publish\PinList.exe"
