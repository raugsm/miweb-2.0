# AriadGSM Autonomous Operating System 1.0

Documento base para construir una inteligencia artificial operativa real para AriadGSM.

Fecha: 2026-04-27
Estado: arquitectura propuesta, sin codigo nuevo
Version objetivo inicial: 0.7.x

Nota 2026-04-27:

El plano maestro de producto final ahora vive en:

```text
docs/ARIADGSM_FINAL_PRODUCT_BLUEPRINT.md
```

Este documento queda como base historica y tecnica. El blueprint final manda cuando haya conflicto de criterio, especialmente en experiencia de producto, Trust & Safety, Cabin Authority, Input Arbiter, memoria viva y prohibicion de parches.

## 1. Proposito

AriadGSM no debe terminar siendo un bot con muchas reglas. El objetivo es una IA operativa local capaz de observar el trabajo diario, entender el negocio, aprender de conversaciones, decidir acciones, usar herramientas disponibles, verificar resultados y reportar lo que hizo.

El operador debe poder:

1. Abrir el programa.
2. Iniciar sesion.
3. Presionar iniciar.
4. Ver que la IA prepara su cabina.
5. Dejar que lea, aprenda, priorice, atienda, negocie, procese trabajos y mantenga contabilidad.
6. Intervenir cuando sea necesario sin pelear contra el mouse o teclado.

La IA debe actuar como un operador asistente, no como una macro fija.

## 2. Diferencia entre bot e IA operativa

### Bot

Un bot ejecuta instrucciones fijas:

- Si ve palabra "precio", abre chat.
- Si detecta "pago", crea registro.
- Si falla USB Redirector, queda bloqueado.
- Si aparece una herramienta nueva, hay que modificar codigo.

Eso no sirve para AriadGSM a largo plazo porque el negocio cambia por cliente, pais, herramienta, metodo, version, cable, error, cuenta, proveedor y urgencia.

### IA operativa

Una IA operativa usa un ciclo de razonamiento:

```text
Observar -> Entender -> Recordar -> Planear -> Actuar -> Verificar -> Aprender -> Reportar
```

Ejemplo real:

Un cliente pide un servicio Xiaomi. La IA revisa historial, pais, deuda, herramienta usual, disponibilidad de USB Redirector, estado del PC remoto, errores anteriores, precios, instrucciones previas y prioridad. Si USB Redirector se cae, no se bloquea como bot: busca alternativas conocidas, revisa memoria de casos, propone otra herramienta o pide confirmacion humana si la accion tiene riesgo.

La meta es que AriadGSM pueda razonar con herramientas, no depender de que cada herramienta este quemada en codigo.

## 3. Principio central

El codigo no debe contener el negocio completo. El codigo debe contener capacidades.

El negocio debe vivir en memoria, conocimiento, politicas, ejemplos y herramientas registradas.

```text
Codigo = capacidades estables
Memoria = conocimiento del negocio
Herramientas = acciones disponibles
Cerebro = razonamiento y seleccion de estrategia
Humano = autoridad final en acciones sensibles
```

## 4. Problema actual

El sistema actual ya tiene piezas valiosas: login, updater, motores, lectura, memoria, manos, supervisor y panel. Pero todavia se comporta parcialmente como bot porque:

1. La cabina no tiene una sola autoridad de verdad.
2. Vision, Perception, Cabin Manager y Hands pueden contradecirse.
3. La lectura depende demasiado de pantalla/OCR.
4. Las herramientas del negocio no estan modeladas como capacidades reutilizables.
5. La memoria guarda datos, pero aun no decide estrategias de trabajo.
6. Las manos hacen acciones, pero todavia no tienen verificacion profunda por objetivo.
7. Los errores se tratan como fallos tecnicos, no como experiencia que la IA debe aprender.

## 5. Arquitectura final

Nombre propuesto:

```text
AriadGSM Autonomous Operating System
```

Capas principales:

```text
1. Identity & Trust
2. Managed Cabin Host
3. Perception Fabric
4. Reader Core
5. Business Memory
6. Tool Registry
7. Cognitive Core
8. Planner & Executor
9. Input Arbiter
10. Verification Layer
11. Audit Timeline
12. Cloud Sync
13. Self-Improvement Loop
```

## 6. Identity & Trust

Responsabilidad:

