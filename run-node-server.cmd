@echo off
setlocal
cd /d "%~dp0"
set "NODE_EXE=%~dp0runtime18\node-v18.20.8-win-x64\node.exe"
if not exist "%NODE_EXE%" (
  echo node runtime not found: %NODE_EXE%
  exit /b 1
)
"%NODE_EXE%" server.js 1>>"%~dp0server.out" 2>>"%~dp0server.err"
