# AriadGSM Agent Desktop

Aplicacion local para controlar el observador visual de los 3 WhatsApp sin escribir comandos.

## Abrir el launcher

Ejecuta:

```text
AriadGSM-Agent.cmd
```

El launcher permite:

- iniciar el observador continuo;
- detenerlo;
- hacer una lectura una vez;
- abrir la cabina en `ariadgsm.com`;
- abrir logs locales.

Cuando se presiona **Iniciar observador**, la ventana se minimiza sola para dejar libre la pantalla de los 3 WhatsApp.

## Instalar acceso directo

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\install-agent-launcher.ps1
```

Esto crea:

- acceso directo en el escritorio;
- acceso directo en el menu Inicio.

Para iniciar el observador automaticamente con Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\install-agent-launcher.ps1 -WithWindowsStartup
```

## Seguridad

El launcher usa `scripts\visual-agent\visual-agent.cloud.json`, que esta ignorado por Git para no subir tokens.

Nivel actual del agente:

1. Observa pantalla.
2. Lee los 3 WhatsApp.
3. Envia eventos a la nube.
4. Puede calcular/coordenar clics con permiso explicito.

Todavia no escribe mensajes ni envia respuestas a clientes.

## Mouse y lectura de conversaciones

El agente ya tiene control basico de puntero en:

```text
scripts\visual-agent\visual-pointer-control.ps1
```

Por seguridad, el script no mueve nada en modo `Preview`. Para hacer clic necesita `-Execute`.

Ejemplo para abrir una fila visible en el WhatsApp central:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-pointer-control.ps1 -Channel wa-2 -Action OpenChatRow -RowRatio 0.28 -Execute
```

La siguiente etapa sera seleccionar chats por texto detectado, no solo por posicion aproximada.

## Logs

Los logs se guardan en:

```text
scripts\visual-agent\runtime
```

Esa carpeta tambien esta ignorada por Git.
