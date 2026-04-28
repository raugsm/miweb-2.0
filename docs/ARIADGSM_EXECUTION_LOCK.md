# AriadGSM Execution Lock

Fecha: 2026-04-27
Estado: contrato de continuidad para no perder el hilo

## 1. Proposito

Este documento existe para evitar que Codex cambie de rumbo por intuicion.

A partir de ahora, cuando Bryams pida avanzar el proyecto, Codex debe leer este
archivo y respetar el bloque activo, el orden bloqueado y la definicion de
terminado.

Si Codex considera que el siguiente paso tecnico deberia cambiar, no puede
cambiarlo en silencio. Debe explicar:

```text
Quiero desviarme de Execution Lock por esta razon.
Riesgo si sigo el orden actual.
Riesgo si cambio de orden.
Decision recomendada.
```

Y esperar confirmacion de Bryams si el cambio altera el orden.

## 2. Frase de control del usuario

Cuando Bryams quiera que no se improvise, puede escribir:

```text
Codex, sigue Execution Lock. No cambies de bloque.
```

Cuando quiera entrega completa sin pasos intermedios:

```text
Codex, activa Modo Entrega Completa para el bloque <nombre>.
Sigue Execution Lock.
Investiga, implementa, prueba, versiona y sube a GitHub.
Solo detente ante bloqueo real.
```

Cuando quiera solo diseno:

```text
Codex, sigue Execution Lock. Solo documenta, no codifiques.
```

## 3. Regla central

El producto final es:

```text
AriadGSM IA Local: una IA operadora del negocio AriadGSM, no un bot de WhatsApp.
```

La IA debe comportarse como Bryams a nivel operativo:

- observa;
- entiende;
- recuerda;
- prioriza;
- cotiza;
- negocia;
- registra contabilidad;
- aprende de errores;
- usa herramientas;
- pide permiso cuando hay riesgo;
- verifica antes de dar por hecho.

## 4. Documentos fuente

Orden de autoridad:

1. `docs/ARIADGSM_EXECUTION_LOCK.md`
2. `docs/ARIADGSM_MASTER_EXECUTION_ROADMAP.md`
3. `docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md`
4. `docs/ARIADGSM_FINAL_PRODUCT_BLUEPRINT.md`
5. `docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS.md`
6. `docs/ARIADGSM_BUSINESS_DOMAIN_MAP.md`
7. `docs/ARIADGSM_BUSINESS_OPERATING_MODEL.md`
8. `docs/ARIADGSM_AUTONOMOUS_OPERATING_SYSTEM_1.0.md`
9. Documentos tecnicos por motor dentro de `desktop-agent/`

Si hay conflicto, manda este archivo.

## 5. Orden bloqueado de la nueva etapa

Este es el orden maestro que Bryams marco y queda bloqueado:

```text
0. Execution Lock
1. Domain Event Contracts
2. Autonomous Cycle Orchestrator
3. Case Manager
4. Channel Routing Brain
5. Accounting Core evidence-first
6. Product Shell
7. Cabin Authority
8. Safe Eyes / Reader Core
9. Living Memory
10. Business Brain
11. Trust & Safety + Input Arbiter
12. Hands & Verification
13. Tool Registry
14. Cloud Sync / ariadgsm.com
15. Evaluation + Release
```

Ningun bloque posterior puede reemplazar este orden sin aprobacion.

La version explicada del mapa vive en:

```text
docs/ARIADGSM_MASTER_EXECUTION_ROADMAP.md
```

## 6. Estado actual de los bloques

### 6.0 Execution Lock / Product Foundation

Estado:

```text
CERRADA COMO BASE FORMAL 0.8.1
```

Ya existe:

- `docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md`
- `desktop-agent/contracts/stage-zero-readiness.schema.json`
- `desktop-agent/ariadgsm_agent/stage_zero.py`
- `desktop-agent/tests/stage_zero_foundation.py`
- integracion minima al ciclo principal en `AgentRuntime.cs`

