@echo off
cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 goto :no_python

echo Starting balance_analysis workbench...
echo Browser: http://localhost:8503
echo NOTE: Port 8503 = balance_analysis. RL uses 8501; logic_qa uses 8502.
echo Close this window to stop.
echo NOTE: If you run Unity batch from the UI, close the Game project Editor first.
echo.

python -m streamlit run "app\streamlit_app.py" --server.port 8503 --browser.gatherUsageStats false
if errorlevel 1 goto :fail
goto :eof

:no_python
echo ERROR: python not found in PATH.
pause
exit /b 1

:fail
echo.
echo Failed. Try: pip install -r requirements.txt
echo If port 8503 is busy, close the other Streamlit window first.
pause
exit /b 1
