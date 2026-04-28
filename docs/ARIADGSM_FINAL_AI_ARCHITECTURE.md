# AriadGSM Final AI Architecture

Fecha: 2026-04-28
Estado: autoridad madre de arquitectura

## 1. Proposito

Este documento congela la arquitectura final de `AriadGSM IA Local`.

Desde este punto, Codex no debe proponer parches, capas nuevas ni cambios de
rumbo por intuicion. Toda recomendacion o implementacion debe:

1. ubicarse dentro de una de las 8 capas finales;
2. indicar capas relacionadas;
3. revisar fuentes externas confiables cuando el cambio sea importante;
4. contrastar la decision con el negocio real de AriadGSM;
5. explicar por que no es un parche;
6. terminar con el siguiente pedido exacto recomendado.

El objetivo sigue siendo:

```text
Una IA operadora de AriadGSM, no un bot de WhatsApp.
```

La IA debe observar, entender, recordar, decidir, pedir permiso cuando haya
riesgo, actuar con herramientas, verificar y aprender de correcciones.

## 2. Fuentes usadas

La arquitectura se basa en investigacion externa y en la app actual.

### Agentes IA

- OpenAI Practical Guide to Building Agents:
  https://openai.com/business/guides-and-resources/a-practical-guide-to-building-ai-agents/
- Microsoft AI Agent Orchestration Patterns:
  https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns

Aplicacion a AriadGSM:

- un agente no es un clasificador aislado;
- debe tener modelo, herramientas, instrucciones, guardrails y ciclo de run;
- el manager/orchestrator conserva control y delega a capacidades;
- las herramientas deben estar documentadas, probadas y autorizadas.

### Riesgo, seguridad y confianza

- NIST AI Risk Management Framework:
  https://www.nist.gov/itl/ai-risk-management-framework
- OWASP Top 10 for LLM Applications:
  https://owasp.org/www-project-top-10-for-large-language-model-applications/

Aplicacion a AriadGSM:

- clientes no pueden cambiar reglas internas con mensajes;
- memoria no acepta cualquier texto como verdad;
- acciones como enviar, cobrar, registrar pagos o usar herramientas GSM
  requieren permisos y evidencia;
- privacidad, auditoria y explicabilidad son parte del producto, no extras.

### Runtime y ciclo de vida

- .NET Generic Host:
  https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host
- LangGraph Durable Execution:
  https://docs.langchain.com/oss/python/langgraph/durable-execution
- Temporal Durable Execution:
  https://temporal.io/home

Aplicacion a AriadGSM:

- iniciar y detener IA debe ser un protocolo de vida, no un boton llamando
  directo a motores;
- cada encendido necesita `runSessionId`;
- cada fase debe poder reintentarse, bloquearse o recuperarse con checkpoint.

### Ojos y escritorio Windows

- Microsoft UI Automation:
  https://learn.microsoft.com/en-us/windows/win32/winauto/entry-uiauto-win32
- UI Automation Control Patterns:
  https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview
- WinEvents / SetWinEventHook:
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
- Windows Graphics Capture:
  https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture
- Playwright Actionability:
  https://playwright.dev/docs/actionability

Aplicacion a AriadGSM:

- no basta con ver un titulo de ventana;
- el sistema debe fusionar ventana, foco, geometria, UIA, lectura semantica,
  frescura y accionabilidad;
- antes de actuar debe validar que el objetivo esta visible, estable, habilitado
  y puede recibir accion;
- OCR es respaldo, no la verdad principal.

### Observabilidad y soporte

- OpenTelemetry:
  https://opentelemetry.io/docs/
- Windows Error Reporting LocalDumps:
  https://learn.microsoft.com/en-us/windows/win32/wer/collecting-user-mode-dumps

Aplicacion a AriadGSM:

- logs, trazas y metricas deben contar una misma historia;
- `traceId`, `correlationId` y `runSessionId` son obligatorios;
- dumps, capturas, chats completos, tokens o passwords no se suben sin permiso.