- Login.
- Licencia.
- Version.
- Actualizaciones.
- Permisos.
- Modo seguro.
- Identidad del operador.

Regla:

La IA no arranca acciones autonomas sin sesion autorizada.

No debe iniciar moviendo mouse apenas se abre el programa. Primero login, luego preparacion, luego validacion, luego inicio.

## 7. Managed Cabin Host

Esta es la raiz del problema actual de Edge/Chrome/Firefox.

Debe ser el unico modulo autorizado para:

- Abrir navegadores.
- Adoptar ventanas existentes.
- Registrar procesos.
- Registrar handles de ventana.
- Posicionar cajas.
- Restaurar ventanas.
- Decidir si una cabina esta lista.

Regla obligatoria:

```text
Ningun otro modulo puede abrir, cerrar, mover, restaurar o decidir la identidad de Edge, Chrome o Firefox.
```

Canales:

```text
wa-1 = Edge
wa-2 = Chrome
wa-3 = Firefox
```

Cada canal debe tener:

- processId
- processName
- windowHandle
- browserProfile
- boxId
- expectedUrl
- status
- lastVerifiedAt
- confidence
- reason

Estados permitidos:

```text
READY
COVERED
WRONG_PAGE
NOT_LOGGED_IN
NEEDS_QR
MINIMIZED
MISSING
UNREADABLE
OPERATOR_USING
RECOVERING
FAILED_NEEDS_HUMAN
```

No se permite que otro modulo invente estados paralelos.

## 8. Box Grid

La cabina se divide en cajas.

```text
box-1 = zona izquierda = wa-1 Edge
box-2 = zona central = wa-2 Chrome
box-3 = zona derecha = wa-3 Firefox
```

Las cajas no son un parche visual. Son un contrato espacial.

Reglas:

1. Todo lo leido dentro de box-1 pertenece a wa-1.
2. Todo lo leido dentro de box-2 pertenece a wa-2.
3. Todo lo leido dentro de box-3 pertenece a wa-3.
4. Lo que ocurre fuera de las cajas no entra al aprendizaje de WhatsApp.
5. Si una caja esta tapada, se reporta. No se adivina.
6. Las manos solo pueden clicar dentro de la caja autorizada.

## 9. Perception Fabric

No debe depender de una sola forma de ver.

Orden de lectura recomendado:

```text
1. Browser Bridge
2. Windows UI Automation
3. Accessibility tree
4. Window capture
5. OCR
6. Vision model verifier
```

El OCR queda como respaldo, no como verdad principal.

Fuentes:

- Microsoft UI Automation permite escuchar eventos y estructura de UI.
- Windows.Graphics.Capture permite capturar ventana o pantalla.
- Browser automation permite leer estructura del DOM cuando el navegador lo permite.

## 10. Browser Bridge

Para no vivir pegados al OCR, cada navegador debe tener un puente.

Chrome y Edge:

- CDP cuando sea posible.
- Playwright o conexion DevTools con perfil aislado.
- Perfil dedicado con user-data-dir separado.

Firefox:

- WebDriver BiDi.
- Accessibility fallback si BiDi no da suficiente informacion.

Regla:

El Browser Bridge no debe robar cookies ni exfiltrar sesiones. Solo lee contenido visible o estructura necesaria para operar la cabina local.

## 11. Reader Core

Reader Core convierte cualquier fuente en objetos de negocio.

Entrada:

- DOM.
- Accesibilidad.
- OCR.
- Vision.
- Eventos de navegador.

Salida canonica:

```json
{
  "channelId": "wa-2",
  "boxId": "box-2",
  "conversationId": "wa-2-abc123",
  "conversationTitle": "Cliente Peru",
  "messageId": "msg-xyz",
  "sender": "client|business|unknown",
  "text": "bro cuanto sale?",
  "language": "es-PE",
  "countryHint": "Peru",
  "timestamp": "2026-04-27T20:30:00Z",
  "source": "dom|uia|ocr|vision",
  "confidence": 0.94
}
```

Reader Core no decide acciones. Solo entrega lectura confiable.

## 12. Business Memory

La memoria debe dejar de ser solo almacenamiento.

Debe guardar:

- Clientes.
- Alias.
- Telefonos.
- Pais.
- Idioma y jerga.
- Servicios consultados.
- Servicios comprados.
- Precios ofrecidos.
- Deudas.
- Pagos.
- Comprobantes.
- Herramientas usadas.
- Errores frecuentes.
- Respuestas que funcionaron.
- Proveedores.
- Casos cerrados.
- Casos pendientes.

