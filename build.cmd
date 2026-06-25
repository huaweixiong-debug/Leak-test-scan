@echo off
setlocal enabledelayedexpansion

echo ========================================================
echo   ATEQ Leak Test - C# .NET 8.0 Build
echo ========================================================
echo.

:: ---- Resolve dotnet command ----
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

echo [1/4] .NET SDK: %DOTNET_CMD%
echo.

:: ---- Stop running server ----
echo [2/4] Stopping running ATEQ server (if any)...
cd /d "%~dp0"
call "%~dp0stop-server.cmd" >nul
timeout /t 2 /nobreak >nul
echo.

:: ---- Restore ----
echo [3/4] Restoring NuGet packages...
"%DOTNET_CMD%" restore ATEQ.LeakTest.sln
if errorlevel 1 (
    echo [ERROR] NuGet restore failed.
    exit /b 1
)
echo.

:: ---- Build ----
echo [4/4] Building solution...
"%DOTNET_CMD%" build ATEQ.LeakTest.sln -c Release --no-restore
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)
echo.

:: ---- Verify ----
dir /b "src\ATEQ.LeakTest.Web\bin\Release\net8.0\ATEQ.LeakTest.Web.dll" 2>nul
if errorlevel 1 (
    echo [WARN] Output DLL not found.
) else (
    echo   ATEQ.LeakTest.Web.dll - OK
)

echo.
echo Build SUCCESS (exit 0)
exit /b 0