## 3. Decision arquitectonica final

La arquitectura final tiene 8 capas. No se agregan mas capas por fallos nuevos.

Cuando aparezca un problema, se ubica dentro de una de estas capas y se corrige
alli. Las 15 etapas anteriores quedan como entregables internos e historial de
construccion, no como arquitectura viva principal.

```text
1. Operator Product Shell
2. AI Runtime Control Plane
3. Cabin Reality Authority
4. Perception & Reader Core
5. Event, Timeline & Durable State Backbone
6. Living Memory & Business Brain
7. Action, Tools & Verification
8. Trust, Telemetry, Evaluation & Cloud
```

## 4. Las 8 capas definitivas

### Capa 1: Operator Product Shell

Responsabilidad:

- login;
- identidad de operador;
- version visible;
- botones humanos minimos;
- progreso real;
- estados claros;
- actividad entendible;
- errores explicados sin ruido tecnico.

Problemas que resuelve:

- interfaz demasiado tecnica;
- usuario sin saber si la IA esta viva, pausada o bloqueada;
- fallos invisibles cuando la ventana se minimiza;
- confusion entre cabina, ojos, manos, nube y runtime.

Componentes actuales:

- `MainForm.cs`;
- login local/nube;
- pantalla de estado;
- version y updater visible;
- reportes humanos del Runtime Kernel y Support Telemetry.

Estado actual:

```text
BASE FUNCIONAL, NO CONSOLIDADA FINAL
```

Para consolidar:

- esconder tablas tecnicas como vista principal;
- mostrar flujo humano: login -> alistar cabina -> encender IA -> actividad;
- explicar incidentes por capa;
- mostrar progreso que no retrocede;
- ofrecer diagnostico sin obligar a leer logs.

### Capa 2: AI Runtime Control Plane

Responsabilidad:

- protocolo de inicio;
- protocolo de pausa;
- actualizaciones;
- ownership de procesos;
- `runSessionId`;
- command ledger;
- checkpoints;
- recuperacion;
- diferencia entre app cerrada, IA pausada, update, fallo, dispose y boton.

Problemas que resuelve:

- "encendi la IA y se cayo";
- `operator_button` ambiguo;
- estados viejos que bloquean todo;
- UI llamando directo a `Start()` o `Stop()`;
- motores vivos sin autoridad comun;
- procesos propios huerfanos.

Componentes actuales a fusionar:

- Life Controller;
- Runtime Kernel;
- Runtime Governor;
- Workspace Guardian;
- Agent Supervisor;
- updater state;
- launch state;
- parte de Evaluation Release.

Estado actual:

```text
BASE AVANZADA, NO CONSOLIDADA FINAL
```

Para consolidar:

- toda orden debe pasar por Control Plane;
- cada orden debe guardar `commandId`, `runSessionId`, origen, control UI,
  foco, momento y resultado;
- lectura, pensamiento y accion deben tener readiness separada;
- si Hands falla, ojos y memoria siguen;
- detener IA requiere causa verificable, no etiqueta generica.

### Capa 3: Cabin Reality Authority

Responsabilidad:

- Edge = `wa-1`;
- Chrome = `wa-2`;
- Firefox = `wa-3`;
- ventanas, perfiles, URL, bounds, z-order y foco;
- no cerrar navegadores;
- no abrir WhatsApp instalado de Windows;
- cajas fijas;
- estado visual/semantico/accionable.

Problemas que resuelve:

- Edge o Chrome cerrados por error;
- abrir WhatsApp equivocado;
- confundir ventana tapada con ventana lista;
- detectar WhatsApp por titulo solamente;
- mover una ventana y dejar solo 2 canales visibles.

Componentes actuales a fusionar:

- Cabin Manager;
- Cabin Authority;
- Window Reality Resolver;
- partes de Vision/Perception relacionadas a geometria de ventana.

