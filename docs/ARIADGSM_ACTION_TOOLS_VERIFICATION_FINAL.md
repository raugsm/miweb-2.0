# AriadGSM Action, Tools & Verification

Version: 0.9.6
Estado: consolidacion de Capa 7
Fecha: 2026-04-28

## Capa afectada

Capa 7: Action, Tools & Verification.

Esta entrega no agrega una capa nueva. Cierra el hueco entre "la IA decidio" y
"la IA toco la pantalla" con una transaccion obligatoria por accion fisica.

## Fuentes externas usadas

- Playwright Actionability: antes de hacer click, Playwright exige que el
  objetivo sea unico, visible, estable, reciba eventos y este habilitado.
  Fuente: https://playwright.dev/docs/actionability
- Microsoft UI Automation Control Patterns: Invoke, Scroll, Selection, Text,
  Value y Window separan capacidades reales de controles en vez de depender
  solo de pixeles.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview
- Microsoft UI Automation Specification: UI Automation permite acceder y operar
  elementos de escritorio con estructura programatica, no solo input bruto.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification
- OpenTelemetry Traces: un flujo auditable debe conservar traceId/span/eventos
  para reconstruir causalidad entre decision, accion y verificacion.
  Fuente: https://opentelemetry.io/docs/specs/otel/overview/

## Problema que cierra

El fallo real no era solo que Hands moviera el mouse. El fallo era que podia
recibir varias acciones desde una misma lectura y seguir actuando cuando la
pantalla ya habia cambiado.

Eso se parecia a un bot:

1. leer una foto;
2. decidir varias cosas;
3. hacer varios clicks;
4. verificar tarde.

La IA operadora necesita otro contrato:

1. decidir;
2. abrir una transaccion fisica;
3. validar realidad fresca;
4. actuar una sola vez;
5. verificar;
6. recien despues permitir la siguiente accion.

## Contrato de Capa 7

Antes de ejecutar una accion fisica, `Action Transaction Gate` exige:

- `runSessionId` y ciclo vivo gobernados por Capa 2;
- canal/ventana autorizados por Capa 3;
- Perception e Interaction apuntando a la misma lectura fresca de Capa 4;
- Trust & Safety e Input Arbiter vigentes por Capa 8;
- una sola accion fisica por ciclo;
- un solo canal bajo lease;
- politica no destructiva de navegador.

## Action Transaction Gate

Archivo:

`desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Transactions/ActionTransactionGate.cs`

Responsabilidad:

- crear `actionTransactionId`;
- crear `actionTraceId`;
- bloquear si ya existe una accion fisica viva;
- bloquear si Perception esta vieja;
- bloquear si Interaction no coincide con la Perception actual;
- bloquear si el click no cae dentro de la fila confirmada;
- bloquear cualquier accion destructiva de navegador;
- escribir estado y journal local.

No decide negocio. No elige clientes. No reemplaza al cerebro. Solo controla si
la mano puede tocar pantalla en este instante.

## Action Journal

Archivos runtime:

- `desktop-agent/runtime/action-transaction-state.json`
- `desktop-agent/runtime/action-journal.jsonl`

Contratos:

- `desktop-agent/contracts/action-transaction-state.schema.json`
- `desktop-agent/contracts/action-transaction-event.schema.json`

Cada accion guarda:

- fase: `begin`, `complete` o `blocked`;
- transaccion;
- trace;
- canal;
- accion;
- decision fuente;
- resumen;
- verificacion final cuando exista.

Esto permite responder: que quiso hacer, donde, con que evidencia, que paso y
por que paro.

## Politica no destructiva de navegador

Capa 7 no puede:

- cerrar ventanas;
- cerrar tabs;
- terminar procesos de navegador;
- abrir URL por shell;
- crear pestanas nuevas;
- recuperar o mover ventanas por su cuenta.

Si una ventana falta, esta tapada o perdio identidad, Capa 7 bloquea y deja que
Capa 3 explique el estado de cabina.

## Relacion con las otras capas

Capa 2: AI Runtime Control Plane

- mantiene sesion, start/stop, command ledger y readiness.

Capa 3: Cabin Reality Authority

- decide si `wa-1`, `wa-2`, `wa-3` son ventanas reales y accionables.

Capa 4: Perception & Reader Core

- produce lectura fresca antes y despues de tocar.

Capa 8: Trust, Telemetry, Evaluation & Cloud

- autoriza riesgo, cede mouse al operador y registra incidentes.

## Definicion de terminado

- Capa 7 no agrega capas nuevas.
- Hands ejecuta una accion fisica por ciclo.
- `open_chat` exige fila de chat confirmada por Perception actual.
- `Interaction` y `Perception` deben compartir `perceptionEventId`.
- Si no se confirma chat/canal, la accion falla y no continua.
- El estado humano dice si la accion fue verificada, bloqueada o fallida.
- Existe journal transaccional local.
- Existen pruebas C# y Python.
- Build y paquete de release pasan.

## Siguiente prueba real

1. Actualizar a la version 0.9.6.
2. Abrir/alistar Edge = WhatsApp 1, Chrome = WhatsApp 2, Firefox = WhatsApp 3.
3. Presionar `Encender IA`.
4. No tocar mouse por 60 segundos.
5. Verificar que solo haga una accion, espere verificacion y no continue si el
   chat/canal no coincide.
