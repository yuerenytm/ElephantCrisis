@echo off
cd /d "%~dp0"

set "PY="
if exist "%~dp0.venv\Scripts\python.exe" set "PY=%~dp0.venv\Scripts\python.exe"
if not defined PY where python >nul 2>&1 && set "PY=python"
if not defined PY goto :no_python

echo Starting RL workbench...
echo Browser: http://localhost:8501
echo NOTE: Port 8501 = RL. logic_qa uses 8502; balance_advisor uses 8503.
echo Close this window to stop.
echo NOTE: Close Game project Editor before Unity train/eval.
echo Trainer needs Python 3.10 venv: Tools\rl\.venv
echo.
"%PY%" -m streamlit run "app\streamlit_app.py" --server.port 8501 --browser.gatherUsageStats false
if errorlevel 1 goto :fail
goto :eof

:no_python
echo ERROR: python not found. Install Python 3.10 and create Tools\rl\.venv
pause
exit /b 1

:fail
echo.
echo Failed. Try:
echo   py -3.10 -m venv .venv
echo   .venv\Scripts\python -m pip install -r requirements.txt
echo If port 8501 is busy, close the other Streamlit window first.
pause
exit /b 1
