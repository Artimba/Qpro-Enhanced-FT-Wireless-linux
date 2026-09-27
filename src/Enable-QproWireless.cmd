@echo off
setlocal
set "QPRO_HOME=%~dp0.."
set "QPRO_RUNTIME=%QPRO_HOME%\QproRuntime"
if not exist "%QPRO_RUNTIME%\Enable-QproWireless.ps1" (
  set "QPRO_HOME=%~dp0."
  set "QPRO_RUNTIME=%~dp0."
)
cd /d "%QPRO_RUNTIME%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%QPRO_RUNTIME%\Enable-QproWireless.ps1"
if errorlevel 1 echo Wireless setup failed. Read the error above.
pause
