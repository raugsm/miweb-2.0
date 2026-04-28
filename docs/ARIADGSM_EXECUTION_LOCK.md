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
2. `docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md`
3. `docs/ARIADGSM_FINAL_PRODUCT_BLUEPRINT.md`
4. `docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS.md`
5. `docs/ARIADGSM_BUSINESS_DOMAIN_MAP.md`
6. `docs/ARIADGSM_BUSINESS_OPERATING_MODEL.md`
7. `docs/ARIADGSM_AUTONOMOUS_OPERATING_SYSTEM_1.0.md`
8. Documentos tecnicos por motor dentro de `desktop-agent/`

Si hay conflicto, manda este archivo.

## 5. Orden bloqueado de la nueva etapa

Este es el orden que Bryams marco y queda bloqueado:

```text
0. Product Foundation
1. Domain Event Contracts
2. Autonomous Cycle Orchestrator
3. Case Manager
4. Channel Routing Brain
5. Accounting Core evidence-first
```

Ningun bloque posterior puede reemplazar este orden sin aprobacion.

## 6. Estado actual de los bloques

### 6.0 Product Foundation

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

## 7. Bloques que NO pueden adelantarse

Estos bloques son importantes, pero no deben sustituir el orden de la nueva
etapa salvo que Bryams lo autorice:

- Product Shell visual final.
- Cabin Authority final.
- Trust & Safety Core completo.
- Hands Engine avanzado.
- Updater final.
- Cloud Sync final.

Motivo:

Son necesarios para producto, pero si se hacen antes de cerrar la columna mental
del negocio, volvemos al patron de parches.

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

Como `Product Foundation`, `Domain Event Contracts`, `Case Manager`,
`Channel Routing Brain` y `Accounting Core evidence-first` quedan cerrados, la
columna mental inicial del negocio queda completa.

El bloque recomendado para producto final ya cerrado en `0.8.6` es:

```text
Cabin Authority final
```

El entregable documental minimo es:

```text
docs/ARIADGSM_CABIN_AUTHORITY_FINAL_DESIGN.md
```

Y el entregable tecnico minimo posterior es:

```text
alistamiento determinista de Edge/Chrome/Firefox
verificacion de 3 WhatsApps visibles sin cerrar ventanas del usuario
estado visible para Bryams antes de Encender IA
pruebas sin mover/cerrar sesiones reales
```

Queda cerrado con:

- `docs/ARIADGSM_CABIN_AUTHORITY_FINAL_DESIGN.md`
- `desktop-agent/contracts/cabin-authority-state.schema.json`
- selector de pestanas seguro solo `TabItem`
- apertura por ejecutable exacto de Edge/Chrome/Firefox
- perfil fijo desactivado por defecto
- prueba `desktop-agent/tests/cabin_authority_final.py`

El bloque recomendado para producto final ya cerrado en `0.8.7` es:

```text
Hands Engine avanzado + accion sobre chat verificada
```

El entregable documental minimo es:

```text
docs/ARIADGSM_HANDS_ENGINE_ADVANCED_DESIGN.md
```

Y el entregable tecnico minimo posterior es:

```text
abrir chat visible con confirmacion de chat correcto
ceder mouse al operador sin apagar ojos ni memoria
verificar accion por percepcion antes de continuar
reportar fallo accionable en lenguaje humano
pruebas sin depender de sesiones reales
```

Queda cerrado con:

- `docs/ARIADGSM_HANDS_ENGINE_ADVANCED_DESIGN.md`
- `desktop-agent/tests/hands_engine_advanced.py`
- `SendInput` en Hands para mouse en vez de `mouse_event`
- verificacion fresca de Perception antes de continuar despues de `open_chat`
- fallo `failed` y suspension de acciones dependientes si el chat visible no
  coincide con el esperado
- Input Arbiter con cesion de mouse al operador sin apagar ojos, memoria ni
  cognicion
- pruebas C# de verificacion correcta, chat equivocado y prioridad humana

El bloque recomendado para producto final ya cerrado en `0.8.8` es:

```text
Trust & Safety Core completo
```

El entregable documental minimo es:

```text
docs/ARIADGSM_TRUST_SAFETY_CORE_DESIGN.md
```

Y el entregable tecnico minimo posterior es:

```text
politica central de niveles de autonomia
matriz de riesgo por accion de negocio
permisos explicitos para enviar mensajes, tocar herramientas y registrar pagos
bloqueo verificable de acciones irreversibles
reporte humano de por que una accion fue permitida, pausada o bloqueada
pruebas sin ejecutar acciones reales sobre clientes
```

Queda cerrado con:

- `docs/ARIADGSM_TRUST_SAFETY_CORE_DESIGN.md`
- `desktop-agent/ariadgsm_agent/trust_safety.py`
- `desktop-agent/contracts/trust-safety-state.schema.json`
- `desktop-agent/tests/trust_safety_core.py`
- politica central de autonomia 1 a 6
- matriz de riesgo por capacidad operativa
- permisos explicitos para mensajes, herramientas, contabilidad y rutas
- bloqueo de acciones irreversibles sin permiso/evidencia
- Supervisor compatible consumiendo Trust & Safety
- estado visible `trust-safety-state.json` en la app

El siguiente bloque recomendado para producto final es:

```text
Product Shell visual final
```

El entregable documental minimo es:

```text
docs/ARIADGSM_PRODUCT_SHELL_FINAL_DESIGN.md
```

Y el entregable tecnico minimo posterior es:

```text
interfaz menos tecnica y mas operativa para Bryams
estado humano de IA, seguridad, cabina, memoria y manos
inicio manual claro despues de login
progreso real para alistar WhatsApps
errores accionables sin cajas tecnicas largas
pruebas visuales sin mover sesiones reales
```

Queda cerrado con:

- `docs/ARIADGSM_PRODUCT_SHELL_FINAL_DESIGN.md`
- `desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/MainForm.cs`
- `desktop-agent/tests/product_shell_visual_final.py`
- login con inicio manual explicito
- version visible en cabina
- progreso de alistamiento monotono de 0 a 100
- estado humano por areas: cabina, ojos, cerebro/memoria, contabilidad,
  seguridad, manos, nube/panel
- Trust & Safety visible en tarjetas y salud operativa
- errores accionables sin tabla tecnica larga
- la app solo se minimiza si no hay errores ni avisos visibles

El siguiente bloque recomendado para producto final es:

```text
Updater final
```
