@echo off
setlocal
pushd %~dp0
if not exist node_modules (
	call npm ci
	if errorlevel 1 goto :fail
)
npm run build
if errorlevel 1 goto :fail
popd
exit /b 0

:fail
popd
exit /b 1
