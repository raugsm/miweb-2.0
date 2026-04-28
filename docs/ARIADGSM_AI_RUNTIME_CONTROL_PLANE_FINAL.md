# AriadGSM AI Runtime Control Plane

Fecha: 2026-04-28
Estado: Capa 2 consolidada como autoridad de sesion

## 1. Capa afectada

```text
Capa 2: AI Runtime Control Plane
```

Capas relacionadas:

- Capa 1: Operator Product Shell
- Capa 3: Cabin Reality Authority
- Capa 5: Event, Timeline & Durable State Backbone
- Capa 8: Trust, Telemetry, Evaluation & Cloud

No se agrega una capa nueva. Este bloque consolida la autoridad de vida que ya
estaba definida en `docs/ARIADGSM_FINAL_AI_ARCHITECTURE.md`.

## 2. Fuentes externas revisadas

- Microsoft .NET Generic Host:
  https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- Microsoft .NET Worker Services:
  https://learn.microsoft.com/en-us/dotnet/core/extensions/workers
- OpenTelemetry Traces:
  https://opentelemetry.io/docs/concepts/signals/traces/
- LangGraph Durable Execution:
  https://docs.langchain.com/oss/python/langgraph/durable-execution

Aplicacion a AriadGSM:

- el inicio y apagado deben ser protocolo de vida, no botones sueltos;
- una sesion debe envolver UI, updater, cabina, motores, supervisor y stop;
- el estado debe poder correlacionarse por `runSessionId`;
- cada fase debe dejar checkpoint para reconstruir que paso;
- lectura, pensamiento y accion no pueden bloquearse como una sola masa.

## 3. Decision final

El Control Plane queda como autoridad unica de sesion local.

Toda orden importante debe producir:

- `runSessionId`;
- `commandId`;
- origen humano/tecnico;
- razon exacta;
- resultado;
- fases del Boot Protocol;
- readiness separada;
- causa exacta de stop/update/dispose/operator.

## 4. Boot Protocol por fases

El arranque queda dividido en fases:

```text
operator_authorized
update_check
workspace_bootstrap
preflight
runtime_governor
workspace_guardian
workers
python_core
supervisor
readiness
```

Cada fase tiene:

- `phaseId`;
- `status`;
- `summary`;
- `updatedAt`;
- `completedAt` cuando aplica.

Esto evita el fallo repetido de "le di iniciar y no se que hizo".

## 5. Start/Stop Command Ledger

El archivo local:

```text
desktop-agent/runtime/control-plane-command-ledger.jsonl
```

registra comandos como:

- start;
- start_engines;
- stop;
- update;
- run_once;
- dispose.

Cada linea lleva:

- `commandId`;
- `commandType`;
- `source`;
- `reason`;
- `runSessionId`;
- `status`;
- `accepted`;
- `result`;
- `createdAt`;
- `completedAt`.

## 6. Readiness separada

La IA ya no queda "lista/no lista" como una sola cosa.

Control Plane publica:

```text
read.ready
think.ready
act.ready
sync.ready
```

Consecuencia:

- si Hands/Input Arbiter esta bloqueado, manos se pausan;
- ojos y memoria pueden seguir vivos;
- si la cabina no esta lista, lectura se bloquea;
- si el cerebro o memoria fallan, accion queda pausada;
- si la nube falla, la IA local puede seguir trabajando.

## 7. Causas exactas

El estado `control-plane-state.json` incluye:

- `lastStopCause.reason`;
- `lastStopCause.source`;
- `lastStopCause.runSessionId`;
- `lastStopCause.at`;
- `lastStopCause.detail`.

Ejemplos de origen:

- `ui.pause_button`;
- `ui.form_closing`;
- `runtime.updater`;
- `runtime.dispose`;
- `ui.prepare_whatsapps_button`.

Esto evita que `operator_button`, `dispose` o `app_closing` se mezclen sin
evidencia.

## 8. Integracion con componentes actuales

UI:

- solicita inicio con `RequestControlPlaneStart`;
- ya no representa la autoridad de sesion;
- stop, cierre de ventana y alistamiento registran fuente exacta.

Life Controller:

- publica `runSessionId`;
- reporta vida, pero no reemplaza el ledger.

Runtime Kernel:

- conserva verdad operacional;
- agrega `runSessionId`;
- declara `sessionTruthSource = control-plane-state.json`;
- expone `canRead` junto a observe/think/act/sync.

Runtime Governor:

- publica `runSessionId`;
- sigue controlando solo procesos propios AriadGSM.

Workspace Guardian:

- queda dentro de la fase `workspace_guardian`;
- no decide sesion.

Updater:

- queda como comando de Control Plane;
- registra version objetivo y resultado.

## 9. Contrato

Contrato nuevo:

```text
desktop-agent/contracts/control-plane-state.schema.json
```

Salida local:

```text
desktop-agent/runtime/control-plane-state.json
```

Checkpoints:

```text
desktop-agent/runtime/control-plane-checkpoints.jsonl
```

Ledger:

```text
desktop-agent/runtime/control-plane-command-ledger.jsonl
```

## 10. Definicion de terminado

Capa 2 queda cerrada cuando:

- la UI no inicia motores sin crear sesion;
- todo arranque tiene `runSessionId`;
- el Boot Protocol publica fases;
- Start/Stop quedan en ledger;
- stop/update/dispose/operator tienen causa exacta;
- read/think/act/sync estan separados;
- Runtime Kernel y Runtime Governor reciben sesion;
- hay contrato JSON;
- hay pruebas automatizadas;
- build y paquete pasan.

## 11. Por que no es parche

No corrige un boton puntual.

Mueve la causa de los fallos de inicio/apagado a una autoridad unica:

```text
UI pide -> Control Plane registra -> Life/Kernel/Governor ejecutan -> UI explica
```

El siguiente fallo ya no deberia aparecer como "se cayo": debe aparecer como
sesion, comando, fase, causa y componente responsable.
