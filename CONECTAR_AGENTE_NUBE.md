# Conectar agente visual a ariadgsm.com

Estado actual:

- La web en nube responde en `https://ariadgsm.com`.
- `https://ariadgsm.com/api/auth/status` responde `configured: true`.
- La cabina local ya recibio eventos de prueba correctamente.
- La prueba segura contra `https://ariadgsm.com/api/operativa-v2` respondio `401 No autorizado`, porque Railway aun no tiene el token local.

## Paso 1 - Copiar token local

Abre este archivo en esta PC:

```text
scripts\visual-agent\railway-operativa-agent-key.secret.txt
```

Contiene:

```text
OPERATIVA_AGENT_KEY=...
```

Ese archivo esta ignorado por Git y no debe subirse al repo.

## Paso 2 - Poner variable en Railway

En el proyecto de Railway donde corre `ariadgsm.com`, agrega o actualiza esta variable:

```text
OPERATIVA_AGENT_KEY
```

El valor debe ser exactamente el texto despues de `OPERATIVA_AGENT_KEY=` del archivo secreto local.

Railway debe redesplegar la app despues de guardar la variable.

## Paso 3 - Probar autorizacion sin crear datos

Cuando Railway termine el redeploy, ejecutar desde esta carpeta:

```powershell
$token = (Get-Content .\scripts\visual-agent\railway-operativa-agent-key.secret.txt) -replace '^OPERATIVA_AGENT_KEY=', ''
curl.exe -s -H "Authorization: Bearer $token" https://ariadgsm.com/api/operativa-v2
```

Si responde datos de la cabina, la conexion esta lista.

Si responde `{"error":"No autorizado"}`, el token no coincide o Railway aun no redesplego.

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