Objetivo:

Definir que el producto es `AriadGSM IA Local`, una IA operadora local para
AriadGSM, y fijar documentos, etapas, contratos, versionado y criterios de
terminado antes de seguir construyendo autonomia.

Pendiente para considerarlo producto final:

- ninguno dentro de Etapa 0; las etapas siguientes deben cumplir sus propias
  definiciones de terminado.

### 6.1 Domain Event Contracts

Estado:

```text
CERRADA COMO CONTRATO OPERATIVO FINAL 0.8.2
```

Ya existe:

- `desktop-agent/contracts/domain-event-envelope.schema.json`
- `desktop-agent/contracts/domain-event-registry.json`
- `desktop-agent/ariadgsm_agent/domain_events.py`
- `desktop-agent/ariadgsm_agent/domain_contracts.py`
- `desktop-agent/tests/domain_events_contracts.py`
- `desktop-agent/tests/domain_contracts_finalization.py`
- `docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS.md`
- `docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS_FINALIZATION.md`

Pendiente para considerarlo producto final:

- ninguno dentro de Etapa 1; Case Manager debe consumir esta columna sin crear
  un idioma paralelo.

### 6.2 Autonomous Cycle Orchestrator

Estado:

```text
IMPLEMENTADO COMO CICLO CENTRAL 0.8.0
```

Objetivo:

```text
observar -> entender -> planear -> pedir permiso -> actuar -> verificar -> aprender -> reportar
```

Debe resolver:

- que ningun boton haga trabajo por su lado;
- que Vision, Perception, Memory, Brain, Safety y Hands respondan a un ciclo comun;
- que el ciclo tenga estado visible;
- que cada paso emita domain events;
- que el ciclo pueda pausar y retomar.

Definicion de terminado:

- existe contrato de ciclo;
- existe estado unico de ciclo;
- el boton `Encender IA` registra inicio y checkpoint del ciclo;
- el ciclo produce reporte humano;
- el ciclo respeta permisos y handoff humano;
- tiene pruebas de flujo completo sin mover mouse real.

Entregables:

- `docs/ARIADGSM_AUTONOMOUS_CYCLE_ORCHESTRATOR_DESIGN.md`
- `desktop-agent/ariadgsm_agent/autonomous_cycle.py`
- `desktop-agent/contracts/autonomous-cycle-event.schema.json`
- `desktop-agent/tests/autonomous_cycle_orchestrator.py`
- integracion minima con `AgentRuntime.cs`

### 6.3 Case Manager

Estado:

```text
CERRADO COMO CASE MANAGER OPERATIVO 0.8.3
```

Objetivo:

Convertir mensajes y conversaciones en casos reales de trabajo.

Debe entender:

- cliente;
- pais;
- canal;
- servicio;
- marca/modelo;
- estado;
- pago/deuda;
- prioridad;
- riesgo;
- herramienta probable;
- siguiente accion.

Definicion de terminado:

- `CaseOpened`, `CaseUpdated`, `CaseNeedsHumanContext`, `CaseClosed` existen y se usan;
- cada pago, precio, accion y aprendizaje se puede asociar a un caso;
- la IA deja de tratar chats como mensajes sueltos;
- existe vista humana de casos abiertos.

Ya existe:

- `docs/ARIADGSM_CASE_MANAGER_DESIGN.md`
- `desktop-agent/contracts/case-manager-state.schema.json`
- `desktop-agent/ariadgsm_agent/case_manager.py`
- `desktop-agent/tests/case_manager.py`
- integracion de `CaseManager` entre Domain Events y Memory en `AgentRuntime.cs`
- integracion de Case Manager al Autonomous Cycle.

Pendiente para considerarlo producto final:

- ninguno dentro de este bloque; Channel Routing Brain debe usar los casos para
  decidir derivacion/fusion entre WhatsApp 1/2/3.

### 6.4 Channel Routing Brain

Estado:

