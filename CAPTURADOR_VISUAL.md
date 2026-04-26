# Capturador visual AriadGSM

Este capturador reconstruye el paso que ya habia llegado a funcionar: leer pantalla, convertir texto visible en eventos y alimentar `https://ariadgsm.com`.

## Estado

- `ariadgsm.com` autoriza el `OPERATIVA_AGENT_KEY` de Railway.
- La nube ya contiene eventos reales previos de `Lectura visual`, `visible_window` y `agent_status`.
- En GitHub no hay otra rama con el capturador anterior; por eso se agrego un capturador nuevo local.
- El nuevo script usa OCR nativo de Windows y divide el monitor ultrawide en 3 secciones: `wa-1`, `wa-2`, `wa-3`.

## Archivo principal

```text
scripts\visual-agent\visual-screen-capture.ps1
```

## Launcher local

Para usar el agente sin escribir comandos:

```text
AriadGSM-Agent.cmd
```

Para instalar accesos directos en Windows:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\install-agent-launcher.ps1
```

## Modo autopiloto

El autopiloto coordina las piezas locales en ciclos:

```text
scripts\visual-agent\visual-autopilot.ps1
```

En cada ciclo intenta abrir/acomodar WhatsApp, captura pantalla, atiende alertas clasificadas y aprende chats visibles. No escribe mensajes al cliente.

Preview seguro:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-autopilot.ps1
```

Ciclo real:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-autopilot.ps1 -Execute -Send -OpenWhatsApp -ArrangeWindows
```

El launcher tiene el boton **Autopiloto** para dejarlo corriendo en segundo plano. **Detener** lo apaga.

## Control visual con puntero

El control del mouse existe en modo seguro y no hace clics a menos que se use `-Execute`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-pointer-control.ps1 -Channel wa-2 -Action Preview
```

Para enfocar una seccion:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-pointer-control.ps1 -Channel wa-2 -Action FocusChannel -Execute
```

Para abrir una fila de chat por posicion vertical aproximada:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-pointer-control.ps1 -Channel wa-2 -Action OpenChatRow -RowRatio 0.28 -Execute
```

Por ahora no escribe ni envia mensajes; solo puede enfocar/abrir una zona con permiso explicito.

Ya existe un primer navegador por texto:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-navigator.ps1 -Channel wa-2 -Action Find -Query pago
```

Para abrir el primer resultado visible:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-navigator.ps1 -Channel wa-2 -Action OpenFirst -Query pago -Execute
```

## Puente con clasificador

El siguiente nivel ya esta agregado en:

```text
scripts\visual-agent\visual-intent-bridge.ps1
```

En preview consulta la cabina, toma una alerta reciente de `pago`, `deuda` o `precio`, busca el chat visible por OCR y devuelve el candidato sin hacer clic:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-intent-bridge.ps1
```

Para que abra el chat encontrado, espere, capture la conversacion y actualice la nube:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-intent-bridge.ps1 -Execute -CaptureAfterOpen -Send
```

El launcher tambien tiene el boton **Atender alerta**, que ejecuta este flujo minimizando la ventana primero.

## Aprendizaje de chats

Para que la IA aprenda clientes, servicios y contabilidad con mas contexto, existe una pasada que abre chats visibles y captura la conversacion abierta:

```text
scripts\visual-agent\visual-chat-learning-pass.ps1
```

Preview sin clics:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-learning-pass.ps1
```

Ejecucion real con envio a nube:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-learning-pass.ps1 -Execute -Send
```

El capturador marca el chat abierto como `opened_chat`, usando el nombre de la fila que el mouse abrio. Asi la nube puede agrupar mejor lo aprendido por cliente/chat en vez de dejar todo como pantalla generica.

## Prueba sin enviar a nube

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-screen-capture.ps1
```

Esto crea capturas y un JSON de vista previa en:

```text
scripts\visual-agent\captures-dryrun
```

Por defecto el capturador recorta cada tercio para leer la zona de conversacion y evitar lista de chats, barras y pestanas. Si necesitas diagnosticar una seccion completa:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-screen-capture.ps1 -FullSection
```

Si una seccion parece ser Codex, Railway, YouTube u otra pagina, el capturador la omite para no enviar ruido a la nube.

## Enviar una lectura real a ariadgsm.com

Solo hacerlo cuando los 3 WhatsApp esten visibles, uno por seccion de pantalla:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-screen-capture.ps1 -Send
```

El script escribe eventos en `cloud-inbox` y luego ejecuta `visual-agent.js --once`.

## Modo observacion continua

Cuando la lectura este limpia:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-screen-capture.ps1 -Send -Watch -PollSeconds 60
```

## Como ayudar antes de enviar

1. Deja visibles solo los 3 WhatsApp que se deben observar.
2. Acomoda cada uno en una tercera parte del monitor.
3. Evita dejar Railway, YouTube u otras paginas encima de las secciones.
4. Abre chats reales o listas que quieras que el agente aprenda.
5. Ejecuta primero sin `-Send` y revisa que el JSON no tenga demasiado ruido.
