@echo off
cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 goto :no_python

echo Starting logic_qa workbench...
echo Browser: http://localhost:8502
echo NOTE: Port 8502 = logic_qa. RL uses 8501; balance_analysis uses 8503.
echo Close this window to stop.
echo.

python -m streamlit run "app\streamlit_app.py" --server.port 8502 --browser.gatherUsageStats false
if errorlevel 1 goto :fail
goto :eof

:no_python
echo ERROR: python not found in PATH.
pause
exit /b 1

:fail
echo.
echo Failed. Try: pip install -r requirements.txt
echo If port 8502 is busy, close the other Streamlit window first.
pause
exit /b 1
