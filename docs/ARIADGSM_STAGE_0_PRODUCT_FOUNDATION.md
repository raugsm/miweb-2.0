# AriadGSM Stage 0 Product Foundation

Fecha: 2026-04-27
Estado: etapa base formal para `0.8.1`

## 1. Proposito

La Etapa 0 existe para que AriadGSM IA Local no vuelva a crecer como una suma
de parches.

Esta etapa no ensena a la IA a responder clientes todavia. Su trabajo es fijar
la base del producto:

- que estamos construyendo;
- que documentos mandan;
- que significa "IA operadora" y que no significa;
- que etapas vienen despues;
- que contratos deben existir antes de mover mouse, leer chats o tocar
  contabilidad;
- como se valida que una etapa esta cerrada.

## 2. Decision de producto

El producto final es:

```text
AriadGSM IA Local
```

Descripcion:

```text
Una IA operadora local para AriadGSM que observa WhatsApp y herramientas,
entiende el negocio, recuerda, prioriza, propone, actua con permiso, verifica,
registra contabilidad y aprende de correcciones.
```

No es:

- un bot de WhatsApp;
- una macro que hace clics;
- un sistema de filtros infinitos;
- un OCR con reglas;
- una automatizacion sin memoria ni criterio.

## 3. Fuentes externas usadas

Esta etapa se apoya en estas referencias:

- OpenAI Agents SDK: agentes como aplicaciones que planifican, usan
  herramientas, mantienen estado, hacen handoffs, tienen guardrails, revision
  humana, trazas y evaluaciones.
  Fuente: https://developers.openai.com/api/docs/guides/agents
- NIST AI RMF: gestionar riesgos de IA durante diseno, desarrollo, uso y
  evaluacion; confianza, seguridad, privacidad, transparencia y medicion.
  Fuente: https://www.nist.gov/itl/ai-risk-management-framework
- ISO/IEC/IEEE 29148: requisitos claros durante el ciclo de vida del sistema y
  productos de informacion verificables.
  Fuente: https://www.iso.org/standard/72089.html
- OWASP GenAI Security Project: seguridad de aplicaciones LLM, sistemas
  agenticos, gobernanza y riesgos.
  Fuentes:
  https://owasp.org/www-project-top-10-for-large-language-model-applications/
  https://genai.owasp.org/2025/12/09/owasp-top-10-for-agentic-applications-the-benchmark-for-agentic-security-in-the-age-of-autonomous-ai/

Como se traduce a AriadGSM:

- cada etapa debe tener contrato, estado, evidencia y prueba;
- cada accion riesgosa debe pasar por permiso y verificacion;
- cada lectura debe guardar origen y confianza;
- cada decision importante debe poder auditarse;
- ninguna etapa se declara "100%" si no existe una validacion repetible.

## 4. Orden de autoridad

El orden de autoridad queda:

1. `docs/ARIADGSM_EXECUTION_LOCK.md`
2. `docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md`
3. `docs/ARIADGSM_FINAL_PRODUCT_BLUEPRINT.md`
4. `docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS.md`
5. `docs/ARIADGSM_BUSINESS_DOMAIN_MAP.md`
6. `docs/ARIADGSM_BUSINESS_OPERATING_MODEL.md`
7. `docs/ARIADGSM_AUTONOMOUS_OPERATING_SYSTEM_1.0.md`
8. documentos tecnicos por motor

Si hay conflicto, primero manda `Execution Lock`; esta Etapa 0 define la base
que `Execution Lock` debe respetar.

## 5. Etapas maestras

Las etapas quedan ordenadas asi:

```text
0. Product Foundation
1. Domain Event Contracts
2. Autonomous Cycle Orchestrator
3. Case Manager
4. Channel Routing Brain
5. Accounting Core evidence-first
6. Product Shell y cabina humana
7. Ojos definitivos y manos verificadas
8. Memory / Learning / Self-Improvement
9. Tool Registry GSM y operacion avanzada
10. Cloud Sync / ariadgsm.com / reportes
11. Evaluation, release y rollback
```

Execution Lock puede activar un bloque dentro de estas etapas, pero no puede
borrar la Etapa 0.

## 6. Definicion de terminado de Etapa 0

Etapa 0 esta cerrada solo si:

- existe este documento;
- Execution Lock reconoce la Etapa 0 y el siguiente bloque correcto;
- existe un verificador ejecutable de Etapa 0;
- existe un contrato JSON para el reporte de Etapa 0;
- existe una prueba automatica;
- el verificador genera reporte humano y estado tecnico;
- version y update manifest apuntan a la misma version;
- la app ejecuta la verificacion al comenzar el ciclo principal;
- el build y el paquete se generan sin errores.

## 7. Contrato de salida

El estado de Etapa 0 se guarda en:

```text
desktop-agent/runtime/stage-zero-state.json
```

El reporte humano se guarda en:

```text
desktop-agent/runtime/stage-zero-report.json
```

El contrato vive en:

```text
desktop-agent/contracts/stage-zero-readiness.schema.json
```

Campos clave:

- `stageId`
- `version`
- `status`
- `checks`
- `humanReport`
- `nextStage`

## 8. Reglas contra parches

1. Si falta una fuente de verdad, no se parcha en UI: se agrega al contrato.
2. Si una etapa no puede medirse, no se declara cerrada.
3. Si un boton hace trabajo por su lado, debe pasar por el ciclo autonomo.
4. Si una accion mueve mouse, debe pasar por Input Arbiter y verificacion.
5. Si se lee WhatsApp, debe quedar origen, canal, confianza y evidencia.
6. Si se registra dinero, debe haber caso y evidencia.
7. Si la IA aprende, debe separar hecho, sospecha y aprendizaje pendiente.

## 9. Que queda fuera de Etapa 0

Etapa 0 no resuelve:

- abrir chats con mouse;
- entender todos los clientes;
- responder mensajes;
- cerrar contabilidad;
- enrutar clientes entre WhatsApps;
- usar herramientas GSM;
- automejorarse.

Eso corresponde a etapas posteriores. Etapa 0 evita que esas etapas se hagan
sin columna vertebral.

## 10. Resultado esperado de 0.8.1

Al terminar `0.8.1`, AriadGSM debe poder responder:

```text
Tengo una base de producto validada.
Se que etapa sigue.
Se que documentos mandan.
Se que contratos existen.
Se que version esta corriendo.
Se que no debo declarar autonomia final sin pruebas.
```

