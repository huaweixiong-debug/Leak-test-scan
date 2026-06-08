@echo off
cd /d "D:\ATEQ Test\ATEQ-Leak-Test"
start "" /B "D:\ATEQ Test\ATEQ-Leak-Test\runtime18\node-v18.20.8-win-x64\node.exe" server.js >> "D:\ATEQ Test\ATEQ-Leak-Test\server.log" 2>&1