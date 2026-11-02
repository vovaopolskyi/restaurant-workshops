@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "SDCT_CONF=%CD%\.syndicate-config-local"
"%CD%\.venv\Scripts\syndicate.exe" update --deploy-name dev --update-only-resources restaurant-backend-api --replace-output
set "UPDATE_EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %UPDATE_EXIT_CODE%
