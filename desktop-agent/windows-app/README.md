# AriadGSM Windows App

Aplicacion principal de escritorio para operar el agente local sin PowerShell.

## Ejecutable

El agente principal esta en:

```text
desktop-agent\windows-app\src\AriadGSM.Agent.Desktop
```

El actualizador independiente esta en:

```text
desktop-agent\windows-app\src\AriadGSM.Agent.Updater
```

Publicacion:

```text
desktop-agent\windows-app\build-agent-package.cmd
```

Salida:

```text
desktop-agent\dist\AriadGSMAgent\AriadGSM Agent.exe
```

Si ya hay un agente abierto, el empaquetador crea una carpeta lateral versionada:

```text
desktop-agent\dist\AriadGSMAgent-next-<version>-<id>\AriadGSM Agent.exe
```

Los launchers `AriadGSM-Agent.cmd` y `AriadGSM-Agent.vbs` abren automaticamente la version `next` mas reciente.

## Que inicia

- revision de actualizaciones;
- verificacion de WhatsApp 1/2/3;
- panel local Node en `http://127.0.0.1:3000/operativa-v2.html`;
- Vision Worker;
- Perception Worker;
- Hands Worker;
- ciclo Python de Timeline, Cognitive, Operating, Memory y Supervisor.

Todo se inicia mediante `ProcessStartInfo` con ventanas ocultas. No se invoca PowerShell para operar el agente.

Por defecto el agente arranca en modo autonomo. Para cabina manual:

```text
AriadGSM Agent.exe --manual
```

## Actualizaciones

El agente lee el canal:

```text
desktop-agent\update\ariadgsm-update.json
```

Cuando el manifest remoto publica una version nueva con `autoApply: true`, el agente copia `AriadGSM Updater.exe` a `desktop-agent\runtime\updater-runner`, cierra el proceso principal, respalda la version actual, aplica el ZIP y reinicia el agente.

## Seguridad

El manifiesto del ejecutable solicita administrador porque la captura, lectura de ventanas y control de mouse necesitan permisos estables en Windows.

El nivel actual sigue sin escribir ni enviar mensajes a clientes.
