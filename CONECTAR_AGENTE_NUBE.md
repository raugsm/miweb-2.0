# Conectar agente visual a ariadgsm.com

Estado actual:

- La web en nube responde en `https://ariadgsm.com`.
- `https://ariadgsm.com/api/auth/status` responde `configured: true`.
- La cabina local ya recibio eventos de prueba correctamente.
- La prueba segura contra `https://ariadgsm.com/api/operativa-v2` debe ejecutarse desde el agente local; no usar `curl` manual con la clave en terminal.

## Paso 1 - Copiar token local

Abre este archivo en esta PC:

```text
scripts\visual-agent\visual-agent.config.json
```

El campo autorizado es `agentToken`. Ese archivo esta ignorado por Git y no debe subirse al repo.

## Paso 2 - Poner variable en Render

En Render, servicio `ariadgsm-ops`, agrega o actualiza esta variable:

```text
OPERATIVA_AGENT_KEY
```

El valor debe ser exactamente el valor de `agentToken` del archivo local.

Render debe redesplegar la app despues de guardar la variable.

## Paso 3 - Probar autorizacion sin exponer la clave

Cuando Render termine el redeploy, ejecutar el flujo de Cloud Sync desde el agente local. No copiar la clave a variables de terminal ni a comandos `curl`.

```powershell
python .\desktop-agent\ariadgsm_agent\cloud_sync.py --repo-root . --runtime-dir .\desktop-agent\runtime --cloud-url https://ariadgsm.com --enabled
```

Si el audit log cloud registra el lote con verdict `new`, la conexion esta lista.

Si responde `signature_invalid`, `agent_token_invalid` o `cloud_sync_not_configured`, la clave local y `OPERATIVA_AGENT_KEY` en Render no coinciden o Render aun no redesplego.

## Paso 4 - Enviar prueba controlada a nube

Solo despues de que el paso 3 responda datos:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\send-cloud-smoke-test.ps1
```

Esto copiara una prueba desde `cloud-test-ready` hacia `cloud-inbox` y ejecutara el agente una vez.

## Paso 5 - Ver en la cabina

Entrar a:

```text
https://ariadgsm.com/operativa-v2.html
```

Debe aparecer una alerta de prueba de `Cliente Prueba Nube`.
