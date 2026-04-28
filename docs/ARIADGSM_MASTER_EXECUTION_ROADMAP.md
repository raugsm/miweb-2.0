# AriadGSM Master Execution Roadmap

Fecha: 2026-04-28
Estado: autoridad maestra de etapas

## Proposito

Este documento fija el camino completo para construir `AriadGSM IA Local` como
una IA operadora del negocio, no como un bot con reglas pegadas.

Una etapa solo se considera cerrada cuando cumple tres condiciones:

- tiene documento de diseno o contrato;
- tiene implementacion integrada al ciclo o a la cabina;
- tiene prueba repetible.

Una etapa puede estar `cerrada como base` sin ser `autonomia final`. Eso evita
confundir "compila y pasa tests" con "ya piensa como Bryams".

## Regla de IA, no bot

Cada etapa debe aportar una capacidad mental u operativa:

- observar con evidencia;
- entender contexto;
- formar casos;
- recordar;
- razonar;
- decidir con incertidumbre;
- pedir permiso cuando hay riesgo;
- actuar;
- verificar;
- aprender de correcciones.

Si una entrega solo agrega una regla puntual para un fallo puntual, no cierra una
etapa. Debe integrarse en contratos, eventos, memoria, seguridad o ciclo.

## Etapas maestras

Orden bloqueado:

