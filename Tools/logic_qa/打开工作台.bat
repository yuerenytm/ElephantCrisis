@echo off
cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 goto :no_python

echo Starting logic_qa workbench...
echo Browser: http://localhost:8501
echo Close this window to stop.
echo.

python -m streamlit run "app\streamlit_app.py" --browser.gatherUsageStats false
if errorlevel 1 goto :fail
goto :eof

:no_python
echo ERROR: python not found in PATH.
pause
exit /b 1

:fail
echo.
echo Failed. Try: pip install -r requirements.txt
pause
exit /b 1
