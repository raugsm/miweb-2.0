# AriadGSM Agent Desktop

Aplicacion local para controlar el observador visual de los 3 WhatsApp sin escribir comandos.

## Abrir el launcher

Ejecuta:

```text
AriadGSM-Agent.cmd
```

El launcher permite:

- iniciar el modo vivo;
- iniciar el observador continuo;
- detenerlo;
- hacer una lectura una vez;
- atender la primera alerta clasificada de la cabina;
- hacer una pasada de aprendizaje abriendo chats visibles;
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
5. Puede tomar una alerta de pago/deuda/precio, buscar el chat visible, abrirlo y volver a capturar.
6. Puede recorrer chats visibles para aprender clientes, servicios y contexto contable.
7. Puede correr en modo vivo, coordinando captura rapida y alertas en ciclos.

Todavia no escribe mensajes ni envia respuestas a clientes.

## Modo Vivo

El boton **Modo vivo** inicia un proceso local oculto:

```text
scripts\visual-agent\agent-local.py
```

El flujo del modo vivo es:

1. respeta los 3 WhatsApp que ya dejaste alineados;
2. ejecuta el motor local Python `scripts/visual-agent/agent-local.py`;
3. captura pantalla y lee OCR;
4. toma una decision local rapida sobre pago/deuda/precio sin esperar a la nube;
5. si encuentra una accion, busca el chat visible y lo abre;
6. sube eventos a `ariadgsm.com` para historial y cabina;
7. evita aprendizaje profundo y scroll largo durante la atencion en vivo;
8. repite el ciclo cada 3 a 5 segundos por defecto.

Por seguridad, el nivel actual sigue siendo de lectura. El modo vivo puede mover el mouse y abrir chats, pero no escribe ni envia mensajes.

Por defecto, el boton **Modo vivo** no abre una pestana nueva de Chrome ni reacomoda ventanas. Las opciones `-OpenWhatsApp` y `-ArrangeWindows` quedan solo para pruebas manuales.

El aprendizaje profundo excluye grupos contables repetitivos como `Pagos Mexico`, `Pagos Chile` y `Pagos Colombia`. Pueden aparecer como señales contables en capturas normales, pero el mouse no los abre para entrenar estilo de cliente/servicio.

La lectura base tambien filtra esas filas de pagos cuando aparecen como texto de pantalla, para que no disparen alertas falsas ni contaminen el aprendizaje como si fueran chats de clientes.

El modo vivo no hace scroll de aprendizaje. Cuando necesitas entrenar la IA con historial, usa **Aprender chats**: abre la conversacion, toma una lectura, sube el scroll para buscar mensajes anteriores y repite la lectura de forma controlada.

La ventana principal muestra una seccion **Que paso**. Ahi explica si el modo vivo detecto algo localmente, que texto uso, que busquedas intento y por que no movio el mouse cuando no encontro una fila visible.

La decision local usa reglas rapidas en Python primero para no perder velocidad. Si esta PC tiene `OPENAI_API_KEY` configurada, el modo vivo tambien puede pedir una decision OpenAI directa sobre el OCR reciente cuando las reglas no ven una accion clara, sin esperar a que la nube procese la captura.

PowerShell queda como lanzador y puente de Windows. El ciclo vivo, el estado y la decision principal ya viven en Python.

## Visual Debugger

El boton **Ver ojos** genera un reporte local HTML con:

- la captura de cada seccion `wa-1`, `wa-2`, `wa-3`;
- lineas OCR aceptadas como mensaje util;
- lineas ignoradas y el motivo del filtro;
- secciones bloqueadas por no parecer WhatsApp;
- decision local que tomaria el motor Python.

El reporte queda en:

```text
scripts\visual-agent\runtime\visual-debugger\latest.html
```

Tambien puedes ejecutarlo manualmente:

```powershell
python .\scripts\visual-agent\visual-debugger.py --open
```

## Ojo Vivo

El boton **Ojo vivo** inicia un observador continuo distinto al capturador puntual. Este modo:

- captura pantalla en modo vivo rapido cada 100ms por defecto;
- divide el monitor en las 3 zonas de WhatsApp;
- calcula diferencia visual por region;
- ejecuta OCR solo cuando una zona cambia y lo procesa en segundo plano para no frenar la captura;
- guarda una caja negra visual local en `D:\AriadGSM\vision-buffer` cuando existe la unidad D:;
- guarda un buffer reciente de eventos visuales;
- acepta una region solo si encuentra firma visual de WhatsApp (`Escribe un mensaje`, buscador de chats, pestañas de lista o avisos propios de WhatsApp);
- marca cualquier region sin firma de WhatsApp como `no_whatsapp_signature`, aunque sea Codex, navegador u otro programa;
- deja un reporte vivo en `scripts\visual-agent\runtime\eyes-stream\latest.html`.

