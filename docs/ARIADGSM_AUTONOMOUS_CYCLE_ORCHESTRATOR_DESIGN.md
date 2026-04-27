# AriadGSM Autonomous Cycle Orchestrator Design

Fecha: 2026-04-27
Estado: diseno e implementacion objetivo para la etapa 2 de `Execution Lock`
Version objetivo: 0.8.0

## 1. Bloque activo

Segun `docs/ARIADGSM_EXECUTION_LOCK.md`, el bloque activo es:

```text
Autonomous Cycle Orchestrator
```

No se cambia de bloque. Product Shell, Cabin Authority final, Trust & Safety
completo, Hands avanzado y Cloud Sync quedan fuera salvo integracion minima.

## 2. Problema que resuelve

El sistema ya tenia motores valiosos, pero el boton `Encender IA` podia terminar
activando piezas que trabajaban como islas:

- Vision observa.
- Perception interpreta.
- Cognitive decide.
- Supervisor bloquea.
- Hands intenta actuar.
- Memory guarda.

El problema es que faltaba un ciclo mental unico que diga:

```text
Estoy observando.
Ya entendi.
Esto planeo hacer.
Necesito permiso o puedo seguir.
Actue.
Verifique.
Aprendi.
Reporte.
```

Sin ese ciclo, AriadGSM parece un bot con motores pegados. Con este ciclo,
empieza a comportarse como una IA operadora.

## 3. Fuentes externas usadas

### 3.1 OpenAI: agentes como modelo + herramientas + instrucciones

OpenAI describe los agentes como sistemas con modelo de razonamiento,
herramientas e instrucciones/guardrails. Tambien recomienda evaluar workflows y
usar trazas para detectar errores.

Impacto en AriadGSM:

- el ciclo no es una herramienta;
- el ciclo coordina razonamiento, herramientas y limites;
- cada paso debe quedar trazado y evaluable.

Referencias:

- https://openai.com/business/guides-and-resources/a-practical-guide-to-building-ai-agents/
- https://platform.openai.com/docs/guides/agent-evals

### 3.2 OpenAI Safety: structured outputs, tool approvals, guardrails

OpenAI recomienda limitar datos no confiables con estructuras, mantener
aprobaciones de herramientas y usar guardrails antes de acciones sensibles.

Impacto en AriadGSM:

- el ciclo produce JSON validable, no texto libre;
- `request_permission` es fase obligatoria;
- Hands no debe actuar si el gate dice `ASK_HUMAN`, `BLOCK` o
  `PAUSE_FOR_OPERATOR`.

Referencia:

- https://platform.openai.com/docs/guides/agent-builder-safety

### 3.3 Microsoft Event-Driven Architecture y Saga Orchestration

Microsoft diferencia productores, consumidores y canales de eventos, y recomienda
orquestacion cuando un proceso cruza varios servicios.

Impacto en AriadGSM:

- cada motor sigue emitiendo eventos;
- el ciclo central no borra esa separacion, la ordena;
- si un paso falla, el ciclo registra bloqueo o recuperacion en vez de seguir a
  ciegas.

Referencias:

- https://learn.microsoft.com/en-us/azure/architecture/guide/architecture-styles/event-driven
- https://learn.microsoft.com/en-us/azure/architecture/reference-architectures/saga/saga

### 3.4 NIST AI RMF y OWASP LLM Top 10

NIST enfatiza gobernar, medir y gestionar riesgo. OWASP alerta sobre exceso de
agencia, memoria contaminada y mal uso de herramientas.

Impacto en AriadGSM:

- el ciclo mide estado y riesgos;
- no sube autonomia por fe;
- textos de WhatsApp no pueden saltarse permisos internos;
- memoria y acciones sensibles pasan por gates.

Referencias:

- https://www.nist.gov/publications/artificial-intelligence-risk-management-framework-generative-artificial-intelligence
- https://owasp.org/www-project-top-10-for-large-language-model-applications/

## 4. Contrato mental del ciclo

El ciclo oficial queda congelado asi:

```text
observe -> understand -> plan -> request_permission -> act -> verify -> learn -> report
```

### 4.1 observe

Pregunta:

```text
Que estoy viendo y en que cabina/canal?
```

Entradas:

- `vision-health.json`
- `perception-health.json`
- `interaction-state.json`
- `cabin-readiness.json`
- `cabin-manager-state.json`

Salida:

- estado de ojos;
- canales listos;
- si hay operadores usando input;
- si se puede leer.

### 4.2 understand

Pregunta:

```text
Que mensajes, conversaciones o senales de negocio entiendo?
```

Entradas:

- `timeline-state.json`
- `perception-health.json`
- `conversation-events.jsonl`

Salida:

- mensajes unidos;
- historias;
- eventos rechazados;
- lectura util vs ruido.

### 4.3 plan

Pregunta:

```text
Cual es la siguiente mejor accion de negocio?
```

