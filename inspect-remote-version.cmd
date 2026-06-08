@echo off
cd /d "%~dp0"
echo __DIR__
dir server.js
echo __FIND_ENGLISH__
findstr /c:"Product profiles saved" server.js
findstr /c:"No active test" server.js
echo __FIND_CHINESE__
findstr /c:"产品档案已保存" server.js
findstr /c:"没有活动测试" server.js
