@echo off
setlocal
set "QPRO_HOME=%~dp0.."
if not exist "%QPRO_HOME%\QproFaceTracking.exe" set "QPRO_HOME=%~dp0."
set "QPRO_ROOT=%QPRO_HOME%\QproRuntime"
if not exist "%QPRO_ROOT%\train_tongue_model.py" set "QPRO_ROOT=%QPRO_HOME%"
set "QPRO_HUB=%QPRO_HOME%\QproFaceTracking.exe"
if not exist "%QPRO_HUB%" (
  echo QproFaceTracking.exe is missing. Extract the full ZIP again.
  pause
  exit /b 1
)
tasklist /FI "IMAGENAME eq QproFaceTracking.exe" /NH 2>NUL | find /I "QproFaceTracking.exe" >NUL
if not errorlevel 1 (
  echo QproFaceTracking is already open. Close it first, then run this launcher.
  pause
  exit /b 1
)

set "QPRO_ROCM_INSTALL_SMOKE_TEST="
set "QPRO_ROCM_EXPECTED_GFX_TARGET="
set "ROCM_SDK_TARGET_FAMILY="
if not exist "%QPRO_ROOT%\.venv-rocm-experimental\Scripts\python.exe" goto try_legacy
if not exist "%QPRO_ROOT%\.venv-rocm-experimental\qpro-rocm-ready.json" goto try_legacy
set "QPRO_PYTHON=%QPRO_ROOT%\.venv-rocm-experimental\Scripts\python.exe"
call :check_gpu
if not errorlevel 1 goto launch
echo AMD ROCm 10.0 could not use its validated discrete Radeon GPU. Trying ROCm 7.2.1.

:try_legacy
if not exist "%QPRO_ROOT%\.venv-rocm\Scripts\python.exe" goto no_rocm
if not exist "%QPRO_ROOT%\.venv-rocm\qpro-rocm-ready.json" goto no_rocm
set "QPRO_PYTHON=%QPRO_ROOT%\.venv-rocm\Scripts\python.exe"
set "ROCM_SDK_TARGET_FAMILY=custom"
call :check_gpu
if not errorlevel 1 goto launch

:no_rocm
echo No validated AMD GPU runtime is available. Use Install AMD ROCm in the Hub first.
pause
exit /b 1

:launch
start "" /D "%QPRO_HOME%" "%QPRO_HUB%"
exit /b 0

:check_gpu
pushd "%QPRO_ROOT%"
"%QPRO_PYTHON%" -c "import torch; from qpro_gpu import require_rocm_device_name; from train_tongue_model import create_model; d=require_rocm_device_name(torch); m=torch.nn.Sequential(torch.nn.Conv2d(2,8,3,padding=1),torch.nn.BatchNorm2d(8)).to(d); x=torch.rand(2,2,32,32,device=d); m(x).mean().backward(); torch.cuda.synchronize(device=d); print('AMD GPU ready:',torch.cuda.get_device_name(int(d.split(':')[1])), 'on', d)"
set "QPRO_CHECK_EXIT=%ERRORLEVEL%"
popd
exit /b %QPRO_CHECK_EXIT%