Este modo no usa DOM ni lee sesiones internas del navegador. Es vision local por streaming visual.

Prueba manual corta:

```powershell
python .\scripts\visual-agent\eyes-stream.py --duration-seconds 8
```

Para probar el modo mas cercano a tiempo real:

```powershell
python .\scripts\visual-agent\eyes-stream.py --duration-seconds 8 --live
```

El historial visual completo queda local en disco y rota por defecto con limite de 7 dias o 500GB. Se puede cambiar con `--retention-hours`, `--max-storage-gb` o `--vision-storage-dir`.

El historial de aprendizaje queda limitado a 1 mes de anterioridad. El capturador detecta fechas de WhatsApp como `Hoy`, `Ayer`, dias de la semana, `15/04/2026` o `15 de abril`; esas fechas se usan solo como metadatos para detener el scroll, no se guardan como mensajes de cliente.

Para una prueba sin mover mouse ni enviar datos:

```powershell
python .\scripts\visual-agent\agent-local.py --mode Live --max-cycles 1
```

Para correr un ciclo real manual:

```powershell
python .\scripts\visual-agent\agent-local.py --mode Live --max-cycles 1 --execute --send
```

El boton **Detener** apaga tanto el observador como el modo vivo.

## Mouse y lectura de conversaciones

El agente ya tiene dos niveles de control de puntero.

Control simple por posicion:

```text
scripts\visual-agent\visual-pointer-control.ps1
```

Control con OCR y coordenadas de texto:

```text
scripts\visual-agent\visual-chat-navigator.ps1
```

Por seguridad, los scripts no mueven nada en modo preview/lista. Para hacer clic necesitan `-Execute`.

Ejemplo para abrir una fila visible en el WhatsApp central:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-pointer-control.ps1 -Channel wa-2 -Action OpenChatRow -RowRatio 0.28 -Execute
```

Ejemplo para listar textos visibles en la lista de chats del WhatsApp central:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-navigator.ps1 -Channel wa-2 -Action List
```

Ejemplo para buscar una fila visible que contenga `pago`:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-navigator.ps1 -Channel wa-2 -Action Find -Query pago
```

Ejemplo para abrir el primer resultado visible:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-navigator.ps1 -Channel wa-2 -Action OpenFirst -Query pago -Execute
```

Si Codex u otra ventana esta encima, el navegador leera esa ventana. Antes de usarlo, deja visible el WhatsApp correspondiente.

## Puente clasificador -> chat visible

El puente local ya conecta la cabina con el navegador por OCR:

```text
scripts\visual-agent\visual-intent-bridge.ps1
```

En modo preview consulta `ariadgsm.com`, toma la primera alerta de `pago`, `deuda` o `precio`, busca una fila visible en el WhatsApp correspondiente y devuelve coordenadas sin mover el mouse:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-intent-bridge.ps1
```

Para ejecutar el flujo completo:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-intent-bridge.ps1 -Execute -CaptureAfterOpen -Send
```

Ese modo hace esto:

1. lee la cabina en la nube;
2. elige una alerta de `payment_or_receipt`, `accounting_debt` o `price_request`;
3. busca el chat visible por texto;
4. abre la fila encontrada;
5. espera unos segundos;
6. captura los 3 WhatsApp y actualiza la nube.

Antes de hacer clic, tambien descarta filas de grupos de pagos como `Pagos Mexico`, `Pagos Chile` o `Pago Colombia`. Si la palabra generica `pago` solo encuentra esos grupos, el puente no abre nada y espera una coincidencia mejor.

El boton **Atender alerta** del launcher ejecuta ese mismo flujo y minimiza la ventana antes de buscar.

## Pasada de aprendizaje de chats

Para aprender conversaciones completas, el agente tiene una pasada que lista chats visibles, abre algunos por cada WhatsApp, captura la conversacion abierta y envia la lectura a la nube con el nombre del chat:

```text
scripts\visual-agent\visual-chat-learning-pass.ps1
```

Preview sin mover el mouse:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-learning-pass.ps1
```

Ejecucion real:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-chat-learning-pass.ps1 -Execute -Send
```

El boton **Aprender chats** del launcher ejecuta esta pasada con maximo 2 chats por WhatsApp y 40 lineas por chat. Sigue siendo modo lectura: no escribe ni envia mensajes.

Por defecto esa pasada abre cada chat seleccionado, captura la vista actual, sube hasta 5 paginas de scroll y se detiene si cruza el limite de 1 mes. Antes de hacer clic, revisa toda la fila OCR para evitar grupos repetitivos como `Pagos Mexico`, aunque el candidato elegido sea una vista previa de esa fila. Cada pagina enviada mantiene el mismo `conversationId`, para que la nube agrupe todo como una misma conversacion.

## Logs

Los logs se guardan en:

```text
scripts\visual-agent\runtime
```

Esa carpeta tambien esta ignorada por Git.
