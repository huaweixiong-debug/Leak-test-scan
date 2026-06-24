@echo off
chcp 65001 >nul
call "%~dp0stop-server.cmd"
timeout /t 2 /nobreak >nul
call "%~dp0run-server.cmd"
