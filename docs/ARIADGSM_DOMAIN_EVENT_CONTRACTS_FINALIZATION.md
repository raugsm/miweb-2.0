# AriadGSM Domain Event Contracts Finalization

Fecha: 2026-04-28
Version objetivo: 0.8.2
Estado: cierre final de Etapa 1

## 1. Proposito

Esta etapa cierra `Domain Event Contracts` como columna vertebral operativa.

El objetivo no es tener "mas eventos". El objetivo es que AriadGSM IA Local no
pueda tomar decisiones importantes, mover manos, registrar dinero, aprender
hechos o pedir aprobaciones sin dejar una historia auditable.

## 2. Fuentes externas usadas

Se uso investigacion externa para evitar inventar una arquitectura fragil:

- CloudEvents: eventos con identidad, tipo, fuente, subject, tiempo y payload
  separado de metadata.
  https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md
- JSON Schema 2020-12: validacion formal de estructuras JSON.
  https://json-schema.org/draft/2020-12
- OpenTelemetry Context Propagation: trace/correlation para reconstruir cadenas
  entre procesos.
  https://opentelemetry.io/docs/concepts/context-propagation/
- Microsoft Event-Driven Architecture: productores y consumidores desacoplados,
  eventos durables, replay y respuesta casi en tiempo real.
  https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/event-driven
- Microsoft Event Sourcing: append-only store, auditoria, idempotencia y
  materialized views.
  https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing
- Microsoft Domain Analysis / DDD: bounded contexts y lenguaje ubicuo para que
  el software refleje el negocio real.
  https://learn.microsoft.com/en-us/azure/architecture/microservices/model/domain-analysis
- OpenAI Structured Outputs y Agents guardrails: salidas estructuradas,
  validacion, revision humana y bloqueo de respuestas/acciones riesgosas.
  https://platform.openai.com/docs/guides/structured-outputs/supported-schemas
  https://openai.github.io/openai-agents-python/guardrails/

## 3. Decision para AriadGSM

Todo evento importante pasa por este camino:

```text
motor tecnico -> domain event validado -> memoria/proyeccion -> ciclo autonomo -> manos/supervision
```

Ningun modulo importante debe depender solo de:

- texto suelto;
- OCR bruto;
- decision local sin trazabilidad;
- accion de mouse sin verificacion;
- pago sin evidencia;
- aprendizaje sin estado de candidato/correccion.

## 4. Que queda cerrado

La etapa queda cerrada cuando existen y pasan validacion:

- contrato comun `domain-event-envelope`;
- registro versionado `domain-event-registry`;
- adaptadores para todos los eventos de motor relevantes;
- eventos humanos/correcciones;
- cobertura de eventos importantes;
- memoria leyendo domain events como fuente de negocio;
- verificador de cierre de contratos;
- estado y reporte humano;
- prueba automatica;
- integracion en el ciclo principal antes de Memory.

## 5. Eventos humanos y correcciones

La IA no se automejora tocando codigo. Se automejora cuando entiende una
correccion humana como evento.

Eventos obligatorios:

```text
HumanCorrectionReceived
HumanApprovalGranted
HumanApprovalRejected
OperatorOverrideRecorded
OperatorNoteAdded
AccountingCorrectionReceived
```

Uso:

- una correccion no borra historia;
- crea un nuevo evento que corrige, rechaza o aprueba;
- Memory puede aprender de la correccion;
- Supervisor puede bloquear si falta aprobacion humana.

## 6. Cobertura obligatoria

Un evento de motor se considera importante si es:

- `decision_event`;
- `accounting_event`;
- `action_event`;
- `learning_event`;
- `human_feedback_event`;
- `autonomous_cycle_event` con bloqueo o permiso humano.

Cada evento importante debe producir al menos un `domain_event` validado.

Si no ocurre, la etapa queda en `attention` o `blocked`.

## 7. Memoria como proyeccion

Memory Core debe comportarse como proyeccion de negocio:

```text
Domain Events -> Memory projections
```

Todavia conserva compatibilidad con eventos tecnicos para no romper lectura de
mensajes, pero la fuente de decisiones, pagos, acciones y aprendizaje pasa a ser
`domain-events.jsonl`.

## 8. Definicion de terminado

Etapa 1 esta cerrada si:

- `ariadgsm_agent.domain_contracts` reporta `ok`;
- `domain_events_contracts.py` cubre pagos, grupos, UI rechazada, acciones,
  correcciones humanas y privacidad;
- `architecture_contracts.py` sigue pasando;
- `Memory Core` ingiere domain events;
- `AgentRuntime.cs` ejecuta `DomainEvents` antes de `Memory`;
- version y manifest quedan coherentes.

## 9. Resultado esperado

Al cerrar 0.8.2, AriadGSM puede decir:

```text
Mis motores hablan un idioma comun.
Mis decisiones importantes quedan auditadas.
Mis correcciones humanas son parte del aprendizaje.
Mi memoria puede proyectar eventos de dominio.
No salto a Case Manager hasta que esta columna esta verde.
```