Entradas:

- `cognitive-state.json`
- `operating-state.json`
- `memory-state.json`

Salida:

- decision;
- caso o tarea;
- accion propuesta;
- motivo y confianza.

### 4.4 request_permission

Pregunta:

```text
Puedo actuar ahora o debo pedir permiso / pausar?
```

Entradas:

- `supervisor-state.json`
- `input-arbiter-state.json`
- riesgo de la decision;
- nivel de autonomia.

Salida:

```text
ALLOW
ALLOW_WITH_LIMIT
ASK_HUMAN
PAUSE_FOR_OPERATOR
BLOCK
```

### 4.5 act

Pregunta:

```text
Que accion autorizada ejecuto?
```

Entradas:

- `hands-state.json`
- `action-events.jsonl`
- directiva del ciclo.

Salida:

- accion escrita;
- accion ejecutada;
- accion bloqueada;
- accion cedida al operador.

### 4.6 verify

Pregunta:

```text
La accion logro el objetivo correcto?
```

Entradas:

- verificacion de Hands;
- lectura posterior;
- supervisor.

Salida:

- verificado;
- fallo explicado;
- recuperacion requerida.

### 4.7 learn

Pregunta:

```text
Que debo guardar como memoria, sospecha o correccion?
```

Entradas:

- `memory-state.json`
- `learning-events.jsonl`
- `accounting-events.jsonl`
- eventos de dominio.

Salida:

- aprendizaje;
- borrador contable;
- sospecha;
- conflicto;
- nada que aprender.

### 4.8 report

Pregunta:

```text
Como se lo explico a Bryams?
```

Entradas:

- resumen del ciclo;
- blockers;
- next actions;
- reporte humano.

Salida:

- `autonomous-cycle-state.json`
- `autonomous-cycle-events.jsonl`
- `autonomous-cycle-directives.json`
- `autonomous-cycle-report.json`

## 5. Archivos de salida

### 5.1 Estado unico

```text
desktop-agent/runtime/autonomous-cycle-state.json
```

Debe contener:

- `cycleId`
- `status`
- `phase`
- `summary`
- `steps`
- `stages`
- `permissionGate`
- `directives`
- `humanReport`
- `blockers`
- `nextActions`
- `metrics`

### 5.2 Evento auditable

```text
desktop-agent/runtime/autonomous-cycle-events.jsonl
```

Cada ciclo emite un evento `autonomous_cycle_event` validado por contrato.

### 5.3 Directivas

```text
desktop-agent/runtime/autonomous-cycle-directives.json
```

No ejecuta acciones por si solo. Declara que pueden hacer los motores en el
momento actual.

Ejemplo:

```json
{
  "gateDecision": "PAUSE_FOR_OPERATOR",
  "allowedEngines": {
    "vision": true,
    "perception": true,
    "memory": true,
    "hands": false
  }
}
```

### 5.4 Reporte humano

```text
desktop-agent/runtime/autonomous-cycle-report.json
```

Debe ser entendible:

```text
Estoy observando 3 WhatsApps.
Necesito permiso antes de actuar.
No movi mouse porque Bryams tomo control.
Aprendi 12 mensajes utiles y 2 borradores contables.
```

## 6. Reglas de seguridad del ciclo

1. El ciclo no mueve mouse.
2. El ciclo no escribe mensajes.
3. El ciclo no confirma contabilidad final.
4. El ciclo autoriza, pausa, bloquea o reporta.
5. Si `Input Arbiter` dice que Bryams tiene control, `Hands` queda deshabilitado.
6. Si Supervisor marca critico, el ciclo queda `blocked`.
7. Si hay accion sensible, el gate debe ser `ASK_HUMAN`.
8. Si no hay lectura util, no se inventa negocio.
9. Si no hay memoria, se reporta como aprendizaje pendiente.
10. Si todo esta ok, se emite checkpoint y se permite continuar.

## 7. Definicion de terminado

El bloque queda terminado cuando:

- existe este diseno;
- `autonomous-cycle-event.schema.json` cubre steps/gate/directives;
- `autonomous_cycle.py` genera ciclo por fases;
- se escribe estado unico, evento, directivas y reporte humano;
- se integra con `AgentRuntime`;
- hay pruebas del flujo sin mouse real;
- Domain Events puede adaptar el ciclo a `CycleStarted`, `CycleCheckpointCreated`,
  `CycleBlocked` o `CycleRecovered`;
- la app puede mostrar resumen humano desde el estado del ciclo.

## 8. Que queda fuera de este bloque

No se implementa aun:

- nuevo Product Shell visual completo;
- Cabin Authority final;
- Case Manager final;
- Channel Routing Brain final;
- Accounting Core evidence-first final;
- envio automatico de mensajes;
- operacion avanzada de herramientas GSM.

Esos siguen en el orden de `Execution Lock`.

