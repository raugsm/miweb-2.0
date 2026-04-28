# AriadGSM Cabin Reality Authority final

Version: 0.9.7
Estado: Capa 3 consolidada como autoridad de cabina.

## Fuentes externas usadas

- Microsoft UI Automation Control Patterns:
  https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview
- Microsoft SetForegroundWindow:
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow
- Microsoft DWM DWMWINDOWATTRIBUTE / DWMWA_CLOAKED:
  https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
- Microsoft Windows Graphics Capture:
  https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture
- Playwright actionability checks:
  https://playwright.dev/docs/actionability

## Decision final

La cabina ya no usa una sola senal para decir "listo". Capa 3 separa tres
verdades:

1. `structuralReady`: Windows confirma que el navegador correcto existe,
   corresponde al canal esperado y su columna no esta tapada.
2. `semanticFresh`: Reader/Perception aportan lectura fresca de ese canal.
3. `actionReady`: Capa 7 puede actuar porque la ventana es visible, la lectura
   esta fresca, Input Arbiter no cedio control al operador y Hands puede
   verificar.

Un canal solo queda accionable cuando las tres verdades coinciden. Si solo la
ventana esta bien, el estado humano sera "visible, esperando lectura fresca",
no "listo para manos".

## Mapa fijo de cabina

- `wa-1`: Microsoft Edge / `msedge`
- `wa-2`: Google Chrome / `chrome`
- `wa-3`: Mozilla Firefox / `firefox`
- URL valida: `https://web.whatsapp.com/`
- Prohibido: abrir WhatsApp Desktop por shell URL.
- Prohibido: cerrar, matar, minimizar o reorganizar navegadores desde motores
  que no sean Cabin Reality Authority.

## Contrato operativo

Archivo principal:

```text
desktop-agent/runtime/cabin-authority-state.json
```

Campos de autoridad:

- `contract = cabin_authority_state`
- `authorityVersion = cabin-reality-authority-v2`
- `readiness.expectedChannels`
- `readiness.structuralReadyChannels`
- `readiness.actionReadyChannels`
- `channels[].status`
- `channels[].structuralReady`
- `channels[].semanticFresh`
- `channels[].actionReady`
- `channels[].handsMayAct`

Estados validos por canal:

- `visible_ready`: ventana correcta, visible y libre; aun no autoriza manos.
- `action_ready`: ventana, lectura fresca y accionabilidad coinciden.
- `covered`: la columna esta tapada.
- `missing`: no hay ventana WhatsApp recuperable para ese canal.
- `human_required`: WhatsApp pide QR, "Usar aqui" o error de perfil.
- `blocked` / `stale`: evidencia vieja o contradictoria.

## Integracion con capas

### Capa 2: AI Runtime Control Plane

La sesion sigue siendo duena de `runSessionId`, start/stop y ciclo de vida. Capa
3 no inicia motores por su cuenta; solo publica realidad de cabina para que Capa
2 decida si el arranque puede continuar.

### Capa 4: Perception & Reader Core

Window Reality fusiona cabina con lectura fresca. Capa 3 consume ese consenso
para decidir si un canal puede pasar de `visible_ready` a `action_ready`.

### Capa 7: Action, Tools & Verification

Hands solo acepta acciones cuando `cabin-authority-state.json` dice
`status=action_ready` y `handsMayAct=true`. Si el canal esta solo
`visible_ready`, puede leer/esperar, pero no mover mouse.

### Capa 8: Trust, Telemetry, Evaluation & Cloud

La caja negra puede explicar si el bloqueo vino de ventana tapada, lectura vieja,
input humano, seguridad o falta de ventana. Eso evita que la UI diga "no hizo
nada" sin razon.

## Reglas de no contradiccion

- Cabin Manager puede decir que una ventana esta estructuralmente visible.
- Window Reality decide si esa evidencia esta fresca y no contradice Reader,
  Input y Hands.
- Orchestrator ya no permite acciones solo por `cabin-readiness.json`; exige
  Window Reality accionable.
- Hands ya no interpreta `ready` visual como permiso fisico: exige
  `action_ready` o compatibilidad antigua `ready` con `handsMayAct=true`.

## Definicion de terminado

- El contrato `cabin-authority-state` valida con JSON schema.
- La app no abre WhatsApp Desktop.
- La app no cierra Edge, Chrome ni Firefox durante alistamiento.
- Una ventana DWM cloaked no cuenta como visible.
- Una ventana tapada no cuenta como accionable.
- Un canal visible sin lectura fresca queda `visible_ready`, no `action_ready`.
- Capa 7 bloquea acciones si Capa 3 no autorizo el canal exacto.
- Orchestrator reporta el mismo bloqueo que Window Reality y Cabin Authority.
