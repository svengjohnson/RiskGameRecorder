@echo off

:: Ensure Obfuscar is installed
dotnet tool update --global Obfuscar.GlobalTool >nul 2>&1
if %ERRORLEVEL% neq 0 (
    dotnet tool install --global Obfuscar.GlobalTool >nul 2>&1
)
set OBFUSCAR=%USERPROFILE%\.dotnet\tools\obfuscar.console.exe
if not exist "%OBFUSCAR%" (
    echo Obfuscar not found. Run: dotnet tool install --global Obfuscar.GlobalTool
    pause
    exit /b 1
)

:: Build
dotnet build -c Release
if %ERRORLEVEL% neq 0 (
    echo Build failed.
    pause
    exit /b 1
)

:: Obfuscate
"%OBFUSCAR%" obfuscar.xml
if %ERRORLEVEL% neq 0 (
    echo Obfuscation failed.
    pause
    exit /b 1
)

:: Replace DLL with obfuscated version
copy /y bin\Release\net8.0-windows\win-x64\obfuscated\RiskGameRecorder.dll bin\Release\net8.0-windows\win-x64\RiskGameRecorder.dll >nul

:: Publish single-file exe (reuses existing build)
dotnet publish -c Release --no-build -o publish
if %ERRORLEVEL% neq 0 (
    echo Publish failed.
    pause
    exit /b 1
)

:: Copy to dist
if not exist dist mkdir dist
copy /y publish\RiskGameRecorder.exe dist\RiskGameRecorder.exe >nul

echo.
echo Done. Distributable: %~dp0dist\RiskGameRecorder.exe
pause