```text
0. Execution Lock
0.5. Runtime Kernel
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

### Etapa 0: Execution Lock

Control del proyecto, orden bloqueado, definicion de terminado y reglas para que
Codex no improvise.

Estado actual: `base cerrada, requiere alineacion continua`.

Existe:

- `docs/ARIADGSM_EXECUTION_LOCK.md`
- `docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md`
- `desktop-agent/tests/stage_zero_foundation.py`

### Etapa 0.5: Runtime Kernel

Autoridad unica local sobre motores, incidentes, reinicios, capacidad de actuar,
cabina, input humano y recuperacion antes de Cloud Sync.

Estado actual: `cerrada como base final en 0.8.17`.

Existe:

- `docs/ARIADGSM_RUNTIME_KERNEL_FINAL.md`
- `desktop-agent/contracts/runtime-kernel-state.schema.json`
- `desktop-agent/ariadgsm_agent/runtime_kernel.py`
- `desktop-agent/tests/runtime_kernel.py`
- `desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.RuntimeKernel.cs`

Quedo cerrado:

- verdad unica `runtime-kernel-state.json`;
- incidentes normalizados;
- UI usando Kernel para titular estado humano;
- Vision tolera escritura denegada sin tumbar el motor;
- Cloud Sync congelado hasta consumir Runtime Kernel.

### Etapa 1: Domain Event Contracts

Idioma interno de la IA: eventos como `CustomerIdentified`, `CaseOpened`,
`PaymentDrafted`, `ActionVerified`.

Estado actual: `cerrada como contrato operativo`.

Existe:

- `docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS.md`
- `docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS_FINALIZATION.md`
- `desktop-agent/ariadgsm_agent/domain_events.py`
- `desktop-agent/ariadgsm_agent/domain_contracts.py`
- `desktop-agent/tests/domain_events_contracts.py`
- `desktop-agent/tests/domain_contracts_finalization.py`

### Etapa 2: Autonomous Cycle Orchestrator

El ciclo central:

```text
observar -> entender -> planear -> pedir permiso -> actuar -> verificar -> aprender
```

Estado actual: `implementada como ciclo central base`.

Existe:

- `docs/ARIADGSM_AUTONOMOUS_CYCLE_ORCHESTRATOR_DESIGN.md`
- `desktop-agent/ariadgsm_agent/autonomous_cycle.py`
- `desktop-agent/tests/autonomous_cycle_orchestrator.py`

Pendiente para autonomia final:

- depender de ojos, memoria y cerebro mas fuertes en etapas 8, 9 y 10.

### Etapa 3: Case Manager

Convertir chats en casos reales de trabajo, no mensajes sueltos.

Estado actual: `cerrada como base operativa`.

Existe:

- `docs/ARIADGSM_CASE_MANAGER_DESIGN.md`
- `desktop-agent/ariadgsm_agent/case_manager.py`
- `desktop-agent/tests/case_manager.py`

Pendiente para autonomia final:

- mejorar la calidad de entrada con Reader Core;
- alimentar decisiones con Business Brain.

### Etapa 4: Channel Routing Brain

Decidir si un cliente se atiende en WhatsApp 1, 2, 3, se deriva o se fusiona
contexto.

Estado actual: `cerrada como base operativa`.

Existe:

- `docs/ARIADGSM_CHANNEL_ROUTING_BRAIN_DESIGN.md`
- `desktop-agent/ariadgsm_agent/channel_routing.py`
- `desktop-agent/tests/channel_routing.py`

Pendiente para autonomia final:

- aprender rutas reales desde memoria y correcciones;
- no quedarse solo en politica inicial.

### Etapa 5: Accounting Core evidence-first

Pagos, deudas, reembolsos y caja, siempre con evidencia y caso asociado.

Estado actual: `cerrada como base evidence-first`.

Existe:

- `docs/ARIADGSM_ACCOUNTING_CORE_EVIDENCE_FIRST_DESIGN.md`
- `desktop-agent/ariadgsm_agent/accounting_evidence.py`
- `desktop-agent/tests/accounting_core_evidence.py`

Pendiente para autonomia final:

- conectar Living Memory y Business Brain para detectar patrones contables,
  correcciones y excepciones reales.

### Etapa 6: Product Shell

Interfaz final: login, pantalla clara, estado humano, version, progreso real,
actividad entendible.

Estado actual: `cerrada como shell operativo 0.8.9`.

Existe:

- `docs/ARIADGSM_PRODUCT_SHELL_FINAL_DESIGN.md`
- `desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/MainForm.cs`
- `desktop-agent/tests/product_shell_visual_final.py`

### Etapa 7: Cabin Authority

Alistar Edge, Chrome y Firefox sin cerrar ventanas, con cajas fijas y estados
claros.

Estado actual: `cerrada como autoridad de cabina`.

Existe:

- `docs/ARIADGSM_CABIN_AUTHORITY_FINAL_DESIGN.md`
- `desktop-agent/contracts/cabin-authority-state.schema.json`
- `desktop-agent/tests/cabin_authority_final.py`

Pendiente para autonomia final:

- validacion viva prolongada en la PC de Bryams;
- integracion con Reader Core para confirmar no solo ventana lista, sino lectura
  util real.

### Etapa 8: Safe Eyes / Reader Core

Lectura robusta: DOM, accesibilidad, UI Automation y OCR como respaldo.
Mensajes reales, no ruido.

Estado actual: `cerrada como base Reader Core 0.8.11`.

Existe:

- `docs/ARIADGSM_SAFE_EYES_READER_CORE_DESIGN.md`
- `desktop-agent/contracts/visible-message.schema.json`
- `desktop-agent/contracts/reader-core-state.schema.json`
- `desktop-agent/ariadgsm_agent/reader_core.py`
- `desktop-agent/tests/safe_eyes_reader_core.py`

Quedo integrado antes de Timeline para que Case Manager, Accounting, Memory y
Business Brain reciban mensajes con fuente, confianza y evidencia.

Debe cerrar:

- identidad positiva: solo WhatsApp Web real;
- canal por navegador: Edge=wa-1, Chrome=wa-2, Firefox=wa-3;
- lectura como objetos: chat, remitente, direccion, mensaje, hora, confianza;
- comparacion de fuentes: DOM/accesibilidad/UIA/OCR;
- evidencia local auditable;
- errores humanos claros cuando no puede leer;
- prueba sin sesiones reales.

### Etapa 9: Living Memory

Memoria episodica, semantica, procedimental y contable. Lo que aprendio, lo que
duda y lo que corriges.

Estado actual: `cerrada como base operativa en 0.8.12`.

Existe:

- `docs/ARIADGSM_LIVING_MEMORY_DESIGN.md`
- `desktop-agent/contracts/living-memory-state.schema.json`
- memoria viva integrada en `desktop-agent/ariadgsm_agent/memory.py`
- `desktop-agent/tests/living_memory.py`

Debe cerrar:

- separar hechos, sospechas, procedimientos, estilo y contabilidad;
- aprender de correcciones humanas;
- olvidar o degradar conocimiento inseguro;
- explicar que aprendio y de donde.

Pendiente para autonomia final:

- que Business Brain use esta memoria para razonar y decidir;
- recuperacion semantica/vectorial cuando crezca el volumen real.

### Etapa 10: Business Brain

El cerebro de negocio: clientes, precios, servicios, mercado, proveedores,
herramientas, riesgos y estilo AriadGSM.

Estado actual: `cerrada como base operativa en 0.8.13`.

Existe:

- `docs/ARIADGSM_BUSINESS_BRAIN_DESIGN.md`
- `desktop-agent/contracts/business-brain-state.schema.json`
- `desktop-agent/ariadgsm_agent/business_brain.py`
- `desktop-agent/tests/business_brain.py`

Debe cerrar:

- decision de negocio con contexto;
- cotizacion y negociacion asistida;
- manejo de proveedores y excepciones;
- razonamiento con incertidumbre;
- propuesta de respuesta, no envio autonomo sin permiso.

Pendiente para autonomia final:

- alimentar Business Brain con resultados reales del Tool Registry;
- habilitar razonamiento LLM bajo contrato;
- validar con conversaciones reales largas.

### Etapa 11: Trust & Safety + Input Arbiter

Permisos, niveles de autonomia, no pelear con tu mouse, pedir confirmacion
cuando haya riesgo.

Estado actual: `cerrada como base final en 0.8.14`.

Existe:

- `docs/ARIADGSM_TRUST_SAFETY_INPUT_ARBITER_FINAL.md`
- `docs/ARIADGSM_TRUST_SAFETY_CORE_DESIGN.md`
- `desktop-agent/contracts/trust-safety-state.schema.json`
- `desktop-agent/contracts/input-arbiter-state.schema.json`
- `desktop-agent/contracts/safety-approval-event.schema.json`
- `desktop-agent/ariadgsm_agent/trust_safety.py`
- `desktop-agent/tests/trust_safety_core.py`

Debe mantenerse como gate central de etapas posteriores.

### Etapa 12: Hands & Verification

Abrir chats, hacer scroll, escribir borradores, operar herramientas y verificar
que hizo lo correcto.

Estado actual: `cerrada como base final en 0.8.15`.

Existe:

- `docs/ARIADGSM_HANDS_ENGINE_ADVANCED_DESIGN.md`
- `desktop-agent/tests/hands_engine_advanced.py`
- `docs/ARIADGSM_HANDS_VERIFICATION_FINAL.md`
- `desktop-agent/contracts/hands-verification-state.schema.json`
- `desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Pipeline/HandsPipeline.cs`
- `desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Verification/ActionVerifier.cs`
- `desktop-agent/hands-engine/tests/AriadGSM.Hands.Tests/Program.cs`

Pendiente para autonomia final fuera de esta etapa:

- operar herramientas GSM reales bajo Tool Registry;
- verificar antes/despues con Safe Eyes.

### Etapa 13: Tool Registry

Herramientas GSM por capacidad: USB Redirector, programas, paneles, servidores,
proveedores, alternativas.

Estado actual: `cerrada como base final en 0.8.16`.

Existe:

- `docs/ARIADGSM_TOOL_REGISTRY_FINAL.md`
- `desktop-agent/contracts/tool-registry-state.schema.json`
- `desktop-agent/ariadgsm_agent/tool_registry.py`
- `desktop-agent/tool-registry/catalog.example.json`
- `desktop-agent/tests/tool_registry.py`

Quedo cerrado:

- inventario inicial de herramientas;
- capacidades, riesgos, entradas, salidas y verificadores;
- alternativas cuando una herramienta falla;
- planes de herramienta sin ejecucion directa;
- integracion con Trust & Safety y Hands por `decision_event`;
- pruebas sin acciones reales peligrosas.

### Etapa 14: Cloud Sync / ariadgsm.com

Panel, reportes, respaldos, sincronizacion, contabilidad y aprendizaje subido a
la nube.

Estado actual: `siguiente etapa pendiente despues de Runtime Kernel`.

Debe cerrar:

- sincronizacion de casos, memoria, contabilidad y auditoria;
- respaldo seguro;
- reportes para Bryams;
- no subir informacion sensible sin politica.

### Etapa 15: Evaluation + Release

Pruebas, metricas, instalador, updater, rollback, versiones y entrega estable.

Estado actual: `pendiente final`.

Debe cerrar:

- metricas de lectura, decision, accion y aprendizaje;
- instalador;
- updater final;
- rollback;
- pruebas largas;
- release estable.

## Orden bloqueado actual

La proxima etapa pendiente es:

```text
Etapa 14: Cloud Sync / ariadgsm.com
```

No se debe saltar a `Evaluation + Release`, updater final o ejecucion real de
herramientas GSM antes de cerrar esta etapa, salvo bloqueo real aprobado por
Bryams.
