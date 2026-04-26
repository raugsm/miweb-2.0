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

## Logs

Los logs se guardan en:

```text
scripts\visual-agent\runtime
```

Esa carpeta tambien esta ignorada por Git.
