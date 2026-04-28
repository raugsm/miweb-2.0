# AriadGSM Case Manager Design

Fecha: 2026-04-28
Version objetivo: 0.8.3
Estado: implementacion completa del bloque `Case Manager`

## 1. Proposito

Case Manager convierte eventos de dominio en casos reales de trabajo.

Antes de esta etapa, la IA podia leer conversaciones, detectar pagos, precios,
servicios y decisiones. El problema era que cada cosa vivia como evento suelto.
Eso no se parece a Bryams operando el negocio: Bryams no ve "lineas", ve casos.

Un caso AriadGSM une:

- cliente;
- canal o canales;
- conversacion o conversaciones;
- servicio;
- pais;
- equipo;
- precio;
- pago/deuda;
- riesgo;
- prioridad;
- siguiente accion;
- evidencia;
- estado.

## 2. Fuentes externas usadas

La estructura sigue patrones confiables:

- Microsoft Domain Analysis / DDD: modelar segun subdominios, entidades,
  agregados y lenguaje comun.
  https://learn.microsoft.com/en-us/azure/architecture/microservices/model/domain-analysis
- Microsoft Event Sourcing: un append-only event store como fuente de verdad,
  con proyecciones/materialized views para leer el estado actual.
  https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing
- Microsoft CQRS: separar comandos/eventos que cambian estado de vistas
  optimizadas para lectura.
  https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs
- Microsoft Event-Driven Architecture: desacoplar productores/consumidores y
  reaccionar a eventos.
  https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/event-driven
- OpenAI Agents guardrails: acciones y decisiones riesgosas deben pasar por
  controles, aprobacion humana y trazabilidad.
  https://openai.github.io/openai-agents-python/guardrails/

## 3. Decision para AriadGSM

Case Manager no lee OCR ni chats directamente.

Lee:

```text
desktop-agent/runtime/domain-events.jsonl
```

Produce:

```text
desktop-agent/runtime/case-manager-state.json
desktop-agent/runtime/case-manager-report.json
desktop-agent/runtime/case-events.jsonl
desktop-agent/runtime/case-manager.sqlite
```

Luego `DomainEvents` absorbe `case-events.jsonl` para que los casos tambien
queden dentro del historial central.

## 4. Flujo

```text
Domain Events
  -> Case Manager
  -> Case projection SQLite
  -> CaseOpened / CaseUpdated / CaseNeedsHumanContext / CaseClosed
  -> Domain Events
  -> Memory / Supervisor / Autonomous Cycle
```

## 5. Que abre un caso

Un caso puede nacer de:

- `CustomerCandidateIdentified`
- `ServiceDetected`
- `DeviceDetected`
- `QuoteRequested`
- `PaymentDrafted`
- `DebtDetected`
- `RefundCandidate`
- `DecisionExplained`
- `HumanApprovalRequired`
- eventos de accion/verificacion
- eventos de caso ya existentes

No abre caso por si solo:

- screenshot;
- objeto visual;
- fila de chat;
- ruido de navegador;
- grupo de pagos marcado como bajo valor.

Los grupos como `Pagos Mexico`, `Pagos Chile` y `Pagos Colombia` pueden quedar
registrados como `ignored`, pero no entran en la cola de clientes.

## 6. Estados de caso

Estados iniciales:

```text
open
in_progress
needs_quote
accounting_review
waiting_payment
needs_human
blocked
closed
ignored
```

Regla:

```text
Un pago, deuda, accion, aprendizaje o precio debe poder apuntar a un caseId.
```

## 7. Vista humana

El reporte humano debe decir:

- cuantos casos abiertos hay;
- cuantos necesitan a Bryams;
- cuales son los mas urgentes;
- que accion siguiente sugiere;
- que eventos quedaron vinculados;
- que casos fueron ignorados por baja utilidad.

## 8. Definicion de terminado

Case Manager queda cerrado si:

- existe `ariadgsm_agent.case_manager`;
- existe contrato `case-manager-state`;
- existe prueba `case_manager.py`;
- lee `domain-events.jsonl`;
- escribe `case-events.jsonl`;
- emite `CaseOpened`, `CaseUpdated`, `CaseNeedsHumanContext`, `CaseClosed` cuando corresponde;
- vincula pagos, precios, acciones, decisiones y aprendizajes a `caseId`;
- expone vista humana de casos abiertos;
- runtime ejecuta Case Manager despues de Domain Events y antes de Memory;
- version y paquete quedan publicados.

## 9. Limite de esta etapa

Esta etapa no resuelve todavia:

- derivar clientes entre WhatsApps con cerebro especializado;
- confirmar pagos;
- responder mensajes;
- mover mouse;
- operar herramientas GSM.

Eso viene despues. Pero a partir de aqui esas capacidades ya tendran una unidad
mental comun: el caso.

## 10. Modelo mental del Case Manager

Case Manager no es una lista de reglas para cada frase. Es una proyeccion viva
del negocio:

```text
Domain Event -> Case Aggregate -> Case State -> Case Events -> Memory/Supervisor
```

El evento es la evidencia historica. El caso es el agregado operativo. La vista
SQLite es la lectura rapida para que la cabina pueda responder en segundos sin
releer todo el pasado.

Campos minimos de cada caso:

- `caseId`
- `customerId`
- `channelId`
- `conversationId`
- `title`
- `status`
- `priority`
- `country`
- `service`
- `device`
- `intent`
- `paymentState`
- `quoteState`
- `requiresHuman`
- `nextAction`
- eventos vinculados

## 11. Idempotencia

El sistema puede ciclar muchas veces por minuto. Por eso Case Manager guarda:

- eventos de dominio ya procesados;
- eventos de caso ya emitidos;
- vinculos caso-evento;
- proyeccion de casos.

Si el mismo evento vuelve a entrar, no abre otro caso. Si el mismo caso recibe
nueva evidencia, actualiza el caso y emite un evento de caso nuevo solo cuando
hay causa nueva.

## 12. Human-in-the-loop

Case Manager no confirma pagos ni cierra decisiones de riesgo por su cuenta.
Cuando detecta dinero, deuda, reembolso, accion fallida o aprobacion humana
pendiente, marca `requiresHuman=true` y emite `CaseNeedsHumanContext`.

Esto deja listo el siguiente paso: Channel Routing Brain y Accounting Core
pueden razonar sobre casos reales, no sobre mensajes sueltos.

## 13. Integracion runtime

Orden de ejecucion local:

```text
StageZero
DomainContracts
AutonomousCycleStart
Timeline
Cognitive
Operating
DomainEventsBeforeCaseManager
CaseManager
DomainEventsBeforeMemory
Memory
Supervisor
AutonomousCycle
DomainEventsAfterCycle
```

La razon es simple: primero se normalizan eventos de motor a eventos de dominio;
luego Case Manager abre/actualiza casos; despues esos eventos de caso entran al
stream central para memoria, supervisor y reportes.

## 14. Definicion final de cerrado 0.8.3

Este bloque queda cerrado si:

- el contrato `case_manager_state` valida;
- `ariadgsm_agent.case_manager` lee `domain-events.jsonl`;
- escribe `case-manager-state.json`, `case-manager-report.json`,
  `case-manager.sqlite` y `case-events.jsonl`;
- emite `CaseOpened`, `CaseUpdated`, `CaseNeedsHumanContext`;
- no abre casos desde OCR/ventanas/ruido;
- marca grupos de pagos como `ignored`;
- Domain Events puede absorber `case-events.jsonl`;
- Autonomous Cycle ve Case Manager como etapa del ciclo;
- el paquete del agente queda versionado y publicado.
