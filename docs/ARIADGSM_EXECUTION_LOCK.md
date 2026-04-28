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

### 6.0.5 Runtime Kernel

Estado:

```text
CERRADA COMO BASE FINAL EN 0.8.17
```

Objetivo:

Ser la verdad unica local sobre vida de motores, incidentes, reinicios,
capacidad de actuar, cabina, input humano y recuperacion. Esta etapa fue
insertada por autorizacion directa de Bryams antes de Cloud Sync para evitar que
la nube consuma estados contradictorios.

Definicion de terminado:

- existe documento final;
- existe contrato `runtime_kernel_state`;
- la app Windows publica `runtime-kernel-state.json`;
- la UI usa Runtime Kernel para explicar estado humano;
- Vision no cae por `Access denied` al escribir estado;
- los incidentes recientes se normalizan en vez de esconderse en logs;
- Cloud Sync consume este estado como verdad unica local;
- tiene pruebas repetibles.

Existe:

- `docs/ARIADGSM_RUNTIME_KERNEL_FINAL.md`
- `desktop-agent/contracts/runtime-kernel-state.schema.json`
- `desktop-agent/ariadgsm_agent/runtime_kernel.py`
- `desktop-agent/tests/runtime_kernel.py`
- integracion en `AgentRuntime.cs`, `AgentRuntime.RuntimeKernel.cs` y UI

Pendiente para autonomia final:

- enriquecer incidentes con trazas OpenTelemetry completas;
- ampliar metricas en Evaluation + Release.

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
CERRADA COMO BASE READER CORE 0.8.11
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

Ya existe:

- `docs/ARIADGSM_SAFE_EYES_READER_CORE_DESIGN.md`
- `desktop-agent/contracts/visible-message.schema.json`
- `desktop-agent/contracts/reader-core-state.schema.json`
- `desktop-agent/ariadgsm_agent/reader_core.py`
- `desktop-agent/tests/safe_eyes_reader_core.py`
- integracion de `ReaderCore` antes de `Timeline` en `AgentRuntime.cs`.

Pendiente para autonomia final:

- adaptadores vivos DOM/CDP/UIA por navegador;
- validacion prolongada con sesiones reales de Edge, Chrome y Firefox;
- metricas de latencia mensaje nuevo -> evento de negocio.

### 6.8.5 Window Reality Resolver

Estado:

```text
CERRADA COMO CAPA TRANSVERSAL EN 0.9.1
```

Objetivo:

Resolver la diferencia entre lo que Windows cree, lo que la pantalla muestra y
lo que la IA puede leer u operar. Ningun WhatsApp queda listo por una sola
senal: se fusiona identidad estructural, geometria visual, lectura semantica,
frescura y accionabilidad.

Existe:

- `docs/ARIADGSM_WINDOW_REALITY_RESOLVER_FINAL.md`
- `desktop-agent/contracts/window-reality-state.schema.json`
- `desktop-agent/ariadgsm_agent/window_reality.py`
- `desktop-agent/tests/window_reality_resolver.py`
- integracion con `AgentRuntime.cs`, Runtime Kernel y Autonomous Cycle

Impacto en etapas:

- Etapa 7: Cabin Authority publica proyeccion estructural inmediata.
- Etapa 8: Reader Core aporta evidencia semantica.
- Etapa 11: Trust & Safety bloquea manos si no hay realidad operable.
- Etapa 12: Hands solo actua si `handsMayAct=true`.
- Etapa 15: Evaluation + Release valida contrato, documento y paquete.

### 6.8.6 Support & Telemetry Core

Estado:

```text
CERRADA COMO CAPA TRANSVERSAL EN 0.9.2
```

Objetivo:

Crear soporte formal, telemetria, caja negra local y diagnostico humano para
entender por que AriadGSM IA Local observa, decide, falla, se detiene, no actua
o necesita ayuda.

Existe:

- `docs/ARIADGSM_SUPPORT_TELEMETRY_CORE_FINAL.md`
- `desktop-agent/contracts/support-telemetry-state.schema.json`
- `desktop-agent/contracts/support-telemetry-event.schema.json`
- `desktop-agent/ariadgsm_agent/support_telemetry.py`
- `desktop-agent/tests/support_telemetry_core.py`
- integracion con `AgentRuntime.cs`, Runtime Kernel, Cloud Sync y Evaluation

