# AriadGSM Trust & Safety + Input Arbiter Final

Version: 0.8.14
Fecha: 2026-04-28
Etapa: 11
Estado: cerrada como base final de seguridad y arbitraje

## 1. Proposito

Esta etapa convierte la seguridad en una autoridad central, no en una lista de
parches. El cerebro puede proponer, pero ninguna mano toca la PC sin pasar por
un permiso vigente, evidencia suficiente y un dueno claro del mouse/teclado.

La forma final es:

```text
Cognitive / Operating / Business Brain
  -> Trust & Safety
  -> permissionGate
  -> Hands
  -> Input Arbiter lease
  -> ejecucion
  -> verificacion
```

## 2. Investigacion aplicada

Fuentes externas usadas para cerrar el diseno:

- NIST AI RMF Generative AI Profile: gestionar riesgo durante diseno, uso y
  evaluacion de sistemas generativos.
  https://www.nist.gov/publications/artificial-intelligence-risk-management-framework-generative-artificial-intelligence
- OWASP Top 10 for LLM Applications 2025: riesgo de excessive agency, output
  inseguro y acciones con herramientas sin control.
  https://owasp.org/www-project-top-10-for-large-language-model-applications/assets/PDF/OWASP-Top-10-for-LLMs-v2025.pdf
- Microsoft Human-AI Interaction Guidelines: mostrar capacidades/limites,
  permitir correccion, explicar acciones y mantener controles globales.
  https://www.microsoft.com/en-us/research/articles/guidelines-for-human-ai-interaction-eighteen-best-practices-for-human-centered-ai-design/
- Microsoft GetLastInputInfo: la deteccion de input es por sesion y sirve para
  saber si el operador esta activo.
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo
- Microsoft SendInput: el input sintetico tiene limites de integridad y debe
  tratarse como una accion auditada.
  https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput

## 3. Decisiones de raiz

1. Trust & Safety queda como gate central.
   Lee decisiones de Cognitive, Operating y Business Brain. Supervisor ya no
   puede sobrescribir seguridad ignorando Business Brain.

2. Hands no ejecuta si Trust & Safety no autoriza.
   Aunque Hands tenga una accion lista, primero revisa `trust-safety-state.json`.
   Si `permissionGate.canHandsRun` no es true o el estado esta viejo, no mueve.

3. Input Arbiter usa leases.
   El mouse/teclado tienen dueno temporal: `ai`, `operator`, `none` o `unknown`.
   Cada intento publica lease, TTL, cooldown, accion y razon.

4. Acciones criticas necesitan aprobacion por ejecucion.
   Enviar mensajes, confirmar pagos, ejecutar herramientas externas o cualquier
   accion irreversible requiere permiso explicito y aprobacion humana vigente.

5. Acciones de alto impacto necesitan evidencia.
   Borradores contables, textos al cliente y derivaciones entre canales deben
   tener evidencia enlazada. Confirmar contabilidad requiere evidencia nivel A.

6. Cuando Bryams usa el mouse, solo se pausan manos.
   Vision, Perception, Memory, Cognitive y Business Brain siguen trabajando.

## 4. Contratos finales

Archivos nuevos o elevados a contrato final:

- `desktop-agent/contracts/input-arbiter-state.schema.json`
- `desktop-agent/contracts/safety-approval-event.schema.json`
- `desktop-agent/contracts/trust-safety-state.schema.json`

Estados principales:

- `runtime/trust-safety-state.json`
- `runtime/input-arbiter-state.json`
- `runtime/safety-approval-events.jsonl`

## 5. Politica de accion

Trust & Safety solo puede responder:

```text
ALLOW
ALLOW_WITH_LIMIT
ASK_HUMAN
PAUSE_FOR_OPERATOR
BLOCK
```

Interpretacion:

- `ALLOW`: puede continuar.
- `ALLOW_WITH_LIMIT`: reversible, local y auditable.
- `ASK_HUMAN`: necesita aprobacion de Bryams.
- `PAUSE_FOR_OPERATOR`: Bryams tiene prioridad de mouse/teclado.
- `BLOCK`: prohibido por riesgo, permiso, evidencia o politica.

## 6. Que problema soluciona

Antes, una parte del sistema podia proponer y otra podia intentar actuar sin una
autoridad final unificada. Eso abria fallos tipo:

- el supervisor no veia decisiones de Business Brain;
- Hands podia avanzar con una decision local aunque seguridad no hubiese dado
  permiso fresco;
- Input Arbiter publicaba senales utiles, pero no un contrato completo de dueno,
  lease y cooldown;
- una accion critica podia depender demasiado de confianza numerica.

En 0.8.14, esas rutas quedan cerradas como base:

- decision -> seguridad -> manos;
- manos -> permiso vigente;
- permiso -> lease de input;
- lease -> ejecucion corta;
- ejecucion -> verificacion/auditoria.

## 7. Limites conscientes

Esta etapa no vuelve a Hands mas inteligente. Eso es Etapa 12.

Tampoco crea la pantalla final de aprobaciones humanas. Ya existe el contrato
`safety_approval_event`; Product Shell/Cloud Sync podran escribir esos eventos
cuando toque interfaz final.

## 8. Definicion de terminado

Etapa 11 queda cerrada porque:

- Trust & Safety evalua Cognitive, Operating y Business Brain;
- existe contrato final para Input Arbiter;
- existe contrato para aprobaciones humanas;
- Hands respeta Trust & Safety antes de ejecutar;
- Input Arbiter publica dueno, lease, cooldown y continuidad de motores;
- acciones criticas exigen permiso, evidencia y aprobacion por ejecucion;
- pruebas Python y C# cubren bloqueo, aprobacion, Business Brain y control
  humano;
- la version se empaqueta y publica en GitHub.