```text
CERRADO COMO CHANNEL ROUTING BRAIN OPERATIVO 0.8.4
```

Objetivo:

Resolver que un cliente puede entrar por un WhatsApp pero corresponder a otro.

Debe decidir:

- atender en el canal actual;
- derivar a otro canal;
- fusionar contexto;
- marcar duplicado;
- pedir confirmacion humana.

Ejemplo:

```text
Cliente escribe por wa-2, pero el servicio es Xiaomi y el historial vive en wa-3.
La IA no debe perder contexto ni moverlo por regla fija.
Debe proponer ruta con evidencia.
```

Definicion de terminado:

- existe evento `ChannelRouteProposed`;
- existe evento `ChannelRouteAccepted` o aprobacion equivalente;
- la ruta explica motivo y confianza;
- no depende solo de posicion en pantalla;
- se prueba cliente cruzando canales.

Ya existe:

- `docs/ARIADGSM_CHANNEL_ROUTING_BRAIN_DESIGN.md`
- `desktop-agent/contracts/channel-routing-state.schema.json`
- `desktop-agent/ariadgsm_agent/channel_routing.py`
- `desktop-agent/tests/channel_routing.py`
- integracion de Channel Routing entre Case Manager, Domain Events, Memory y Autonomous Cycle;
- integracion de estado `Channel Routing` en `AgentRuntime.cs`.

Pendiente para considerarlo producto final:

- ninguno dentro de este bloque; Accounting Core debe usar casos y rutas para
  registrar pagos/deudas con evidencia antes de confirmar contabilidad.

### 6.5 Accounting Core evidence-first

Estado:

```text
CERRADO COMO ACCOUNTING CORE EVIDENCE-FIRST OPERATIVO 0.8.5
```

Objetivo:

Pagos, deudas, reembolsos y caja deben nacer desde evidencia y caso asociado.

Regla:

```text
La IA puede crear borradores contables.
La IA no confirma contabilidad final sin evidencia suficiente o permiso.
```

Debe manejar:

- `PaymentDrafted`;
- `DebtDetected`;
- `RefundCandidate`;
- `AccountingEvidenceAttached`;
- `AccountingRecordConfirmed`;
- moneda;
- monto;
- metodo;
- cliente;
- caso;
- fuente;
- confianza;
- estado.

Definicion de terminado:

- todo pago/deuda se vincula a caso;
- se separa borrador de confirmado;
- existe razon cuando falta evidencia;
- hay reporte humano de contabilidad pendiente;
- hay pruebas de pagos ambiguos, repetidos y confirmados.

Ya existe:

- `docs/ARIADGSM_ACCOUNTING_CORE_EVIDENCE_FIRST_DESIGN.md`
- `desktop-agent/contracts/accounting-core-state.schema.json`
- `desktop-agent/ariadgsm_agent/accounting_evidence.py`
- `desktop-agent/tests/accounting_core_evidence.py`
- eventos `AccountingEvidenceAttached` y `AccountingRecordConfirmed`;
- integracion con Case Manager, Domain Events, Channel Routing, Memory,
  Autonomous Cycle y `AgentRuntime.cs`.

Pendiente para considerarlo producto final:

- ninguno dentro de este bloque; la siguiente fase debe fortalecer la cabina,
  seguridad visual, interfaz final y manos sin volver a mezclarlo con la
  columna mental del negocio.

### 6.6 Product Shell

Estado:

```text
CERRADO COMO SHELL OPERATIVO 0.8.9
```

Objetivo:

Login, pantalla clara, estado humano, version visible, progreso real y actividad
entendible para Bryams.

Ya existe:

- `docs/ARIADGSM_PRODUCT_SHELL_FINAL_DESIGN.md`
- `desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/MainForm.cs`
- `desktop-agent/tests/product_shell_visual_final.py`

Pendiente para considerarlo autonomia final:

- ninguno dentro de esta etapa; las pantallas futuras deben seguir este lenguaje
  humano y no volver a exponer tablas tecnicas como experiencia principal.