Modelo de memoria:

```text
Event Log -> Timeline por cliente -> Perfil de cliente -> Conocimiento de negocio -> Indices de busqueda
```

Almacenamiento recomendado:

- SQLite WAL para eventos locales rapidos y confiables.
- Base cloud para respaldo y panel.
- Vector/hybrid search para buscar casos similares, jergas y procedimientos.

## 13. Tool Registry

Esta es la pieza que evita convertirlo en bot.

En vez de programar:

```text
Si Xiaomi entonces USB Redirector
```

Se registra una herramienta:

```json
{
  "toolId": "usb_redirector",
  "name": "USB Redirector",
  "category": "remote_usb",
  "capabilities": ["connect_usb", "disconnect_usb", "check_status"],
  "risks": ["disconnect_client_device", "paid_session_loss"],
  "requirements": ["app_open", "client_id", "operator_confirmation_for_disconnect"],
  "fallbacks": ["alternative_remote_usb", "manual_instruction"],
  "status": "available"
}
```

La IA no necesita que el codigo conozca cada caso. Necesita saber que herramientas existen, que pueden hacer, que riesgos tienen y como verificar resultado.

Herramientas iniciales del negocio:

- WhatsApp Web.
- ariadgsm.com panel.
- USB Redirector.
- DFT Pro.
- Herramientas Xiaomi.
- Herramientas Samsung.
- Herramientas Motorola.
- Carpetas de archivos.
- Navegador.
- Calculadora/contabilidad.
- Notas internas.
- Telegram si aplica.
- Busqueda web controlada si aplica.

Cada herramienta debe tener:

- Capacidades.
- Entradas requeridas.
- Salidas esperadas.
- Riesgos.
- Verificacion.
- Fallback.
- Nivel de autonomia permitido.

## 14. Cognitive Core

Aqui vive la IA real.

Responsabilidades:

- Entender conversaciones.
- Detectar intencion.
- Relacionar con memoria.
- Priorizar.
- Elegir herramienta.
- Crear plan.
- Pedir confirmacion si hay riesgo.
- Aprender del resultado.

No debe ejecutar directamente. Debe proponer planes verificables.

Ejemplo:

```text
Entrada:
Cliente: "bro tengo xiaomi redmi note, no pasa cuenta, cuanto?"

Razonamiento:
- Intencion: consulta precio servicio Xiaomi.
- Pais: probable Peru por historial.
- Riesgo: requiere identificar modelo y metodo.
- Memoria: clientes similares pagaron X.
- Herramientas: DFT Pro, USB Redirector, panel precios.
- Proxima accion: revisar historial y responder borrador.

Salida:
Plan con pasos, confianza y necesidad de humano.
```

## 15. Planner & Executor

Planner convierte la decision en pasos.

Executor ejecuta solo si:

- Cabin Host valida la cabina.
- Input Arbiter autoriza mouse/teclado.
- Tool Registry dice que la herramienta esta disponible.
- Risk Policy permite autonomia.
- Verification Layer sabe como comprobar resultado.

Ejemplo de plan:

```text
1. Abrir chat del cliente en wa-2.
2. Leer ultimos 30 dias.
3. Buscar si tiene deuda.
4. Revisar precio recomendado.
5. Crear borrador de respuesta.
6. Esperar confirmacion humana si la respuesta implica promesa de plazo o precio especial.
```

## 16. Input Arbiter

El humano siempre tiene prioridad.

Reglas:

1. Si el operador mueve mouse o teclado, la IA cede.
2. No se apaga toda la IA. Solo pausa manos.
3. La IA puede seguir leyendo si no interfiere.
4. Si necesita controlar mouse, pide lease temporal.
5. Si pierde lease, cancela accion segura y reporta.

Estados:

```text
AI_CONTROL
OPERATOR_CONTROL
WAITING_FOR_IDLE
ACTION_BLOCKED
ACTION_VERIFIED
```

## 17. Verification Layer

Toda accion debe verificarse.

No basta con "hice click".

Debe comprobar:

- Abri el chat correcto.
- El titulo corresponde.
- La caja correcta sigue activa.
- La conversacion cambio como esperaba.
- El mensaje leido pertenece al cliente correcto.
- La herramienta respondio como se esperaba.
- El pago quedó registrado.