Estado actual:

```text
BASE FUERTE, NO VALIDADA EN JORNADA REAL
```

Para consolidar:

- una sola autoridad sobre ventanas;
- lectura permitida aunque manos no puedan actuar;
- pruebas reales con varias aplicaciones abiertas;
- ningun motor excepto Cabin puede mover/restaurar navegadores.

### Capa 4: Perception & Reader Core

Responsabilidad:

- leer WhatsApp como objetos reales;
- mensajes visibles;
- chat, remitente, hora, direccion, texto, confianza;
- DOM/accesibilidad/UI Automation primero;
- OCR y vision como respaldo;
- separacion de ruido y contenido util.

Problemas que resuelve:

- OCR leyendo horas sueltas, titulos o interfaz;
- filtros infinitos;
- leer Codex, YouTube o Railway por error;
- falta de precision para pagos, precios y deudas;
- dependencia de screenshots como unica fuente.

Componentes actuales a fusionar:

- Vision Engine;
- Perception Engine;
- Reader Core;
- Interaction Engine cuando produce targets visibles.

Estado actual:

```text
BASE OPERATIVA, NO OJOS DEFINITIVOS
```

Para consolidar:

- objeto `VisibleMessage` como salida obligatoria;
- adaptadores por navegador/fuente;
- comparador entre UIA/DOM/OCR;
- score de confianza;
- errores humanos claros cuando no puede leer.

### Capa 5: Event, Timeline & Durable State Backbone

Responsabilidad:

- contratos internos;
- eventos de dominio;
- timeline por cliente/caso;
- checkpoints;
- SQLite como verdad durable;
- JSON solo como proyeccion humana;
- trazabilidad por `runSessionId`, `traceId` y `correlationId`.

Problemas que resuelve:

- estados repartidos en muchos JSON;
- memoria y contabilidad leyendo fuentes diferentes;
- no saber que vio la IA antes de decidir;
- perder contexto tras reinicio;
- eventos sin causalidad.

Componentes actuales a fusionar:

- Domain Event Contracts;
- Timeline;
- durable checkpoints;
- domain-events SQLite/jsonl;
- parte de Support Telemetry.

Estado actual:

```text
BASE SOLIDA, FALTA HACERLA VERDAD PRINCIPAL
```

Para consolidar:

- store local transaccional como fuente viva;
- proyecciones JSON derivadas;
- todo evento con sesion, causa y fuente;
- replay de jornada para evaluacion.

### Capa 6: Living Memory & Business Brain

Responsabilidad:

- memoria episodica;
- memoria semantica;
- memoria procedimental;
- memoria contable;
- clientes;
- precios;
- servicios GSM;
- paises;
- jergas;
- rutas entre WhatsApps;
- proveedores;
- mercado;
- razonamiento LLM bajo contrato.

Problemas que resuelve:

- IA que solo clasifica mensajes;
- reglas de negocio fragiles;
- no aprender de clientes reales;
- no explicar que aprendio;
- contabilidad sin contexto;
- derivaciones entre WhatsApps sin criterio.

Componentes actuales a fusionar:

- Living Memory;
- Business Brain;
- Cognitive Core;
- Operating Core;
- Case Manager;
- Channel Routing;
- Accounting Core.

Estado actual:

```text
BASE OPERATIVA, AUN NO IA DE NEGOCIO COMPLETA
```

Para consolidar:

- Business Brain debe usar LLM con herramientas y guardrails;
- decisiones con incertidumbre;
- aprendizajes confirmados, dudosos y corregidos;
- casos reales como unidad de trabajo;
- contabilidad evidence-first;
- evaluaciones con conversaciones GSM reales.

### Capa 7: Action, Tools & Verification

Responsabilidad:

- abrir chats;
- scroll;
- capturar historial;
- escribir borradores;
- operar herramientas GSM;
- usar Tool Registry;
- verificar antes y despues;
- no enviar ni registrar final sin permiso.