### 6.7 Cabin Authority

Estado:

```text
CERRADO COMO AUTORIDAD DE CABINA 0.8.6
```

Objetivo:

Alistar Edge, Chrome y Firefox sin cerrar ventanas, sin abrir WhatsApp instalado,
con identidad wa-1/wa-2/wa-3 y estado claro antes de encender IA.

Ya existe:

- `docs/ARIADGSM_CABIN_AUTHORITY_FINAL_DESIGN.md`
- `desktop-agent/contracts/cabin-authority-state.schema.json`
- `desktop-agent/tests/cabin_authority_final.py`

Pendiente para considerarlo autonomia final:

- validacion viva prolongada en la PC de Bryams;
- integracion con `Safe Eyes / Reader Core` para confirmar lectura util real, no
  solo ventana visible.

### 6.8 Safe Eyes / Reader Core

Estado:

```text
SIGUIENTE ETAPA PENDIENTE
```

Objetivo:

Lectura robusta de WhatsApp Web como objetos reales:

- identidad positiva de WhatsApp Web;
- canal por navegador, no por coordenada;
- mensaje, chat, remitente, hora, direccion y confianza;
- DOM/accesibilidad/UI Automation primero;
- OCR solo como respaldo;
- evidencia auditable;
- errores humanos cuando no pueda leer.

Definicion de terminado:

- existe documento de diseno;
- existe contrato de salida del Reader Core;
- existe implementacion integrada al ciclo;
- existe comparacion de fuente estructurada vs OCR;
- existe prueba sin sesiones reales;
- la siguiente etapa `Living Memory` recibe mensajes reales y no ruido.

## 7. Bloques que NO pueden adelantarse

Estos bloques son importantes, pero no deben sustituir el orden de la nueva
etapa salvo que Bryams lo autorice:

- Living Memory.
- Business Brain.
- Trust & Safety + Input Arbiter final.
- Hands & Verification final.
- Tool Registry.
- Cloud Sync / ariadgsm.com.
- Updater final.
- Evaluation + Release.

Motivo:

Son necesarios para producto, pero si se hacen antes de cerrar `Safe Eyes /
Reader Core`, volvemos al patron de parches: la IA razonaria sobre lecturas
incompletas o ruido de pantalla.

Excepcion:

Se puede tocar un bloque no activo si es dependencia minima para terminar el
bloque actual, pero debe quedar declarado en el resumen.

## 8. Como debe responder Codex antes de trabajar

Si el usuario pide avanzar, Codex debe identificar:

```text
Bloque activo segun Execution Lock:
Alcance:
Archivos esperados:
Pruebas esperadas:
Version objetivo:
Que NO voy a tocar:
```

En Modo Entrega Completa no debe pedir confirmacion por cada paso, pero si debe
respetar el bloque activo.

## 9. Formato de cierre obligatorio

Al terminar un bloque, Codex debe cerrar asi:

```text
Bloque:
Estado: 100% / parcial / bloqueado
Que quedo hecho:
Que se probo:
Que no se pudo validar:
Version:
Commit:
Siguiente bloque segun Execution Lock:
```

## 10. Siguiente bloque activo

Como las etapas `0` a `7` quedan cerradas como base operativa, la siguiente
etapa pendiente del mapa maestro es:

```text
Etapa 8: Safe Eyes / Reader Core
```

El entregable documental minimo es:

```text
docs/ARIADGSM_SAFE_EYES_READER_CORE_DESIGN.md
```

El entregable tecnico minimo posterior es:

```text
lector estructurado para WhatsApp Web en Edge/Chrome/Firefox
contrato de mensaje visible con fuente, confianza y evidencia
DOM/accesibilidad/UI Automation como fuente primaria cuando exista
OCR como respaldo, no como fuente unica
comparacion de fuentes y reporte de desacuerdo
integracion al ciclo autonomo antes de Case Manager/Memory
pruebas sin sesiones reales ni movimiento de mouse
```