Sin verificacion, no hay autonomia real.

## 18. Audit Timeline

Debe existir una historia completa de la IA.

Cada evento debe registrar:

- Que vio.
- Que entendio.
- Que recordo.
- Que decidio.
- Que herramienta eligio.
- Que hizo.
- Que verifico.
- Que fallo.
- Que aprendio.
- Que necesita del operador.

Esto permite corregir el sistema sin adivinar.

## 19. Cloud Sync

ariadgsm.com no debe recibir basura cruda.

Debe recibir:

- Clientes.
- Resumen de conversaciones.
- Eventos contables.
- Pagos detectados.
- Deudas.
- Casos abiertos.
- Casos cerrados.
- Alertas.
- Aprendizajes aprobados.
- Reportes.
- Backups.

La nube es panel, respaldo y memoria compartida. La decision rapida debe ser local.

## 20. Self-Improvement Loop

La IA no debe modificarse el codigo sola.

Debe automejorarse en estas capas:

- Nuevos ejemplos.
- Nuevas respuestas.
- Nuevas jergas.
- Nuevos casos.
- Nuevas reglas de negocio aprobadas.
- Nuevos procedimientos.
- Nuevas herramientas registradas.
- Nuevos fallbacks.

El codigo se actualiza por versiones, pruebas y updater.

Flujo:

```text
Fallo -> Diagnostico -> Hipotesis -> Solucion candidata -> Prueba -> Aprobacion -> Version nueva
```

## 21. Niveles de autonomia

### Nivel 0: Observador

Lee y registra. No actua.

### Nivel 1: Asistente

Resume, clasifica y recomienda.

### Nivel 2: Borrador

Propone respuestas y acciones, pero no ejecuta sin humano.

### Nivel 3: Accion segura

Puede abrir chats, leer, registrar contabilidad y preparar borradores.

### Nivel 4: Operador supervisado

Puede usar herramientas con bajo riesgo, cambiar entre chats, buscar datos, preparar trabajos y pedir confirmacion en puntos sensibles.

### Nivel 5: Operador autonomo limitado

Puede completar flujos completos dentro de politicas aprobadas, con auditoria y rollback.

Meta inicial realista:

```text
Llegar a Nivel 4 primero.
```

Nivel 5 se habilita solo por areas especificas.

## 22. Politica de riesgo

Acciones de bajo riesgo:

- Leer.
- Clasificar.
- Resumir.
- Abrir chat.
- Crear borrador.
- Registrar evento contable como borrador.

Acciones de riesgo medio:

- Enviar respuesta.
- Cambiar estado de un trabajo.
- Registrar pago confirmado.
- Abrir herramienta externa.

Acciones de alto riesgo:

- Ejecutar flasheo.
- Desconectar USB.
- Tomar control prolongado del mouse.
- Borrar archivos.
- Cambiar configuracion de herramientas.
- Prometer entrega o precio especial.

Regla:

Toda accion de alto riesgo requiere confirmacion humana hasta que exista suficiente historial probado.

## 23. WhatsApp: dos caminos

### Camino local actual

Usa WhatsApp Web en 3 navegadores.

Ventaja:

- Funciona con tus sesiones actuales.
- Permite observar como trabajas hoy.
- Sirve para aprendizaje inicial.

Desventaja:

- Depende de ventana, foco, UI, cambios visuales y WhatsApp Web.

### Camino oficial futuro

WhatsApp Business Platform / Cloud API con webhooks.

Ventaja:

- Mensajes entrantes por eventos.
- Mejor velocidad.
- Menos dependencia de pantalla.
- Mejor integracion con nube.

Desventaja:

- Requiere configuracion Meta.
- Puede tener restricciones.
- No siempre reemplaza el flujo actual de tres WhatsApps.

Conclusion:

El sistema debe soportar ambos:

```text
Local Cabin para operar hoy
Cloud/API para evolucionar a eventos oficiales
```

## 24. Pruebas de aceptacion

No se debe declarar "listo" hasta pasar:

