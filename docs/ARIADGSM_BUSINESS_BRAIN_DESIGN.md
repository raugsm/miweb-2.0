# AriadGSM Business Brain

Version: 0.8.13
Etapa: 10
Estado: base cerrada

## 1. Proposito

Business Brain es el cerebro de negocio local de AriadGSM. Su trabajo no es
mover el mouse ni enviar mensajes. Su trabajo es pensar el negocio:

- que cliente o caso tengo delante;
- que quiere;
- que servicio, pais, herramienta o riesgo aparece;
- que memoria viva aplica;
- que falta confirmar;
- que propuesta conviene;
- que debe revisar Bryams antes de actuar.

Esta etapa convierte la memoria viva en decisiones de negocio auditables. Las
manos, herramientas GSM y envio de mensajes quedan fuera de esta etapa.

## 2. Investigacion externa usada

La estructura se apoya en patrones de agentes modernos:

- ReAct propone intercalar razonamiento y accion, pero manteniendo trazas
  interpretables. AriadGSM usa esa idea separando primero "razonar/proponer" y
  despues "actuar/verificar" bajo permisos.
  Fuente: https://arxiv.org/abs/2210.03629
- CoALA organiza agentes con memoria modular, acciones internas/externas y
  decision. AriadGSM adopta esa division: Living Memory es memoria, Business
  Brain decide, Hands ejecuta solo cuando se autorice.
  Fuente: https://arxiv.org/abs/2309.02427
- Los estudios de planificacion de agentes LLM separan descomposicion,
  seleccion de plan, modulos externos, reflexion y memoria. Para AriadGSM,
  Business Brain es seleccion de plan de negocio, no automatizacion fisica.
  Fuente: https://arxiv.org/abs/2402.02716
- RAG respalda que las decisiones deben recuperar evidencia, no depender solo
  de memoria interna del modelo. Business Brain consulta `Living Memory`, casos,
  rutas y contabilidad antes de proponer.
  Fuente: https://arxiv.org/abs/2005.11401
- NIST AI RMF Generative AI Profile enfatiza gestion de riesgo,
  transparencia y evaluacion. Por eso cada propuesta incluye confianza,
  faltantes, evidencia y confirmacion humana.
  Fuente: https://www.nist.gov/publications/artificial-intelligence-risk-management-framework-generative-artificial-intelligence

## 3. Entradas

Business Brain consume:

- `case-manager.sqlite`: casos activos, cliente, pais, servicio, prioridad y
  ultimo evento.
- `memory-core.sqlite`: memorias episodicas, semanticas, procedimentales,
  contables, estilo y correcciones.
- `channel-routing.sqlite`: propuestas de transferencia o fusion entre
  WhatsApps.
- `accounting-core.sqlite`: pagos, deudas, reembolsos y evidencia contable.

## 4. Salidas

Contrato principal:

- `desktop-agent/contracts/business-brain-state.schema.json`

Estado:

- `desktop-agent/runtime/business-brain-state.json`

Eventos:

- `desktop-agent/runtime/business-decision-events.jsonl`
- `desktop-agent/runtime/business-recommendations.jsonl`

Business Brain emite `decision_event` con `decisionId` prefijado como
`business-decision-...`. Domain Events los traduce como fuente `BusinessBrain`.
Trust & Safety tambien los lee antes de permitir cualquier accion posterior.

## 5. Modelo mental

Business Brain arma un modelo mental local:

- clientes activos;
- servicios y mercados detectados;
- casos con mayor prioridad;
- memorias consultadas;
- incertidumbres;
- faltantes para cotizar, responder, derivar o registrar contabilidad.

Este modelo no es una regla fija. Es una vista de negocio que se reconstruye en
cada ciclo desde evidencia reciente y memoria viva.

## 6. Politica de decision

Business Brain opera en modo:

```text
recommend_only_no_physical_action
```

Eso significa:

- puede proponer una respuesta;
- puede proponer pedir datos faltantes;
- puede proponer revisar un pago;
- puede proponer derivar un caso a otro WhatsApp;
- no puede enviar mensajes;
- no puede confirmar pagos;
- no puede mover mouse;
- no puede operar herramientas GSM.

Cualquier decision con riesgo, contabilidad, rutas o respuesta al cliente queda
marcada con `requiresHumanConfirmation=true`.

## 7. Capacidades cerradas en esta etapa

1. Consulta Living Memory
   Recupera memorias por caso, conversacion, cliente y coincidencia semantica
   simple sobre servicio, pais, titulo e intencion.

2. Decide con evidencia
   Cada recomendacion incluye evidencia: ids de caso, memorias, registros
   contables o decisiones de ruta.

3. Propone, no ejecuta
   La salida es una recomendacion y un `decision_event`; no toca la pantalla.

4. Reconoce incertidumbre
   Si falta modelo, pais, servicio o evidencia contable nivel A, lo marca como
   informacion faltante.

5. Conecta contabilidad
   Si hay pago/deuda/reembolso sin confirmacion fuerte, prioriza revision
   contable.

6. Conecta rutas
   Si Channel Routing propone transferir o fusionar contexto, Business Brain lo
   eleva como decision humana antes de mover manos.

7. Reporta en humano
   Explica que entendio, que propone, que duda y que necesita de Bryams.

## 8. Por que esto es IA y no solo bot

Un bot ejecuta una lista de pasos. Business Brain hace algo distinto:

- junta memoria, casos, contabilidad y rutas;
- estima intencion y prioridad;
- reconoce faltantes;
- propone planes de negocio;
- conserva evidencia para que otro modulo pueda verificar;
- respeta incertidumbre y seguridad.

La inteligencia real de AriadGSM no vive en un unico archivo. Vive en el ciclo:

```text
ojos -> timeline -> memoria -> business brain -> trust/safety -> manos -> verificacion -> aprendizaje
```

## 9. Pendiente para autonomia final

Business Brain queda como base operativa. A futuro debe conectarse con:

- modelos LLM locales o cloud para razonamiento mas flexible;
- Tool Registry para escoger herramientas GSM por capacidad;
- mercado/proveedores para comparar precios;
- evaluaciones con conversaciones reales largas;
- politica final de respuestas automaticas por nivel de autonomia.

## 10. Definicion de terminado

Esta etapa queda cerrada cuando:

- existe este documento;
- existe contrato `business_brain_state`;
- el motor consulta Living Memory, casos, rutas y contabilidad;
- genera recomendaciones y `decision_event`;
- Domain Events reconoce las decisiones como `BusinessBrain`;
- Trust & Safety lee las decisiones de negocio;
- la app ejecuta Business Brain dentro del ciclo local;
- hay pruebas repetibles sin WhatsApps reales ni herramientas GSM reales.
