@echo off
powershell.exe -NoProfile -File "%~dp0..\..\scripts\publish-workshop6.ps1"
exit /b %ERRORLEVEL%
