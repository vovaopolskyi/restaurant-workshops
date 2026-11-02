@echo off
powershell.exe -NoProfile -File "%~dp0..\..\scripts\seed-workshop4-menu.ps1" -Apply
exit /b %ERRORLEVEL%
