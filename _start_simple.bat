@echo off
cd /d "D:\ATEQ Test\ATEQ-Leak-Test"
set NODE="D:\ATEQ Test\ATEQ-Leak-Test\runtime18\node-v18.20.8-win-x64\node.exe"
set OUT="D:\ATEQ Test\ATEQ-Leak-Test\server.out"
set ERR="D:\ATEQ Test\ATEQ-Leak-Test\server.err"
start "ATEQServer" /B %NODE% server.js 1>>%OUT% 2>>%ERR%
