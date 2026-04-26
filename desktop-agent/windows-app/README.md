# AriadGSM Windows App

Aplicacion principal de escritorio para operar el agente local sin PowerShell.

## Ejecutable

El proyecto real esta en:

```text
desktop-agent\windows-app\src\AriadGSM.Agent.Desktop
```

Publicacion:

```text
desktop-agent\windows-app\build-agent-package.cmd
```

Salida:

```text
desktop-agent\dist\AriadGSMAgent\AriadGSM Agent.exe
```

## Que inicia

- panel local Node en `http://127.0.0.1:3000/operativa-v2.html`;
- Vision Worker;
- Perception Worker;
- Hands Worker;
- ciclo Python de Timeline, Cognitive, Operating, Memory y Supervisor.

Todo se inicia mediante `ProcessStartInfo` con ventanas ocultas. No se invoca PowerShell para operar el agente.

## Seguridad

El manifiesto del ejecutable solicita administrador porque la captura, lectura de ventanas y control de mouse necesitan permisos estables en Windows.

El nivel actual sigue sin escribir ni enviar mensajes a clientes.
