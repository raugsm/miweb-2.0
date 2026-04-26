@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0..\..\"
for %%I in ("%ROOT%") do set "ROOT=%%~fI"
set "DIST_BASE=%ROOT%\desktop-agent\dist"
set "DIST=%DIST_BASE%\AriadGSMAgent"
set /p AGENT_VERSION=<"%ROOT%\desktop-agent\windows-app\VERSION"
set "AGENT_FILE_VERSION=%AGENT_VERSION%.0"
pushd "%ROOT%" >nul 2>nul
for /f "tokens=*" %%I in ('git rev-parse --short HEAD 2^>nul') do set "GIT_COMMIT=%%I"
popd >nul 2>nul
if not defined GIT_COMMIT set "GIT_COMMIT=unknown"

set "RUNNING_AGENT="
for %%P in ("AriadGSM Agent.exe" "AriadGSM.Vision.Worker.exe" "AriadGSM.Perception.Worker.exe" "AriadGSM.Interaction.Worker.exe" "AriadGSM.Hands.Worker.exe") do (
  tasklist /fi "imagename eq %%~P" 2>nul | find /i "%%~P" >nul
  if not errorlevel 1 set "RUNNING_AGENT=1"
)

if defined RUNNING_AGENT (
  set "DIST=%DIST_BASE%\AriadGSMAgent-next-%AGENT_VERSION%-%RANDOM%%RANDOM%"
  echo AriadGSM Agent or one of its engines is running. Building side-by-side package:
  echo "!DIST!"
)

if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%DIST%" >nul 2>&1
mkdir "%DIST%\engines\vision" >nul 2>&1
mkdir "%DIST%\engines\perception" >nul 2>&1
mkdir "%DIST%\engines\interaction" >nul 2>&1
mkdir "%DIST%\engines\hands" >nul 2>&1
mkdir "%DIST%\config" >nul 2>&1
mkdir "%DIST%\updater" >nul 2>&1

echo Building AriadGSM Agent Desktop...
dotnet restore "%ROOT%\desktop-agent\windows-app\src\AriadGSM.Agent.Desktop\AriadGSM.Agent.Desktop.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\windows-app\src\AriadGSM.Agent.Desktop\AriadGSM.Agent.Desktop.csproj" -c Release -r win-x64 --self-contained false -o "%DIST%" /p:Version=%AGENT_VERSION% /p:AssemblyVersion=%AGENT_FILE_VERSION% /p:FileVersion=%AGENT_FILE_VERSION% /p:InformationalVersion=%AGENT_VERSION%+%GIT_COMMIT%
if errorlevel 1 exit /b 1

echo Building AriadGSM Updater...
dotnet restore "%ROOT%\desktop-agent\windows-app\src\AriadGSM.Agent.Updater\AriadGSM.Agent.Updater.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\windows-app\src\AriadGSM.Agent.Updater\AriadGSM.Agent.Updater.csproj" -c Release -r win-x64 --self-contained false -o "%DIST%\updater" /p:Version=%AGENT_VERSION% /p:AssemblyVersion=%AGENT_FILE_VERSION% /p:FileVersion=%AGENT_FILE_VERSION% /p:InformationalVersion=%AGENT_VERSION%+%GIT_COMMIT%
if errorlevel 1 exit /b 1


echo Building Vision Worker...
dotnet restore "%ROOT%\desktop-agent\vision-engine\src\AriadGSM.Vision.Worker\AriadGSM.Vision.Worker.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\vision-engine\src\AriadGSM.Vision.Worker\AriadGSM.Vision.Worker.csproj" -c Release -r win-x64 --self-contained false -o "%DIST%\engines\vision" /p:Version=%AGENT_VERSION% /p:AssemblyVersion=%AGENT_FILE_VERSION% /p:FileVersion=%AGENT_FILE_VERSION% /p:InformationalVersion=%AGENT_VERSION%+%GIT_COMMIT%
if errorlevel 1 exit /b 1

