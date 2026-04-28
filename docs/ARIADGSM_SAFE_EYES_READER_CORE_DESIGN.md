# AriadGSM Safe Eyes / Reader Core

Version: 0.8.11
Estado: cerrado como base Reader Core
Etapa: 8

## Objetivo

Reader Core convierte lo visible de WhatsApp Web en mensajes estructurados antes
de que Timeline, Case Manager, Accounting, Memory o Business Brain razonen.

La regla central es positiva:

```text
Solo leo fuentes que prueban ser WhatsApp Web real en Edge, Chrome o Firefox.
Todo lo demas se rechaza por diseno.
```

Esto evita el patron anterior de filtros infinitos contra Codex, YouTube,
Railway, titulos del navegador o textos sueltos de OCR.

## Investigacion usada

La arquitectura se apoya en fuentes de plataforma, no en intuicion:

- Microsoft UI Automation define un arbol de elementos con vistas `Raw`,
  `Control` y `Content`, propiedades como nombre/control type, y eventos de
  cambios de UI. Fuente:
  https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification
- Microsoft UI Automation Control Patterns incluye `Text`, `Invoke`,
  `Scroll`, `Selection` y patrones usados para leer o interactuar con controles
  sin depender de pixeles. Fuente:
  https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview
- Chrome DevTools Protocol Accessibility expone metodos como
  `getFullAXTree`, `queryAXTree` y eventos de actualizacion del arbol de
  accesibilidad. Fuente:
  https://chromedevtools.github.io/devtools-protocol/tot/Accessibility/
- Chrome DevTools Protocol DOMSnapshot puede capturar nodos, atributos,
  layout, texto y bounds del documento renderizado. Fuente:
  https://chromedevtools.github.io/devtools-protocol/tot/DOMSnapshot/
- W3C Accessible Name and Description explica que los navegadores crean un
  arbol de accesibilidad paralelo al DOM, con nombre, descripcion, rol, estados
  y propiedades. Fuente:
  https://www.w3.org/TR/accname-1.2/

Conclusion tecnica: los ojos robustos no deben empezar por imagen. Deben leer
estructura cuando exista y bajar a OCR solo cuando la estructura no alcance.

## Contratos

### Entrada: reader source event

Los adaptadores de Edge, Chrome, Firefox, UI Automation u OCR deben emitir una
forma comun:

```json
{
  "sourceEventId": "chrome-dom-001",
  "sourceKind": "dom",
  "browserProcess": "chrome",
  "url": "https://web.whatsapp.com/",
  "windowTitle": "WhatsApp - Google Chrome",
  "conversationId": "wa-2-cliente-123",
  "conversationTitle": "Cliente Mexico",
  "messages": [
    {
      "messageKey": "wamid-1",
      "text": "Cuanto vale liberar Samsung?",
      "direction": "client",
      "senderName": "Cliente Mexico",
      "sentAt": "2026-04-27T18:00:00Z",
      "confidence": 0.96
    }
  ]
}
```

Este contrato no guarda cookies, tokens ni sesiones. Solo contenido visible o
contenido estructurado del documento visible.

### Salida: visible message

Archivo:

```text
desktop-agent/contracts/visible-message.schema.json
```

Campos esenciales:

- `channelId`: `wa-1`, `wa-2`, `wa-3`.
- `browserProcess`: `msedge`, `chrome`, `firefox`.
- `conversationId`, `conversationTitle`.
- `direction`: cliente, agente o desconocido.
- `text`, `sentAt`, `senderName`.
- `source.kind`: `dom`, `accessibility`, `uia` u `ocr`.
- `identity`: prueba de que la fuente es WhatsApp Web.
- `confidence`: confianza final despues de comparar fuentes.
- `evidence`: referencia local y texto crudo.
- `sourcesCompared`: fuentes que vieron el mismo mensaje.
- `disagreements`: diferencias entre fuentes.

### Salida: reader core state

Archivo:

```text
desktop-agent/contracts/reader-core-state.schema.json
```

Publica estado humano y tecnico:

- cuantos mensajes acepto;
- cuantos rechazo;
- cuantos vinieron de fuentes estructuradas;
- cuantos fueron OCR de respaldo;
- que desacuerdos encontro;
- que necesita de Bryams.

## Prioridad de fuentes

Orden de verdad:

```text
DOM -> Accesibilidad del navegador -> Windows UI Automation -> OCR
```

Razon:

- DOM y accesibilidad pueden dar texto, rol, nombre, relacion y contexto.
- UI Automation sirve cuando el navegador expone el arbol de controles.
- OCR queda para captura visual de respaldo cuando no hay estructura.

Si OCR y DOM no coinciden, Reader Core acepta la fuente estructurada con mayor
rango y deja un `disagreement` auditable.

## Identidad positiva

Reader Core no pregunta "parece WhatsApp?".

Acepta una fuente solo si:

- el navegador es conocido:
  - Edge -> `wa-1`;
  - Chrome -> `wa-2`;
  - Firefox -> `wa-3`;
- el `url` contiene `web.whatsapp.com`, o la ventana del navegador contiene
  WhatsApp con confianza menor;
- la conversacion no es un titulo generico de navegador.

Rechaza:

- app de WhatsApp instalada en Windows;
- cualquier proceso que no sea Edge, Chrome o Firefox;
- pestañas, titulos o elementos de navegador;
- textos de interfaz como `Buscar`, `Todos`, `Foto`, horas sueltas o avisos.

## Integracion al ciclo

Orden dentro del core loop:

```text
StageZero
DomainContracts
AutonomousCycleStart
ReaderCore
Timeline
Cognitive
Operating
DomainEventsBeforeCaseManager
CaseManager
ChannelRouting
AccountingCore
Memory
TrustSafety
Supervisor
AutonomousCycle
DomainEventsAfterCycle
```

Reader Core escribe `conversation_event` antes de Timeline, por lo que Living
Memory recibira conversaciones con fuente, confianza y evidencia.

## No hace

Esta etapa no mueve mouse, no abre chats y no envia mensajes.

Eso pertenece a:

```text
Etapa 12: Hands & Verification
```

Reader Core solo decide si una lectura es segura para que el resto de la IA
piense sobre ella.

## Definicion de terminado

Queda cerrada como base 0.8.11 porque:

- existe documento de diseno;
- existe contrato de mensaje visible;
- existe contrato de estado;
- existe implementacion integrada antes de Timeline;
- compara fuentes estructuradas contra OCR;
- emite desacuerdos auditables;
- produce `conversation_event`;
- tiene pruebas sin sesiones reales y sin movimiento de mouse.

Pendiente para autonomia final:

- conectar adaptadores vivos DOM/CDP/UIA reales por navegador;
- validar en una sesion real prolongada de Edge, Chrome y Firefox;
- medir latencia real de mensaje nuevo a evento de negocio.
