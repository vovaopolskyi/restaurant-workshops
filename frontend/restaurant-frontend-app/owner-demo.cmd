@echo off
setlocal
pushd "%~dp0"
call npm.cmd start -- --open --serve-path / --host localhost
set "DEMO_EXIT_CODE=%ERRORLEVEL%"
popd
exit /b %DEMO_EXIT_CODE%
