@echo off
setlocal
pushd "%~dp0"
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "SDCT_CONF=%CD%\.syndicate-config-local"
set "SYNDICATE=%CD%\.venv\Scripts\syndicate.exe"
if not exist "%SYNDICATE%" set "SYNDICATE=%CD%\..\..\..\workshop4\backend\restaurant-backend-app\.venv\Scripts\syndicate.exe"
if not exist "%SYNDICATE%" goto failed
dotnet test Tests\APIHandler.Tests.csproj --configuration Release
if errorlevel 1 goto failed
"%SYNDICATE%" sync
if errorlevel 1 goto failed
"%SYNDICATE%" build
if errorlevel 1 goto failed
"%SYNDICATE%" update --deploy-name dev --update-only-resources lambda-basic-execution --update-only-resources api-handler --update-only-resources restaurant-backend-api --replace-output
if errorlevel 1 goto failed
echo Workshop 5 backend update completed.
popd
exit /b 0
:failed
echo Operation failed. Review the error above before retrying.
popd
exit /b 1
