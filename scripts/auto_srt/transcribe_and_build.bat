@echo off
setlocal

if "%~1"=="" (
  echo Drag and drop an audio file onto this .bat
  pause
  exit /b 1
)

REM Prefer venv python if present
if exist "%~dp0.venv\Scripts\python.exe" (
  "%~dp0.venv\Scripts\python.exe" "%~dp0pipeline.py" "%~1"
) else (
  python "%~dp0pipeline.py" "%~1"
)

pause
