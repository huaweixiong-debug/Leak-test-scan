@echo off
setlocal
cd /d "%~dp0"
REM Rotate logs (keep 3 versions)
if exist server.out.2 del /q server.out.2
if exist server.out.1 ren server.out.1 server.out.2 2>nul
if exist server.out ren server.out server.out.1 2>nul
if exist server.err.2 del /q server.err.2
if exist server.err.1 ren server.err.1 server.err.2 2>nul
if exist server.err ren server.err server.err.1 2>nul
wscript.exe //B "%~dp0start_vbs.vbs"
ping -n 4 127.0.0.1 >nul
echo started
