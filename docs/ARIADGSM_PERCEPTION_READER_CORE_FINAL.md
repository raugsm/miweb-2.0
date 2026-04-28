# AriadGSM Capa 4: Perception & Reader Core Final

Fecha: 2026-04-28
Version: 0.9.8
Estado: consolidada como ojos frescos por canal

## Objetivo

Capa 4 convierte lo visible en WhatsApp Web en evidencia operativa: mensajes
visibles, chat, direccion, confianza, canal y fuente. No declara listo un canal
solo porque una ventana exista. La salida obligatoria es una lectura fresca por
canal que otras capas puedan usar para decidir si la IA puede leer o tocar.

## Fuentes externas usadas

- Microsoft UI Automation Control Patterns:
  https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview
- Windows Graphics Capture:
  https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture
- Chrome DevTools Protocol Accessibility:
  https://chromedevtools.github.io/devtools-protocol/tot/Accessibility/
- Playwright actionability checks:
  https://playwright.dev/docs/actionability

## Decision final

La lectura se decide por evidencia fusionada, no por una sola senal:

```text
ventana visible -> fuente estructurada/OCR -> mensaje confirmado -> frescura -> permiso para manos
```

Playwright usa controles de accionabilidad como visible, estable, recibe eventos
y habilitado antes de hacer acciones. AriadGSM aplica la misma idea al escritorio:
una ventana visible no basta; Reader Core debe confirmar contenido fresco antes
de que Capa 7 pueda actuar.

## Salidas obligatorias

### `reader-core-state.json`

Debe incluir:

- `freshnessPolicy`: declara que manos requieren lectura fresca.
- `channels`: estado por `wa-1`, `wa-2`, `wa-3`.
- `latestMessages`: mensajes aceptados en la corrida actual, aunque ya fueran
  duplicados en memoria.
- `humanReport`: lectura humana de que paso, que se leyo y que falta.

### `perception-events.jsonl`

Debe emitir objetos con identidad suficiente para que Reader Core los acepte:

- `window`: `channelId`, `browserProcess`, `windowTitle`.
- `conversation`: `conversationId`, `conversationTitle`, navegador y titulo.
- `message_bubble`: `channelId`, `messageKey`, conversacion, navegador,
  fuente y texto.

## Politica por canal

Cada canal puede quedar en:

- `fresh_messages_confirmed`: hay mensajes reales leidos en esta corrida.
- `no_fresh_read`: existen mensajes anteriores, pero no evidencia fresca.
- `empty`: aun no hay mensajes confirmados de ese canal.

Capa 3 puede decir "ventana visible". Capa 4 decide "lectura fresca". Capa 7
solo puede tocar si Capa 4 publica `canUnlockHands=true` para ese canal.

## Fallback

Orden de lectura:

1. DOM / CDP cuando exista adaptador local seguro.
2. Accesibilidad / UI Automation.
3. UIA emitida por motores internos.
4. OCR solo como respaldo y con menor confianza.

OCR nunca puede elevar por si solo una ventana desconocida a WhatsApp. Primero
debe existir identidad positiva de `web.whatsapp.com` o ventana de navegador
WhatsApp asignada a `wa-1`, `wa-2` o `wa-3`.

## Integracion por capa

- Capa 3: entrega ventanas y canal asignado.
- Capa 5: recibe `conversation_event` y mensajes visibles durables.
- Capa 7: queda bloqueada si falta lectura fresca por canal.
- Capa 8: reporta incidentes y explica bloqueos sin exponer chats completos.

## Definicion de terminado

Capa 4 queda cerrada cuando:

- Reader Core acepta eventos DOM/accesibilidad/UIA/OCR con identidad positiva.
- Perception entrega identidad de ventana, conversacion y mensaje.
- `reader-core-state.json` publica frescura por canal.
- Window Reality no permite manos si Reader no confirmo mensajes frescos.
- Las pruebas cubren evento ideal y evento real de Perception.
- La app compila, empaqueta y publica una version nueva.