Regla de privacidad:

- no subir capturas, chats completos, tokens, contrasenas, cookies ni dumps;
- la nube recibe solo resumen seguro;
- el Support Bundle queda local y requiere permiso explicito antes de enviarse.

### 6.9 Living Memory

Estado:

```text
CERRADA COMO BASE OPERATIVA EN 0.8.12
```

Objetivo:

Convertir lo leido por Reader Core, Timeline y Domain Events en memoria viva:

- episodica: que paso en cada conversacion y caso;
- semantica: clientes, servicios, paises, precios y patrones;
- procedimental: como se hacen trabajos y como se corrigen errores;
- contable: pagos, deudas, reembolsos y evidencia;
- incertidumbre: que sabe, que duda y que debe preguntarte.

Definicion de terminado:

- existe documento de diseno;
- existe contrato de memoria viva;
- separa hechos, sospechas, procedimientos, estilo y contabilidad;
- aprende de correcciones humanas;
- degrada conocimiento inseguro;
- explica que aprendio y de donde;
- tiene pruebas repetibles con eventos del Reader Core.

Ya existe:

- `docs/ARIADGSM_LIVING_MEMORY_DESIGN.md`
- `desktop-agent/contracts/living-memory-state.schema.json`
- memoria viva integrada en `desktop-agent/ariadgsm_agent/memory.py`
- `desktop-agent/tests/living_memory.py`
- ingestion de `conversation_event`, `learning_event`, `accounting_event`,
  `domain_event` y `human_feedback_event`.

Pendiente para autonomia final:

- que Business Brain consulte esta memoria antes de cotizar, priorizar o
  proponer respuestas;
- medicion prolongada con datos reales;
- recuperacion semantica/vectorial futura cuando crezca el volumen.

### 6.10 Business Brain

Estado:

```text
CERRADA COMO BASE OPERATIVA EN 0.8.13
```

Objetivo:

Crear el cerebro de negocio que use Living Memory para razonar sobre clientes,
servicios GSM, precios, paises, proveedores, herramientas, riesgos, mercado,
contabilidad y estilo AriadGSM.

Definicion de terminado:

- existe documento de diseno;
- consulta Living Memory con evidencia;
- separa decision de negocio de accion fisica;
- propone respuestas, cotizaciones y derivaciones con incertidumbre explicita;
- no envia ni ejecuta acciones de riesgo sin Trust & Safety;
- tiene pruebas sin WhatsApps reales ni herramientas GSM reales.

Ya existe:

- `docs/ARIADGSM_BUSINESS_BRAIN_DESIGN.md`
- `desktop-agent/contracts/business-brain-state.schema.json`
- `desktop-agent/ariadgsm_agent/business_brain.py`
- `desktop-agent/tests/business_brain.py`
- integracion en el ciclo local despues de Living Memory y antes de Trust &
  Safety.

Pendiente para autonomia final:

- conectar Tool Registry para escoger herramientas GSM reales;
- integrar modelos LLM locales/cloud para razonamiento flexible bajo contrato;
- evaluacion prolongada con conversaciones reales;
- politica final de respuestas automaticas por nivel de autonomia.

### 6.11 Trust & Safety + Input Arbiter

Estado:

```text
CERRADA COMO BASE FINAL EN 0.8.14
```

Objetivo:

Cerrar la capa de permisos, niveles de autonomia, arbitraje de mouse/teclado y
confirmaciones humanas para que Business Brain pueda pasar de propuesta a
accion segura sin pelear con Bryams ni ejecutar riesgos.

Definicion de terminado:

- Trust & Safety evalua decisiones de Cognitive, Operating y Business Brain;
- Input Arbiter protege el control humano del mouse/teclado;
- se separan acciones permitidas, limitadas, pausadas y bloqueadas;
- toda accion riesgosa exige evidencia y confirmacion;
- pruebas repetibles cubren propuestas de negocio, manos y control humano.

Ya existe:

- `docs/ARIADGSM_TRUST_SAFETY_INPUT_ARBITER_FINAL.md`
- `docs/ARIADGSM_TRUST_SAFETY_CORE_DESIGN.md`
- `desktop-agent/contracts/trust-safety-state.schema.json`
- `desktop-agent/contracts/input-arbiter-state.schema.json`
- `desktop-agent/contracts/safety-approval-event.schema.json`
- `desktop-agent/ariadgsm_agent/trust_safety.py`
- `desktop-agent/tests/trust_safety_core.py`
- gate de `Hands` contra `trust-safety-state.json`;
- `Input Arbiter` con lease, dueno activo, cooldown y continuidad de motores.

