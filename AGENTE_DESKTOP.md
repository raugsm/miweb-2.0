# AriadGSM Agent Desktop

Aplicacion local para controlar el observador visual de los 3 WhatsApp sin escribir comandos.

## Abrir el launcher

Ejecuta:

```text
AriadGSM-Agent.cmd
```

El launcher permite:

- iniciar el modo autopiloto;
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
7. Puede correr en modo autopiloto, coordinando captura, alertas y aprendizaje en ciclos.

Todavia no escribe mensajes ni envia respuestas a clientes.

## Modo Autopiloto

El boton **Autopiloto** inicia un proceso local oculto:

```text
scripts\visual-agent\visual-autopilot.ps1
```

El flujo del autopiloto es:

1. respeta los 3 WhatsApp que ya dejaste alineados;
2. captura pantalla y sube eventos a `ariadgsm.com`;
3. revisa la cabina para atender alertas de pago/deuda/precio;
4. hace aprendizaje profundo solo cada ciertos ciclos;
5. repite el ciclo cada 30 segundos por defecto.

Por seguridad, el nivel actual sigue siendo de lectura. El autopiloto puede mover el mouse y abrir chats, pero no escribe ni envia mensajes.

Por defecto, el boton **Autopiloto** ya no abre una pestana nueva de Chrome ni reacomoda ventanas. Las opciones `-OpenWhatsApp` y `-ArrangeWindows` quedan solo para pruebas manuales.

El aprendizaje profundo excluye grupos contables repetitivos como `Pagos Mexico`, `Pagos Chile` y `Pagos Colombia`. Pueden aparecer como señales contables en capturas normales, pero el mouse no los abre para entrenar estilo de cliente/servicio.

La lectura base tambien filtra esas filas de pagos cuando aparecen como texto de pantalla, para que no disparen alertas falsas ni contaminen el aprendizaje como si fueran chats de clientes.

Cuando el autopiloto aprende de un chat, ya no captura solo lo visible. Abre la conversacion, toma una lectura, sube el scroll para buscar mensajes anteriores y repite la lectura de forma controlada. El boton **Autopiloto** aprende desde el primer ciclo y luego usa pasadas cortas para no perder velocidad en vivo; el boton **Aprender chats** hace una pasada mas profunda.

El historial de aprendizaje queda limitado a 1 mes de anterioridad. El capturador detecta fechas de WhatsApp como `Hoy`, `Ayer`, dias de la semana, `15/04/2026` o `15 de abril`; esas fechas se usan solo como metadatos para detener el scroll, no se guardan como mensajes de cliente.

Para una prueba sin mover mouse ni enviar datos:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-autopilot.ps1
```

Para correr un ciclo real manual:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\visual-agent\visual-autopilot.ps1 -Execute -Send
```

El boton **Detener** apaga tanto el observador como el autopiloto.

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
