@echo off
cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 goto :no_python

set MATCHES=%~1
if "%MATCHES%"=="" set MATCHES=10
set SEED=%~2
if "%SEED%"=="" set SEED=1

echo LogicSim batch: matches=%MATCHES% seed=%SEED%
echo Close Unity Editor on Game project first.
echo.

python scripts\run_batch.py --matches %MATCHES% --seed %SEED% --out output
echo.
echo Exit code %ERRORLEVEL%
pause
goto :eof

:no_python
echo ERROR: python not found in PATH.
pause
exit /b 1
