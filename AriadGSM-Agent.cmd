@echo off
setlocal
set "ROOT=%~dp0"
set "AGENT_EXE="

for /f "delims=" %%D in ('dir /b /ad /o-d "%ROOT%desktop-agent\dist\AriadGSMAgent-next*" 2^>nul') do (
  if not defined AGENT_EXE (
    if exist "%ROOT%desktop-agent\dist\%%D\AriadGSM Agent.exe" set "AGENT_EXE=%ROOT%desktop-agent\dist\%%D\AriadGSM Agent.exe"
  )
)

if not defined AGENT_EXE if exist "%ROOT%desktop-agent\dist\AriadGSMAgent\AriadGSM Agent.exe" (
  set "AGENT_EXE=%ROOT%desktop-agent\dist\AriadGSMAgent\AriadGSM Agent.exe"
)

if not defined AGENT_EXE (
  set "AGENT_EXE=%ROOT%desktop-agent\windows-app\src\AriadGSM.Agent.Desktop\bin\Debug\net10.0-windows\AriadGSM Agent.exe"
)

if not exist "%AGENT_EXE%" (
  echo AriadGSM Agent.exe no existe todavia.
  echo Ejecuta desktop-agent\windows-app\build-agent-package.cmd para crear el ejecutable.
  exit /b 1
)

start "" "%AGENT_EXE%" %*
