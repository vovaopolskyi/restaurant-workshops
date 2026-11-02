@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "SDCT_CONF=%CD%\.syndicate-config-local"

echo Step 1: resume resource deployment using the latest bundle.
"%CD%\.venv\Scripts\syndicate.exe" deploy --deploy-name dev --continue-deploy
if errorlevel 1 goto failed

echo Step 2: apply corrected routes to the existing API Gateway.
"%CD%\.venv\Scripts\syndicate.exe" update --deploy-name dev --update-only-resources restaurant-backend-api --replace-output
if errorlevel 1 goto failed

echo Backend deployment commands completed successfully.
popd
exit /b 0

:failed
set "DEPLOY_EXIT_CODE=%ERRORLEVEL%"
echo Deployment stopped. Keep the error output for diagnosis.
popd
exit /b %DEPLOY_EXIT_CODE%
