@echo off
setlocal
cd /d "%~dp0"

set "NODE_EXE="
for /f "delims=" %%I in ('where node 2^>nul') do (
  set "NODE_EXE=%%I"
  goto :node_found
)

if exist "C:\Users\Personal\AppData\Local\OpenAI\Codex\bin\node.exe" (
  set "NODE_EXE=C:\Users\Personal\AppData\Local\OpenAI\Codex\bin\node.exe"
  goto :node_found
)

echo No encontre una instalacion utilizable de Node.js desde esta consola.
echo.
echo La ruta de WindowsApps existe, pero Windows la esta bloqueando para ejecucion directa.
echo La solucion mas estable es instalar Node.js en el sistema.
echo.
echo Luego vuelve a abrir este archivo.
pause
exit /b 1

:node_found

echo Generando reporte local del agente...
powershell -ExecutionPolicy Bypass -File ".\scripts\collect-agent-diagnostics.ps1"

echo.
echo Iniciando servidor local en http://localhost:3000
echo Si cierras esta ventana, la web dejara de funcionar.
echo.

"%NODE_EXE%" ".\server.js"

pause
