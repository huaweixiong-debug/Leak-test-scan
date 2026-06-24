@echo off
title ATEQ Leak Test
cd /d "%~dp0"
echo Starting ATEQ Leak Test Server...
start "" "ATEQ.LeakTest.Web.exe"
echo Waiting for server to be ready...
:wait
timeout /t 2 /nobreak >nul
curl -s http://127.0.0.1:3000/api/health >nul 2>&1
if errorlevel 1 goto wait
echo Server ready. Opening browser...
start http://127.0.0.1:3000
echo Done.
