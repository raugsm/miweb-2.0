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

## Modo vivo

El modo vivo coordina las piezas locales en ciclos rapidos:

```text
scripts\visual-agent\agent-local.py
```

En cada ciclo el motor Python respeta los WhatsApp que ya dejaste alineados, captura pantalla, toma una decision local rapida sobre pago/deuda/precio y atiende alertas clasificadas. No hace aprendizaje profundo ni scroll largo mientras trabaja en vivo. No escribe mensajes al cliente.

La decision local usa la lectura reciente del Reader Core, antes de esperar la ida y vuelta con la nube. Primero aplica reglas rapidas en `agent-local.py`; si `OPENAI_API_KEY` existe en esta PC, puede usar OpenAI como segundo escalon cuando no hay senal clara. Si detecta algo pero no encuentra una fila visible para abrir, el launcher lo muestra en **Que paso** con el texto y las busquedas intentadas.

El visual debugger se abre desde **Ver ojos** y deja un reporte en `scripts\visual-agent\runtime\visual-debugger\latest.html`. Ese reporte muestra cada captura, las lineas aceptadas, las ignoradas y el motivo exacto del filtro.

El modo **Ojo vivo** usa `eyes-stream.py`: mira la pantalla en modo rapido cada 100ms por defecto, compara cambios por region y corre OCR en segundo plano solo cuando algo se mueve. Antes del OCR prepara una version mejorada del recorte con escala 2x, contraste y nitidez. El lector puede probar varios idiomas OCR instalados, por defecto `es-MX`, `en-US` y `pt-BR`. Tambien guarda una caja negra visual local comprimida en `D:\AriadGSM\vision-buffer` cuando la unidad D: existe, con rotacion por antiguedad y espacio. Su reporte queda en `scripts\visual-agent\runtime\eyes-stream\latest.html`.

El aprendizaje visible queda en `scripts\visual-agent\runtime\learning-ledger\latest.html`. Ahi se revisa que mensajes fueron aprendidos como contexto, precio, pago o deuda antes de confiar en la contabilidad automatica.

Ese aprendizaje tambien registra idioma probable, pais sugerido y jerga detectada para que la IA pueda adaptar interpretacion por region.

El Reader Core queda como capa de precision: si existe lectura estructurada reciente de WhatsApp Web, se procesa directo antes que OCR. OCR queda como respaldo visual cuando no hay DOM/accesibilidad disponible.

El boton **Modo vivo** no abre una pestana nueva de Chrome ni reacomoda ventanas por defecto. `-OpenWhatsApp` y `-ArrangeWindows` quedan solo para pruebas manuales.

El aprendizaje profundo no abre grupos como `Pagos Mexico`, `Pagos Chile` o `Pagos Colombia`, porque aportan poco al estilo de clientes y servicios. Se dejan fuera por reglas configurables en `autopilot.skipLearningChats`.

El aprendizaje de conversaciones queda separado en el boton **Aprender chats**. Esa pasada abre el chat, captura la vista actual, sube el scroll para leer paginas anteriores y corta el barrido al llegar al limite de 1 mes.

Preview seguro:

```powershell
python .\scripts\visual-agent\agent-local.py --mode Live --max-cycles 1
```

Ciclo real:

```powershell
python .\scripts\visual-agent\agent-local.py --mode Live --max-cycles 1 --execute --send
```

El launcher tiene el boton **Modo vivo** para dejarlo corriendo en segundo plano. **Detener** lo apaga.

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

El puente no abre grupos de pagos cuando la busqueda es generica. Si `pago` devuelve solo `Pagos Mexico`, `Pagos Chile` o `Pago Colombia`, los marca como omitidos y no mueve el mouse.

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

Durante esta pasada, las fechas de WhatsApp (`Hoy`, `Ayer`, dias de la semana o fechas numericas/textuales) se conservan como metadatos locales para saber cuando parar. No se envian como mensajes utiles. Si una pagina ya esta completamente fuera del ultimo mes, no se sube a la nube.

El filtro de grupos de pagos se aplica sobre toda la fila OCR antes de hacer clic. Esto evita que el agente abra `Pagos Mexico` si el OCR selecciona una linea de vista previa dentro de esa misma fila.

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

El capturador usa regla positiva: solo acepta una seccion si contiene firma visual de WhatsApp (`Escribe un mensaje`, buscador de chats, pestañas de lista o avisos propios de WhatsApp). Cualquier seccion sin esa firma se omite como `no_whatsapp_signature`, aunque sea Codex, Railway, YouTube u otro programa.

La lectura base tambien descarta grupos repetitivos de pagos (`Pagos Mexico`, `Pagos Chile`, `Pago Colombia`, `Pagos Peru`) aunque el OCR los lea con acentos, puntos o letras imperfectas. Esos grupos pueden existir en pantalla, pero no deben contaminar el aprendizaje ni las alertas como si fueran clientes.

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
