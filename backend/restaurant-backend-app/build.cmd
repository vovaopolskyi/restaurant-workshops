@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "SDCT_CONF=%CD%\.syndicate-config-local"
"%CD%\.venv\Scripts\syndicate.exe" build
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %BUILD_EXIT_CODE%
