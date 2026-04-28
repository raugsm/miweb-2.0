# AriadGSM Hands Engine Advanced Design

Version objetivo: 0.8.7
Fecha: 2026-04-27
Estado: diseno tecnico del bloque "Hands Engine avanzado + accion sobre chat verificada"

## 1. Proposito

Hands Engine no debe ser un bot que hace clic por intuicion. Debe ser la capa de
manos de AriadGSM IA Local:

```text
intencion -> seguridad -> autoridad de cabina -> arbitro de input -> accion -> verificacion por percepcion -> auditoria
```

La regla base es:

```text
La IA puede tocar el mouse solo cuando sabe que ventana/canal toca, tiene una
fila de chat visible verificada y puede confirmar por Perception que abrio el
chat correcto antes de continuar.
```

## 2. Fuentes tecnicas usadas

Este bloque usa documentacion oficial de Microsoft porque toca APIs de Windows:

- `SendInput`: inyecta eventos de mouse/teclado en el flujo de entrada y reporta
  cuantos eventos fueron insertados.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- `GetLastInputInfo`: permite detectar inactividad de input de la sesion actual.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo
- `SetForegroundWindow`: Windows limita cuando un proceso puede traer una ventana
  al frente; por eso Hands no debe asumir que enfocar fue suficiente.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow
- UI Automation control patterns: confirma que acciones estructuradas deben
  apoyarse en patrones cuando existan, y que input fisico es solo una parte de la
  estrategia.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview

Decision tecnica:

- reemplazar `mouse_event` por `SendInput`;
- mantener `GetLastInputInfo` como arbitro de prioridad humana;
- tratar `SetForegroundWindow` como intento, no como verdad;
- hacer que Perception confirme el resultado antes de continuar.

## 3. Responsabilidad exacta

Hands Engine avanzado se encarga de:

- abrir un chat visible usando coordenadas confirmadas por Interaction Engine;
- verificar que el chat abierto sea el esperado;
- ceder el mouse al operador si detecta input humano reciente;
- dejar ojos, memoria y cognicion corriendo aunque las manos se pausen;
- reportar fallos en lenguaje humano;
- auditar cada accion con evidencia de canal, chat, coordenada, espera y
  verificacion.

Hands Engine avanzado no se encarga de:

- cerrar Edge, Chrome o Firefox;
- mover o reacomodar ventanas;
- iniciar sesiones de WhatsApp;
- leer mensajes por OCR;
- decidir precios, pagos o contabilidad;
- enviar mensajes al cliente.

Esas responsabilidades viven en Cabin Authority, Perception, Cognitive,
Accounting y Safety.

## 4. Contrato de accion verificada

Para `open_chat`, el contrato minimo es:

```json
{
  "actionType": "open_chat",
  "target": {
    "channelId": "wa-2",
    "conversationTitle": "Cliente",
    "chatRowTitle": "Cliente",
    "clickX": 520,
    "clickY": 216,
    "verifiedBeforeContinue": true,
    "verificationPerceptionEventId": "perception-...",
    "verificationWaitMs": 150
  },
  "status": "verified",
  "verification": {
    "verified": true,
    "summary": "Perception confirmo chat correcto..."
  }
}
```

Si Perception ve otro chat:

```text
status = failed
verified = false
acciones dependientes del mismo canal = suspendidas en ese ciclo
```

Eso evita que la IA lea, haga scroll o cree contabilidad sobre el chat incorrecto.

## 5. Flujo de abrir chat

1. Planner recibe decision o navegador de aprendizaje.
2. Interaction Engine entrega `chat_row` accionable con `clickX/clickY`.
3. Safety bloquea si no hay coordenadas verificadas.
4. Cabin Authority confirma que ese canal pertenece a la cabina esperada.
5. Input Arbiter revisa si Bryams esta usando mouse o teclado.
6. Hands usa `SendInput` para hacer clic.
7. Hands espera una lectura fresca de Perception durante una ventana corta.
8. Verifier compara canal y titulo visible contra el chat esperado.
9. Si coincide, la accion queda `verified`.
10. Si no coincide, la accion queda `failed` y el canal se suspende en ese ciclo.

## 6. Arbitro de input

El arbitro tiene una regla humana:

```text
Si Bryams mueve mouse/teclado, la IA no pelea el control.
```

Cuando eso sucede:

- `handsPausedOnly = true`
- `eyesContinue = true`
- `memoryContinue = true`
- `cognitiveContinue = true`
- no se apaga Vision, Perception, Memory ni Cognitive;
- solo se bloquea la accion fisica puntual.

Esto prepara el sistema para autonomia alta sin volverse invasivo.

## 7. Fallos accionables

Los fallos deben decir que paso y que necesita Bryams:

- no veo WhatsApp visible para el canal;
- no pude enfocar la ventana;
- no hay fila de chat verificada;
- el operador esta usando el mouse;
- abri un canal pero Perception vio otro chat;
- no confirme el chat correcto dentro del tiempo esperado.

Ningun fallo debe quedar como codigo tecnico suelto para el operador.

## 8. Criterio de terminado de este bloque

Este bloque queda completo cuando:

- `open_chat` usa coordenadas verificadas por Interaction;
- `open_chat` queda `verified` solo si Perception confirma el chat correcto;
- si falla la confirmacion, no continua con acciones dependientes;
- el mouse se cede al operador sin apagar ojos ni memoria;
- `SendInput` reemplaza a `mouse_event`;
- existen pruebas sin sesiones reales de WhatsApp;
- el paquete del agente se versiona y queda publicado en GitHub.