Problemas que resuelve:

- mouse lento o peleando con Bryams;
- clics en chat equivocado;
- acciones sin confirmacion;
- herramientas GSM tratadas como reglas sueltas;
- no saber si una accion funciono.

Componentes actuales a fusionar:

- Hands Engine;
- Hands Verification;
- Action Queue;
- Tool Registry;
- verificadores de herramientas.

Estado actual:

```text
BASE SEGURA, NO AUTONOMIA FINAL
```

Para consolidar:

- acciones fisicas solo por leases;
- actionability tipo Playwright antes de cada clic;
- herramientas por capacidad, no por nombre fijo;
- verificacion de resultado con Reader/Perception;
- modo borrador antes de envio.

### Capa 8: Trust, Telemetry, Evaluation & Cloud

Responsabilidad:

- permisos;
- niveles de autonomia;
- Input Arbiter;
- seguridad LLM;
- privacidad;
- telemetria;
- caja negra;
- soporte;
- evaluaciones;
- release;
- updater;
- rollback;
- ariadgsm.com.

Problemas que resuelve:

- IA con exceso de agencia;
- datos sensibles subidos;
- no saber por que fallo;
- pruebas que pasan pero no miden negocio;
- releases sin rollback;
- nube recibiendo estados contradictorios.

Componentes actuales a fusionar:

- Trust & Safety;
- Input Arbiter;
- Supervisor;
- Support Telemetry;
- Evaluation Release;
- Cloud Sync;
- updater/release manifest.

Estado actual:

```text
BASE AVANZADA, FALTA PRUEBA REAL SUPERVISADA
```

Para consolidar:

- politicas por herramienta y accion;
- evaluaciones de negocio;
- reportes seguros;
- soporte remoto autorizado a futuro;
- release candidate validado en jornada real.

## 5. Mapeo de las 15 etapas antiguas

Las etapas anteriores se conservan, pero ahora viven dentro de las 8 capas.

| Etapa anterior | Capa final |
| --- | --- |
| 0 Execution Lock | Capa 8 y gobierno documental |
| 0.5 Runtime Kernel | Capa 2 |
| 1 Domain Event Contracts | Capa 5 |
| 2 Autonomous Cycle Orchestrator | Capa 2 y Capa 6 |
| 3 Case Manager | Capa 6 |
| 4 Channel Routing Brain | Capa 6 |
| 5 Accounting Core evidence-first | Capa 6 |
| 6 Product Shell | Capa 1 |
| 7 Cabin Authority | Capa 3 |
| 8 Safe Eyes / Reader Core | Capa 4 |
| 9 Living Memory | Capa 6 |
| 10 Business Brain | Capa 6 |
| 11 Trust & Safety + Input Arbiter | Capa 8 |
| 12 Hands & Verification | Capa 7 |
| 13 Tool Registry | Capa 7 |
| 14 Cloud Sync / ariadgsm.com | Capa 8 |
| 15 Evaluation + Release | Capa 8 |

## 6. Componentes que pierden autoridad

Pierden autoridad directa:

- botones llamando directo a motores;
- modulos que abren/mueven/corrigen navegadores sin Cabin Reality Authority;
- JSON como verdad viva principal;
- filtros infinitos para corregir identidad;
- Hands decidiendo negocio;
- Vision/OCR creando conocimiento final sin Reader/Memory/Brain;
- Cloud Sync leyendo estados sueltos sin Control Plane/Backbone.

## 7. Riesgos evitados

Esta arquitectura evita:

- arranques ambiguos;
- apagados con causa generica;
- navegadores cerrados por accidente;
- IA peleando el mouse;
- lectura de ruido;
- aprendizaje falso;
- contabilidad sin evidencia;
- acciones sin verificacion;
- subida de datos sensibles;
- cambios de rumbo por intuicion.

## 8. Definicion de terminado de la arquitectura

La arquitectura queda consolidada cuando:

