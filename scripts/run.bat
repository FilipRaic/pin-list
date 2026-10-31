@echo off
rem Runs PinList in Debug mode (dotnet run).
rem Closes a running instance first - only one instance may run. Data is already saved.
cd /d "%~dp0.."
taskkill /im PinList.exe /f >nul 2>&1
dotnet run --project src\PinList\PinList.csproj -c Debug -f net10.0-windows