1. La cabina corre 1 hora sin perder wa-1, wa-2, wa-3.
2. Edge, Chrome y Firefox no se cierran ni se duplican.
3. Cada caja conserva identidad aunque haya foco o ventanas encima.
4. Si una caja se tapa, la IA reporta y no inventa.
5. Reader Core extrae mensajes con canal correcto.
6. La IA abre chats correctos y verifica titulo.
7. El operador puede mover mouse y la IA cede sin apagarse.
8. La memoria crea perfiles de cliente utiles.
9. La contabilidad crea borradores verificables.
10. El panel explica en lenguaje humano que esta haciendo.
11. Todo queda en Audit Timeline.
12. Los errores generan aprendizaje o diagnostico, no parches ciegos.

## 25. Que se debe eliminar o reducir

- Reglas duplicadas de identidad en varios modulos.
- Filtros infinitos de OCR como solucion principal.
- Acciones de manos sin verificacion.
- Logs tecnicos como unica explicacion al operador.
- Automatizacion que abre otra ventana si ya existe una sesion.
- Cualquier codigo que permita a modulos secundarios cerrar navegadores.

## 26. Roadmap recomendado

### Fase 1: Congelar arquitectura

Este documento.

Resultado esperado:

- Todos sabemos que se construye y que no.

### Fase 2: Managed Cabin Host real

Resultado esperado:

- Un solo modulo gobierna Edge/Chrome/Firefox.
- Tres cajas fijas.
- Estado unico de cabina.
- Prohibicion estructural de cerrar navegadores desde otros modulos.

### Fase 3: Reader Core por caja

Resultado esperado:

- Cada caja produce mensajes canonicos.
- OCR queda como fallback.
- Se eliminan contradicciones 3/3 vs 2/3.

### Fase 4: Tool Registry

Resultado esperado:

- Las herramientas del negocio se registran como capacidades.
- La IA puede elegir entre herramientas sin cambiar codigo por cada caso.

### Fase 5: Cognitive Core nivel 4

Resultado esperado:

- La IA razona con memoria, herramientas y politicas de riesgo.
- Puede crear planes y ejecutar acciones seguras.

### Fase 6: Business Memory y Accounting real

Resultado esperado:

- Clientes, pagos, deudas, servicios y reportes se consolidan.

### Fase 7: Cloud Sync completo

Resultado esperado:

- ariadgsm.com se convierte en panel, respaldo y centro de reportes.

### Fase 8: Automejora controlada

Resultado esperado:

- La IA aprende procedimientos y fallos sin tocar codigo directamente.

## 27. Respuesta directa a la duda principal

Si seguimos solo con reglas, sera un bot.

Si construimos capacidades, memoria, herramientas registradas, razonamiento, verificacion y aprendizaje, entonces AriadGSM puede convertirse en una IA operativa real.

La diferencia esta en esto:

```text
Bot: el programador sabe todos los pasos.
IA operativa: el sistema entiende el objetivo, conoce herramientas, recuerda casos, elige estrategia, actua con limites y aprende del resultado.
```

## 28. Fuentes tecnicas base

- Microsoft UI Automation Events: https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-overview
- Microsoft Power Automate UI Elements: https://learn.microsoft.com/en-us/power-automate/desktop-flows/ui-elements
- Windows.Graphics.Capture: https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture
- Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/
- Chrome remote debugging and user data directories: https://developer.chrome.com/blog/remote-debugging-port
- Playwright Browser Contexts: https://playwright.dev/docs/browser-contexts
- WebDriver BiDi reference: https://developer.mozilla.org/en-US/docs/Web/WebDriver/Reference/BiDi
- W3C WebDriver BiDi draft: https://www.w3.org/TR/webdriver-bidi/
- OpenAI Computer Use: https://platform.openai.com/docs/guides/tools-computer-use/
- OpenAI Agents SDK Guardrails: https://openai.github.io/openai-agents-python/guardrails/
- OpenAI Agents SDK Tracing: https://openai.github.io/openai-agents-python/tracing/
- SQLite WAL: https://www.sqlite.org/wal.html
- Qdrant Search: https://qdrant.tech/documentation/search/
- PaddleOCR multilingual OCR: https://www.paddleocr.ai/main/en/index.html

## 29. Decision

La siguiente implementacion no debe ser "otro parche".

La siguiente implementacion debe empezar por:

```text
Managed Cabin Host + Box Grid + Estado Unico de Cabina
```

Sin eso, la IA seguira perdiendo Edge, Chrome o Firefox y todo lo demas sera inestable.