- este documento es fuente de autoridad;
- Execution Lock apunta a este documento;
- cada cambio futuro declara capa afectada;
- cada cambio importante cita fuentes externas o justifica por que no aplica;
- las 15 etapas estan mapeadas dentro de las 8 capas;
- ningun componente conserva autoridad duplicada;
- el siguiente bloque se limita a una capa y sus integraciones necesarias.

## 9. Orden de construccion sin improvisar

El orden actual, desde el estado real de la app, es:

### Bloque 1: Consolidar Capa 2

Nombre:

```text
AI Runtime Control Plane
```

Objetivo:

- que el inicio de IA deje de ser ambiguo;
- que `operator_button`, `dispose`, `app_closing`, update y fallos tengan causa
  verificable;
- que exista `runSessionId`;
- que el arranque tenga fases;
- que lectura, pensamiento y accion se separen;
- que un fallo de Hands no mate ojos/memoria.

### Bloque 2: Consolidar Capa 3

Nombre:

```text
Cabin Reality Authority
```

Objetivo:

- confirmar WA1/WA2/WA3 por navegador;
- no cerrar navegadores;
- no abrir WhatsApp Desktop;
- proteger cajas;
- fusionar realidad visual, estructural, semantica y accionable.

### Bloque 3: Consolidar Capa 5

Nombre:

```text
Event, Timeline & Durable State Backbone
```

Objetivo:

- SQLite/event log como fuente viva;
- JSON como proyeccion;
- checkpoints;
- replay de jornada;
- causalidad completa.

### Bloque 4: Consolidar Capa 4

Nombre:

```text
Perception & Reader Core
```

Objetivo:

- ojos robustos;
- lectura por objetos;
- OCR solo como fallback;
- fuente/confianza/evidencia.

### Bloque 5: Consolidar Capa 6

Nombre:

```text
Living Memory & Business Brain
```

Objetivo:

- IA de negocio real;
- LLM bajo contrato;
- memoria viva;
- casos;
- precios;
- contabilidad;
- herramientas y proveedores.

### Bloque 6: Consolidar Capa 7

Nombre:

```text
Action, Tools & Verification
```

Objetivo:

- manos confiables;
- herramientas GSM por capacidad;
- verificacion antes/despues;
- borradores y permisos.

### Bloque 7: Consolidar Capa 1 y 8 como producto final

Nombre:

```text
Product Shell + Trust, Telemetry, Evaluation & Cloud
```

Objetivo:

- experiencia humana final;
- soporte;
- evaluacion de negocio;
- updater/rollback;
- ariadgsm.com;
- prueba real de jornada.

## 10. Regla para cada respuesta futura

Toda respuesta de Codex sobre AriadGSM debe incluir, cuando aplique:

```text
Capa afectada:
Capas relacionadas:
Fuentes externas revisadas:
Contraste con negocio AriadGSM:
Por que no es parche:
Siguiente pedido recomendado:
```

Si no se puede investigar por bloqueo real, Codex debe decirlo antes de decidir.

## 11. Siguiente pedido recomendado

Para empezar a arreglar el problema que mas frena hoy:

```text
Codex, siguiendo docs/ARIADGSM_FINAL_AI_ARCHITECTURE.md,
activa Modo Entrega Completa para consolidar Capa 2: AI Runtime Control Plane.

Investiga fuentes externas confiables antes de disenar.
No agregues capas nuevas.
No cambies arquitectura.
Integra Life Controller, Runtime Kernel, Runtime Governor, Workspace Guardian,
Updater y UI bajo una sola autoridad de sesion.

Debe entregar:
- Boot Protocol por fases;
- runSessionId obligatorio;
- Start/Stop Command Ledger;
- separacion read/think/act readiness;
- causa exacta de stop/update/dispose/operator;
- estados humanos claros;
- pruebas automatizadas;
- build;
- version;
- commit y push.

Solo detente ante bloqueo real.
```
