@echo off
powershell.exe -NoProfile -File "%~dp0..\..\scripts\diagnose-workshop4-menu.ps1"
exit /b %ERRORLEVEL%
