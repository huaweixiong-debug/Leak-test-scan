@echo off
setlocal enabledelayedexpansion
title ATEQ Leak Test Server
cd /d "D:\ATEQ"

set "DOTNET_CMD="
if exist "C:\Users\A\AppData\Local\Microsoft\dotnet\dotnet.exe" (
    set "DOTNET_CMD=C:\Users\A\AppData\Local\Microsoft\dotnet\dotnet.exe"
) else if exist "C:\Program Files\dotnet\dotnet.exe" (
    set "DOTNET_CMD=C:\Program Files\dotnet\dotnet.exe"
) else (
    where dotnet >nul 2>&1
    if not errorlevel 1 (set "DOTNET_CMD=dotnet")
)

if "%DOTNET_CMD%"=="" (
    echo [ERROR] dotnet runtime not found.
    exit /b 1
)

set "PROJECT_DIR=D:\ATEQ\src\ATEQ.LeakTest.Web"
set "SERVER_DLL=%PROJECT_DIR%\bin\Release\net8.0\ATEQ.LeakTest.Web.dll"
set "ASPNETCORE_URLS=http://0.0.0.0:3000"

echo ========================================================
echo   ATEQ Leak Test - C# .NET 8.0 Server
echo ========================================================
echo.

:: ---- Port conflict check ----
for /f "tokens=5" %%a in ('netstat -ano ^| findstr ":3000.*LISTENING" 2^>nul') do (
    for /f "tokens=1 delims=," %%c in ('tasklist /FI "PID eq %%a" /FO CSV /NH 2^>nul') do set PROC=%%~c
    if defined PROC (
        if /i not "!PROC!"=="dotnet.exe" if /i not "!PROC!"=="ATEQ.LeakTest.Web.exe" (
            echo [ERROR] Port 3000 is occupied by !PROC! (PID %%a^)
            echo This is NOT the C# ATEQ server - it may be a legacy Node.js process.
            echo Stop it with: taskkill /F /PID %%a
            pause
            exit /b 1
        )
        :: Verify it is the ATEQ C# service, not some other dotnet app
        curl -s http://127.0.0.1:3000/api/health 2>nul | findstr /C:"dotnet-1.0.0" >nul 2>&1
        if errorlevel 1 (
            echo [ERROR] Port 3000 is owned by dotnet.exe (PID %%a^) but it is NOT the ATEQ C# service.
            echo Stop it with: taskkill /F /PID %%a
            pause
            exit /b 1
        )
        echo Port 3000 is owned by the ATEQ C# service (PID %%a^) - stopping it.
        taskkill /F /PID %%a >nul 2>&1
        timeout /t 2 /nobreak >nul
    )
)

echo   Starting on http://0.0.0.0:3000
echo   Press Ctrl+C to stop
echo.

if not exist "%SERVER_DLL%" (
    echo [ERROR] Built server DLL not found: %SERVER_DLL%
    echo Run build.cmd first.
    exit /b 1
)

pushd "D:\ATEQ"
"%DOTNET_CMD%" "%SERVER_DLL%"
set "RUN_EXIT=%ERRORLEVEL%"
popd
exit /b %RUN_EXIT%
