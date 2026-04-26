@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0..\..\"
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "DIST_BASE=%ROOT%\desktop-agent\dist"
set "DIST=%DIST_BASE%\AriadGSMAgent"

tasklist /fi "imagename eq AriadGSM Agent.exe" 2>nul | find /i "AriadGSM Agent.exe" >nul
if not errorlevel 1 (
  set "DIST=%DIST_BASE%\AriadGSMAgent-next"
  echo AriadGSM Agent is running. Building side-by-side package:
  echo "!DIST!"
)

if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%DIST%" >nul 2>&1
mkdir "%DIST%\engines\vision" >nul 2>&1
mkdir "%DIST%\engines\perception" >nul 2>&1
mkdir "%DIST%\engines\hands" >nul 2>&1

echo Building AriadGSM Agent Desktop...
dotnet restore "%ROOT%\desktop-agent\windows-app\src\AriadGSM.Agent.Desktop\AriadGSM.Agent.Desktop.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\windows-app\src\AriadGSM.Agent.Desktop\AriadGSM.Agent.Desktop.csproj" -c Release -r win-x64 --self-contained false --no-restore -o "%DIST%"
if errorlevel 1 exit /b 1


echo Building Vision Worker...
dotnet restore "%ROOT%\desktop-agent\vision-engine\src\AriadGSM.Vision.Worker\AriadGSM.Vision.Worker.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\vision-engine\src\AriadGSM.Vision.Worker\AriadGSM.Vision.Worker.csproj" -c Release -r win-x64 --self-contained false --no-restore -o "%DIST%\engines\vision"
if errorlevel 1 exit /b 1

echo Building Perception Worker...
dotnet restore "%ROOT%\desktop-agent\perception-engine\src\AriadGSM.Perception.Worker\AriadGSM.Perception.Worker.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\perception-engine\src\AriadGSM.Perception.Worker\AriadGSM.Perception.Worker.csproj" -c Release -r win-x64 --self-contained false --no-restore -o "%DIST%\engines\perception"
if errorlevel 1 exit /b 1

echo Building Hands Worker...
dotnet restore "%ROOT%\desktop-agent\hands-engine\src\AriadGSM.Hands.Worker\AriadGSM.Hands.Worker.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\hands-engine\src\AriadGSM.Hands.Worker\AriadGSM.Hands.Worker.csproj" -c Release -r win-x64 --self-contained false --no-restore -o "%DIST%\engines\hands"
if errorlevel 1 exit /b 1

echo.
echo AriadGSM executable ready:
echo "%DIST%\AriadGSM Agent.exe"
exit /b 0
