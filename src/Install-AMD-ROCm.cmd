@echo off
setlocal
set "QPRO_HOME=%~dp0.."
set "QPRO_RUNTIME=%QPRO_HOME%\QproRuntime"
if not exist "%QPRO_RUNTIME%\Install-QproRocm.ps1" (
  set "QPRO_HOME=%~dp0."
  set "QPRO_RUNTIME=%~dp0."
)
cd /d "%QPRO_RUNTIME%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%QPRO_RUNTIME%\Install-QproRocm.ps1"
if errorlevel 1 (
  echo.
  echo AMD ROCm setup failed. Read the error above, then retry this installer.
) else (
  echo.
  echo AMD ROCm setup completed. Open QproFaceTracking.exe to start.
)
pause
