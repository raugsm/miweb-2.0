@echo off
setlocal
set "ROOT=%~dp0"
fltmc >nul 2>&1
if not "%errorlevel%"=="0" (
  powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'wscript.exe' -ArgumentList '\"%ROOT%AriadGSM-Agent.vbs\" -Action Gui' -Verb RunAs -WindowStyle Hidden"
  exit /b
)
start "" wscript.exe "%ROOT%AriadGSM-Agent.vbs" -Action Gui