echo Building Perception Worker...
dotnet restore "%ROOT%\desktop-agent\perception-engine\src\AriadGSM.Perception.Worker\AriadGSM.Perception.Worker.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\perception-engine\src\AriadGSM.Perception.Worker\AriadGSM.Perception.Worker.csproj" -c Release -r win-x64 --self-contained false -o "%DIST%\engines\perception" /p:Version=%AGENT_VERSION% /p:AssemblyVersion=%AGENT_FILE_VERSION% /p:FileVersion=%AGENT_FILE_VERSION% /p:InformationalVersion=%AGENT_VERSION%+%GIT_COMMIT%
if errorlevel 1 exit /b 1

echo Building Interaction Worker...
dotnet restore "%ROOT%\desktop-agent\interaction-engine\src\AriadGSM.Interaction.Worker\AriadGSM.Interaction.Worker.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\interaction-engine\src\AriadGSM.Interaction.Worker\AriadGSM.Interaction.Worker.csproj" -c Release -r win-x64 --self-contained false -o "%DIST%\engines\interaction" /p:Version=%AGENT_VERSION% /p:AssemblyVersion=%AGENT_FILE_VERSION% /p:FileVersion=%AGENT_FILE_VERSION% /p:InformationalVersion=%AGENT_VERSION%+%GIT_COMMIT%
if errorlevel 1 exit /b 1

echo Building Hands Worker...
dotnet restore "%ROOT%\desktop-agent\hands-engine\src\AriadGSM.Hands.Worker\AriadGSM.Hands.Worker.csproj" -r win-x64
if errorlevel 1 exit /b 1
dotnet publish "%ROOT%\desktop-agent\hands-engine\src\AriadGSM.Hands.Worker\AriadGSM.Hands.Worker.csproj" -c Release -r win-x64 --self-contained false -o "%DIST%\engines\hands" /p:Version=%AGENT_VERSION% /p:AssemblyVersion=%AGENT_FILE_VERSION% /p:FileVersion=%AGENT_FILE_VERSION% /p:InformationalVersion=%AGENT_VERSION%+%GIT_COMMIT%
if errorlevel 1 exit /b 1

copy /y "%ROOT%\desktop-agent\vision-engine\config\vision.example.json" "%DIST%\config\vision.json" >nul
copy /y "%ROOT%\desktop-agent\perception-engine\config\perception.example.json" "%DIST%\config\perception.json" >nul
copy /y "%ROOT%\desktop-agent\interaction-engine\config\interaction.example.json" "%DIST%\config\interaction.json" >nul
copy /y "%ROOT%\desktop-agent\hands-engine\config\hands.example.json" "%DIST%\config\hands.json" >nul

>"%DIST%\ariadgsm-version.json" echo {
>>"%DIST%\ariadgsm-version.json" echo   "appId": "ariadgsm-agent",
>>"%DIST%\ariadgsm-version.json" echo   "version": "%AGENT_VERSION%",
>>"%DIST%\ariadgsm-version.json" echo   "channel": "main",
>>"%DIST%\ariadgsm-version.json" echo   "buildCommit": "%GIT_COMMIT%",
>>"%DIST%\ariadgsm-version.json" echo   "builtAt": "%DATE% %TIME%"
>>"%DIST%\ariadgsm-version.json" echo }

set "ZIP=%DIST_BASE%\AriadGSMAgent-%AGENT_VERSION%.zip"
if exist "%ZIP%" del /q "%ZIP%"
where tar >nul 2>&1
if not errorlevel 1 (
  tar -a -cf "%ZIP%" -C "%DIST%" .
  echo Update zip ready:
  echo "%ZIP%"
  where certutil >nul 2>&1
  if not errorlevel 1 certutil -hashfile "%ZIP%" SHA256
)

echo.
echo AriadGSM executable ready:
echo "%DIST%\AriadGSM Agent.exe"
exit /b 0
