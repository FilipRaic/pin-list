@echo off
rem Builds the Android app, signed with the release key, into publish\android\PinList.apk.
rem Asks for the keystore password while it runs (hidden, never saved or shown), unless PINLIST_KEYSTORE_PASSWORD is set.
rem Usage: scripts\publish-android.bat [path\to\keystore]   (default: pinlist.keystore in the repo folder)
setlocal
cd /d "%~dp0.."

set "KEYSTORE=%~1"
if "%KEYSTORE%"=="" set "KEYSTORE=%cd%\pinlist.keystore"
if not exist "%KEYSTORE%" (
  echo Keystore not found: %KEYSTORE%
  echo Usage: scripts\publish-android.bat [path\to\keystore]
  pause
  exit /b 1
)

rem A clean build every time: the Android build reuses an already signed APK when only the password or key
rem changed, so an old APK with another signature (or a wrong password) could slip through
if exist "src\PinList.Android\bin\Release" rmdir /s /q "src\PinList.Android\bin\Release"
if exist "src\PinList.Android\obj\Release" rmdir /s /q "src\PinList.Android\obj\Release"

rem The build goes to a temp folder first, only the signed APK is kept
set "BUILD=%TEMP%\pinlist-android-build"
if exist "%BUILD%" rmdir /s /q "%BUILD%"

echo Building PinList for Android with %KEYSTORE%
rem The password only lives in an environment variable of this build (env: tells the Android build to read it there)
powershell -NoProfile -Command "if (-not $env:PINLIST_KEYSTORE_PASSWORD) { $p = Read-Host 'Keystore password' -AsSecureString; $env:PINLIST_KEYSTORE_PASSWORD = [Net.NetworkCredential]::new('', $p).Password }; dotnet publish src\PinList.Android\PinList.Android.csproj -c Release -f net10.0-android -o $env:BUILD -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=$env:KEYSTORE -p:AndroidSigningStorePass=env:PINLIST_KEYSTORE_PASSWORD -p:AndroidSigningKeyAlias=pinlist -p:AndroidSigningKeyPass=env:PINLIST_KEYSTORE_PASSWORD; exit $LASTEXITCODE"
if errorlevel 1 (
  echo.
  echo Build failed. A wrong password shows up above as a keystore error.
  echo Is the Android workload installed?  dotnet workload install android
  if exist "%BUILD%" rmdir /s /q "%BUILD%"
  pause
  exit /b 1
)

if not exist "publish\android" mkdir "publish\android"
copy /y "%BUILD%\com.pinlist.app-Signed.apk" "publish\android\PinList.apk" >nul
rmdir /s /q "%BUILD%"
echo.
echo Done: %cd%\publish\android\PinList.apk
pause
