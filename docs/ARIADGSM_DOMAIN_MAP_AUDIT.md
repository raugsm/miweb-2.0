# AriadGSM Domain Map Audit

Auditoria del `ARIADGSM_BUSINESS_DOMAIN_MAP.md` contra investigacion externa y objetivos reales de AriadGSM IA.

Fecha: 2026-04-27
Estado: auditoria de arquitectura

## 1. Resultado honesto

El mapa actual va en la direccion correcta: deja de pensar en bot y estructura AriadGSM como IA operativa de negocio.

Pero no esta completo todavia.

Lo que esta bien:

- Business Brain como cerebro gerente.
- Dominios como capacidades mentales especializadas.
- Guardrails como limites, no como pensamiento.
- Case Manager como centro operativo.
- Accounting Brain con evidencia.
- Channel Routing entre WhatsApps.
- Action Layer subordinada al cerebro.

Lo que falta reforzar:

- Evaluaciones continuas por version.
- Privacidad y minimizacion de datos.
- Contratos/eventos entre dominios.
- Orquestacion del ciclo autonomo.
- Correccion humana convertida en aprendizaje gobernado.
- Human-in-the-loop mas formal.
- Anti-corruption layer para WhatsApp Web, herramientas GSM y proveedores.

## 2. Fuentes usadas

Fuentes primarias consultadas:

- OpenAI Practical Guide to Building Agents.
- OpenAI Agent Evals.
- OpenAI Safety in Building Agents.
- Microsoft Agent Framework.
- Microsoft Domain Analysis / DDD.
- Microsoft UI Automation.
- NIST AI RMF Generative AI Profile.
- OWASP Top 10 for LLM Applications.
- IRS Publication 583 for business recordkeeping.
- OpenTelemetry documentation.

## 3. Hallazgos principales

### 3.1 La arquitectura de Business Brain es correcta

OpenAI recomienda comenzar con un agente capaz y dividir en agentes/capacidades especializadas cuando hay complejidad, exceso de herramientas o fallos de seleccion. AriadGSM ya tiene complejidad suficiente: clientes, pagos, precios, mercado, procedimientos, herramientas, derivacion entre canales y accion fisica.

Decision:

```text
Mantener Business Brain gerente con capacidades mentales especializadas.
```

### 3.2 Faltan Evals como dominio transversal

OpenAI recomienda evaluar agentes con trazas, graders, datasets y eval runs. Sin evaluaciones, no sabremos si una version mejoro o empeoro.

Riesgo actual:

```text
Publicar cambios por sensacion, no por evidencia.
```

Correccion:

Agregar `Evaluation Domain`.

Debe medir:

- si identifica cliente/proveedor/grupo correctamente
- si crea/fusiona casos bien
- si detecta pagos sin duplicar
- si deriva WhatsApps sin perder contexto
- si redacta respuestas correctas
- si bloquea acciones riesgosas
- si aprende sin contaminar memoria

### 3.3 Faltan contratos de eventos entre dominios

Microsoft DDD recomienda bounded contexts y lenguaje publicado. OpenAI safety recomienda structured outputs para limitar errores e inyecciones. AriadGSM necesita contratos claros para que cada dominio se comunique con datos validados, no texto libre.

Riesgo actual:

```text
Un dominio puede pasar texto ambiguo a otro y causar decisiones falsas.
```

Correccion:

Agregar `Domain Event Contracts`.

Ejemplos:

```text
CustomerIdentified
CaseOpened
PaymentDrafted
RouteRecommended
QuoteProposed
ToolActionRequested
HumanApprovalRequired
LearningCandidateCreated
```

### 3.4 Falta privacidad y data governance como dominio propio

OpenAI y Microsoft remarcan cuidado con datos privados y terceros. OWASP incluye exposicion de informacion sensible. AriadGSM maneja clientes, telefonos, pagos, cuentas, proveedores, posibles IMEI, comprobantes y conversaciones.

Riesgo actual:

```text
La IA aprende o sube mas datos de los necesarios.
```

Correccion:

Agregar `Privacy & Data Governance Domain`.

Debe razonar:

- que dato puede guardarse
- que dato debe redactarse
- que dato puede subir a la nube
- que dato nunca debe enviarse a otro cliente/proveedor
- que evidencia debe conservarse
- por cuanto tiempo

### 3.5 Falta orquestacion explicita del ciclo autonomo

Microsoft Agent Framework separa agentes y workflows. Para AriadGSM, algunas partes son abiertas y requieren IA; otras son flujos con estados y checkpoints. El mapa habla del flujo maestro, pero no tiene un dominio que gobierne ciclo, pausas, reintentos, checkpoints y recuperacion.

Riesgo actual:

```text
El sistema actua, falla, queda a medias o repite acciones.
```

Correccion:

Agregar `Autonomous Cycle Orchestrator`.

Debe gobernar:

- observar
- entender
- decidir
- pedir permiso
- actuar
- verificar
- aprender
- pausar
- reintentar
- recuperarse

### 3.6 Falta human-in-the-loop formal

El mapa menciona permisos, pero falta un dominio mental de colaboracion con Bryams. La IA no debe solo pedir permiso; debe aprender de la correccion y explicar opciones.

Correccion:

Agregar `Human Collaboration Domain`.

Debe manejar:

- pedir aprobacion
- presentar opciones
- explicar por que duda
- recibir correccion
- convertir correccion en aprendizaje candidato
- recordar preferencias de Bryams

### 3.7 Falta anti-corruption layer para sistemas externos

Microsoft DDD recomienda proteger el modelo del negocio de sistemas externos. AriadGSM depende de WhatsApp Web, navegadores, herramientas GSM, proveedores, OCR, API/nube.

Riesgo actual:

```text
Un cambio de WhatsApp Web o de una tool contamina el modelo central.
```

Correccion:

Agregar `External System Adapter / Anti-Corruption Layer`.

Debe traducir:

- WhatsApp Web UI -> mensajes/canales/casos
- herramienta GSM -> capacidad/estado/resultado
- proveedor -> oferta/costo/confiabilidad
- OCR -> observacion incierta
- nube -> evento sincronizable

## 4. Veredicto

El mapa actual no esta mal.

Pero para lograr la IA que Bryams busca, debe agregarse una capa transversal de control:

```text
Evaluation
Privacy
Domain Event Contracts
Autonomous Cycle Orchestrator
Human Collaboration
Anti-Corruption Layer
```

Sin esto, el proyecto puede volver a caer en parches.

Con esto, la arquitectura queda mucho mas cerca de una IA operativa real:

```text
Cerebro de negocio
+ capacidades mentales
+ memoria
+ contratos
+ evaluacion
+ privacidad
+ humano en el ciclo
+ accion verificada
```

