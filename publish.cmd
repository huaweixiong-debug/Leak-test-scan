@echo off
setlocal
echo Publishing ATEQ Leak Test (self-contained)...

:: Resolve dotnet command (same logic as build.cmd)
set DOTNET_CMD=
if exist "C:\Users\A\AppData\Local\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_CMD=C:\Users\A\AppData\Local\Microsoft\dotnet\dotnet.exe"
) else if exist "C:\Program Files\dotnet\dotnet.exe" (
    set "DOTNET_CMD=C:\Program Files\dotnet\dotnet.exe"
) else (
    where dotnet >nul 2>&1
    if not errorlevel 1 (set DOTNET_CMD=dotnet)
)

if "%DOTNET_CMD%"=="" (
    echo [ERROR] .NET 8.0 SDK not found.
    exit /b 1
)

cd /d "%~dp0"
echo Using: %DOTNET_CMD%

:: Clean publish output
if exist publish rmdir /s /q publish

:: Self-contained publish
"%DOTNET_CMD%" publish src\ATEQ.LeakTest.Web -c Release -o publish --self-contained true
if errorlevel 1 (
    echo [ERROR] Publish failed.
    exit /b 1
)

echo.
echo Publish SUCCESS. Output: %~dp0publish\
exit /b 0
