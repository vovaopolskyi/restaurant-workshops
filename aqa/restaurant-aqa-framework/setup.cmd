@echo off
setlocal
cd /d "%~dp0"
if exist package-lock.json (
  call npm ci --ignore-scripts
) else (
  call npm install --ignore-scripts --save-exact
)
if errorlevel 1 exit /b %errorlevel%
call npx playwright install chromium
if errorlevel 1 exit /b %errorlevel%
call npm run check
if errorlevel 1 exit /b %errorlevel%
call npm run test:list