Pendiente para autonomia final:

- pantalla final para aprobar/revocar eventos `safety_approval_event`;
- validacion prolongada con mouse/teclado real durante jornada completa;
- telemetria cloud de aprobaciones cuando toque Etapa 14.

### 6.12 Hands & Verification

Estado:

```text
CERRADA COMO BASE FINAL EN 0.8.15
```

Objetivo:

Cerrar manos verificadas: abrir chats, hacer scroll, escribir borradores,
operar herramientas y confirmar que la accion correcta ocurrio antes de seguir.

Definicion de terminado:

- Hands consume solo acciones autorizadas por Trust & Safety;
- cada accion fisica tiene verificacion posterior;
- abrir chat confirma canal, titulo y fila correcta;
- escribir texto no envia sin aprobacion;
- errores quedan explicados en estado humano y action events.

Existe:

- `docs/ARIADGSM_HANDS_VERIFICATION_FINAL.md`
- `desktop-agent/contracts/hands-verification-state.schema.json`
- `desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Pipeline/HandsPipeline.cs`
- `desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Verification/ActionVerifier.cs`
- `desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Safety/HandsSafetyPolicy.cs`
- `desktop-agent/hands-engine/tests/AriadGSM.Hands.Tests/Program.cs`
- `desktop-agent/tests/hands_engine_advanced.py`

Pendiente para autonomia final:

- registrar herramientas GSM reales en Tool Registry;
- validar envios reales solo despues de Evaluation + Release;
- extender verificacion por UI Automation especifica de cajas de texto cuando
  Tool Registry habilite herramientas externas.

### 6.13 Tool Registry

Estado:

```text
CERRADA COMO BASE FINAL EN 0.8.16
```

Objetivo:

Registrar herramientas GSM por capacidad, riesgo, entradas, salidas,
verificadores y alternativas para que Business Brain pueda planear como IA de
negocio sin codificar un parche por cada programa.

Definicion de terminado:

- existe documento final;
- existe contrato `tool_registry_state`;
- existe catalogo editable de herramientas;
- las herramientas se eligen por capacidad, no por nombre de programa;
- cada herramienta declara riesgo, verificadores y fallos;
- produce planes de herramienta como decisiones auditables;
- no ejecuta herramientas externas directamente;
- integra con Trust & Safety y Hands & Verification;
- tiene pruebas sin acciones reales peligrosas.

Existe:

- `docs/ARIADGSM_TOOL_REGISTRY_FINAL.md`
- `desktop-agent/contracts/tool-registry-state.schema.json`
- `desktop-agent/ariadgsm_agent/tool_registry.py`
- `desktop-agent/tool-registry/catalog.example.json`
- `desktop-agent/tests/tool_registry.py`
- integracion en `AgentRuntime.cs` y `autonomous_cycle.py`

Pendiente para autonomia final:

- cargar el inventario real de herramientas, proveedores, paneles y licencias
  de AriadGSM;
- medir fallos reales por herramienta y degradarlas automaticamente con Living
  Memory;
- permitir ejecucion externa solo en etapas superiores, con aprobacion por
  accion y verificacion.

### 6.14 Cloud Sync / ariadgsm.com

Estado:

```text
CERRADA COMO BASE FINAL EN 0.8.18
```

Objetivo:

Conectar la cabina local con `ariadgsm.com` como panel, respaldo, reportes,
sincronizacion y auditoria, sin convertir la nube en cerebro y sin subir
capturas o secretos.

Definicion de terminado:

- existe documento final;
- existe contrato `cloud_sync_state`;
- existe motor local `cloud_sync.py`;
- Runtime Kernel conoce Cloud Sync como motor;
- la app Windows muestra Cloud Sync en salud y actividad;
- el ciclo Python ejecuta Cloud Sync al final;
- `ariadgsm.com` recibe lotes con idempotencia;
- el panel distingue lote nuevo, duplicado y eventos rechazados;
- hay pruebas de contrato, dry-run, endpoint local e idempotencia;
- se empaqueta una version nueva.

