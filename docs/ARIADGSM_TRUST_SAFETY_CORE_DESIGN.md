# AriadGSM Trust & Safety Core Design

Version objetivo: 0.8.8
Fecha: 2026-04-27
Estado: diseno tecnico del bloque "Trust & Safety Core completo"

## 1. Proposito

Trust & Safety Core es la frontera obligatoria entre el cerebro de AriadGSM IA
Local y cualquier accion real. Su trabajo no es volver al sistema un bot con
reglas rigidas; su trabajo es dar una politica central para que una IA autonoma
pueda razonar con limites claros:

```text
Brain propone -> Trust & Safety autoriza/limita/pide humano/bloquea -> Hands o motores actuan -> Perception verifica
```

La regla madre es:

```text
Ninguna accion irreversible, externa, contable final o visible para cliente se
ejecuta sin permiso explicito, evidencia suficiente, nivel de autonomia correcto
y auditoria humana entendible.
```

## 2. Fuentes externas usadas

Este bloque se apoya en fuentes de gobernanza y seguridad de agentes:

- NIST AI RMF Generative AI Profile: recomienda incorporar consideraciones de
  confianza y riesgo durante diseno, desarrollo, uso y evaluacion.
  Fuente: https://www.nist.gov/publications/artificial-intelligence-risk-management-framework-generative-artificial-intelligence
- OpenAI, Practices for Governing Agentic AI Systems: agentes con metas complejas
  necesitan practicas para operaciones seguras y responsables.
  Fuente: https://openai.com/index/practices-for-governing-agentic-ai-systems/
- OWASP Top 10 for LLM Applications: advierte contra prompt injection, insecure
  output handling y excessive agency.
  Fuente: https://owasp.org/www-project-top-10-for-large-language-model-applications/
- Microsoft Responsible AI guidance: enfatiza supervision humana, auditoria,
  privacidad, seguridad, transparencia y responsabilidad.
  Fuente: https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/responsible-ai

Traduccion practica para AriadGSM:

- menor privilegio por defecto;
- permisos explicitos para acciones de alto impacto;
- humano en el ciclo para acciones irreversibles;
- auditoria de cada permiso;
- no confiar ciegamente en texto de WhatsApp ni en salidas de IA;
- verificar resultado antes de continuar.

## 3. Ubicacion en la arquitectura

Trust & Safety vive entre:

- Cognitive Core / Business Brain;
- Operating Core;
- Accounting Core;
- Channel Routing Brain;
- Hands Engine;
- Autonomous Cycle Orchestrator.

Archivos principales:

- `desktop-agent/ariadgsm_agent/trust_safety.py`
- `desktop-agent/ariadgsm_agent/supervisor.py`
- `desktop-agent/contracts/trust-safety-state.schema.json`
- `desktop-agent/runtime/trust-safety-state.json`
- `desktop-agent/runtime/supervisor-state.json`

Supervisor queda como cara compatible para la app, pero su decision sale del
Trust & Safety Core.

## 4. Decisiones posibles

Trust & Safety solo puede emitir cinco decisiones:

```text
ALLOW
ALLOW_WITH_LIMIT
ASK_HUMAN
PAUSE_FOR_OPERATOR
BLOCK
```

Significado:

- `ALLOW`: puede continuar.
- `ALLOW_WITH_LIMIT`: puede continuar solo si es reversible, local y auditable.
- `ASK_HUMAN`: Bryams debe aprobar/corregir antes de ejecutar.
- `PAUSE_FOR_OPERATOR`: Bryams esta usando mouse/teclado; manos quietas, ojos y
  memoria siguen.
- `BLOCK`: accion prohibida por riesgo, falta de permiso o falta de evidencia.

## 5. Niveles de autonomia

```text
1. Observar y auditar.
2. Sugerir.
3. Navegar/leer localmente en WhatsApp verificado.
4. Crear borradores contables con evidencia.
5. Preparar texto o rutas, sin enviar ni mover contexto final.
6. Acciones irreversibles solo con permiso explicito, evidencia y alta confianza.
```

Nivel 6 no significa "todo permitido". Significa que la IA puede ser evaluada
para acciones fuertes, pero aun necesita permisos explicitos.

## 6. Matriz de riesgo

La matriz central vive en codigo y se publica en estado:

| Capacidad | Nivel | Riesgo | Reversible | Permiso |
| --- | --- | --- | --- | --- |
| observar | 1 | bajo | si | ninguno |
| sugerir | 2 | medio | si | ninguno |
| navegar WhatsApp local | 3 | medio | si | `allowLocalNavigation` |
| borrador contable | 4 | alto | si | `allowAccountingDraft` |
| borrador de texto | 5 | alto | si | `allowTextDraft` |
| derivar/fusionar canales | 5 | alto | si | `allowCrossChannelTransfer` |
| herramienta externa | 6 | critico | no | `allowExternalToolExecution` |
| enviar mensaje | 6 | critico | no | `allowMessageSend` |
| confirmar pago/caja | 6 | critico | no | `allowAccountingConfirmation` |

Permisos por defecto:

```text
allowLocalNavigation = true
allowAccountingDraft = true
todo lo demas = false
```

## 7. Bloqueo de acciones irreversibles

Se consideran irreversibles o de alto impacto:

- enviar un mensaje al cliente;
- prometer precio o servicio como respuesta final;
- confirmar pago, cerrar deuda o marcar caja;
- ejecutar herramientas externas;
- modificar telefono, USB, flasheo o unlock tool;
- fusionar contexto entre WhatsApps sin aprobacion.

Estas acciones no pasan solo por confianza. Necesitan:

- autonomia suficiente;
- permiso explicito;
- evidencia suficiente;
- no estar bajo control del operador;
- reporte humano.

## 8. Evidencia contable

Contabilidad es evidence-first:

```text
PaymentConfirmed y AccountingRecordConfirmed requieren evidencia nivel A.
```

Si no existe evidencia A:

```text
decision = BLOCK
reason = Confirmacion contable sin evidencia nivel A
```

Esto evita que la IA marque pagos o cierre deudas solo porque un chat lo dijo.

## 9. Relacion con el operador

Si Input Arbiter detecta control humano:

```text
decision = PAUSE_FOR_OPERATOR
hands = false
vision/perception/memory/cognitive = true
```

La IA no se apaga, solo no pelea el mouse.

## 10. Criterio de terminado

Este bloque queda completo cuando:

- existe estado `trust-safety-state.json` validado por contrato;
- Supervisor usa Trust & Safety como politica central;
- hay matriz de autonomia/riesgo/permisos;
- se bloquean acciones irreversibles sin permiso;
- contabilidad confirmada exige evidencia nivel A;
- la app muestra Trust & Safety como motor de salud;
- hay pruebas sin tocar clientes reales;
- la version queda empaquetada y publicada.

