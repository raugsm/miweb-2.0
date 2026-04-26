@echo off
setlocal
set "ROOT=%~dp0"
start "" wscript.exe "%ROOT%AriadGSM-Agent.vbs" -Action Gui