Existe:

- `docs/ARIADGSM_CLOUD_SYNC_ARIADGSM_COM_FINAL.md`
- `desktop-agent/contracts/cloud-sync-state.schema.json`
- `desktop-agent/ariadgsm_agent/cloud_sync.py`
- `desktop-agent/tests/cloud_sync.py`
- `scripts/test-cloud-sync-server.js`
- integracion en `AgentRuntime.cs`, `runtime_kernel.py`, `server-wrapper.js`,
  `operativa-store.js` y `public/operativa-v2.js`

Pendiente para autonomia final:

- validar corrida larga contra produccion dentro de Evaluation + Release;
- consolidar metricas OpenTelemetry;
- cerrar instalador, updater, rollback y release estable.

### 6.15 Evaluation + Release

Estado:

```text
CERRADA COMO RELEASE CANDIDATE EN 0.9.2
```

Objetivo:

Cerrar pruebas, metricas, ownership de procesos, checkpoints, instalador,
updater, rollback, versionado y paquete para que AriadGSM IA Local pueda pasar
a prueba real supervisada sin volver al patron de parches.

Definicion de terminado:

- Runtime Governor controla solo procesos propios AriadGSM;
- Edge, Chrome, Firefox, WhatsApp, Codex y sesiones humanas quedan fuera de
  ownership;
- Windows Job Object se usa como barrera contra workers propios huerfanos;
- existen checkpoints durables;
- Evaluation Harness valida contratos y escenarios de negocio;
- Trace Grading resume logs y fallos como evidencia calificable;
- Updater valida version, paquete, SHA256 y rollback;
- Long-run simulado pasa;
- Release Candidate queda empaquetado y versionado.

Existe:

- `docs/ARIADGSM_EVALUATION_RELEASE_FINAL.md`
- `desktop-agent/contracts/runtime-governor-state.schema.json`
- `desktop-agent/contracts/evaluation-release-state.schema.json`
- `desktop-agent/contracts/window-reality-state.schema.json`
- `desktop-agent/contracts/support-telemetry-state.schema.json`
- `desktop-agent/contracts/support-telemetry-event.schema.json`
- `desktop-agent/ariadgsm_agent/runtime_governor.py`
- `desktop-agent/ariadgsm_agent/window_reality.py`
- `desktop-agent/ariadgsm_agent/support_telemetry.py`
- `desktop-agent/ariadgsm_agent/release_evaluation.py`
- `desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.ProcessGovernor.cs`
- `desktop-agent/tests/evaluation_release.py`
- `desktop-agent/tests/support_telemetry_core.py`

Pendiente fuera de construccion:

- prueba real supervisada de cabina completa;
- corrida larga real con los 3 WhatsApps antes de declarar estable productivo.

## 7. Bloques que NO pueden adelantarse

La construccion de etapas queda cerrada como release candidate. Lo que no puede
adelantarse ahora es:

- ejecucion real no supervisada de herramientas GSM;
- envio automatico de respuestas sin permiso;
- declarar produccion estable sin prueba real supervisada;
- abrir nuevas etapas por intuicion sin cambiar Execution Lock.

Motivo:

El producto ya tiene columna vertebral de IA operadora, pero un release
candidate debe demostrar en la PC real que ojos, memoria, manos, seguridad,
cloud sync y release trabajan juntos sin cerrar navegadores ni pelear con el
operador.

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

Como las etapas `0`, `0.5` y `1` a `15` quedan cerradas como base operativa, la
siguiente accion no es otra etapa de construccion: es validar el release
candidate en la PC real.

```text
Release Candidate 0.9.2: prueba real supervisada de cabina completa
```

Alcance:

```text
arrancar app desde paquete 0.9.2
iniciar sesion
alistar Edge=wa-1, Chrome=wa-2, Firefox=wa-3 sin cerrar ventanas
encender IA
observar Window Reality Resolver, lectura, razonamiento, permisos, manos y sincronizacion
revisar Evaluation Release, Runtime Kernel, Runtime Governor y Support & Telemetry
aceptar o rechazar el release candidate con evidencia
```

Que no se debe hacer aqui:

```text
inventar una etapa nueva
parchar un fallo puntual sin revisar la traza de release
habilitar acciones GSM reales sin aprobacion humana
```
