# AriadGSM Living Memory

Version: 0.8.12
Etapa: 9
Estado: base cerrada

## 1. Proposito

Living Memory convierte lecturas, eventos de dominio, casos, contabilidad y
correcciones humanas en memoria de negocio util. Esta etapa existe para que la
IA deje de depender de chats sueltos y empiece a conservar contexto con
evidencia:

- que paso;
- que significa;
- como se suele resolver;
- que impacto contable tiene;
- que duda;
- que corrigio Bryams.

Esta memoria no es una lista de reglas. Es una capa de conocimiento operativo
que el Business Brain podra consultar antes de decidir.

## 2. Investigacion externa usada

La estructura se apoya en patrones conocidos de agentes con memoria:

- Generative Agents separa observacion, memoria, reflexion y planificacion.
  Su idea clave para AriadGSM es que las experiencias se guardan en una memoria
  recuperable y luego se sintetizan para actuar con coherencia a largo plazo.
  Fuente: https://3dvar.com/Park2023Generative.pdf
- CoALA propone agentes con componentes modulares de memoria, acciones internas
  y externas, y un proceso general de decision. Para AriadGSM esto respalda que
  memoria, manos y cerebro no sean un solo bloque mezclado.
  Fuente: https://arxiv.org/abs/2309.02427
- MemGPT usa jerarquia de memoria para manejar contexto limitado y memoria
  externa persistente. Para AriadGSM esto justifica separar memoria reciente,
  memoria durable y recuperacion bajo demanda.
  Fuente: https://shishirpatil.github.io/publications/memgpt-2023.pdf
- RAG muestra que recuperar evidencia externa mejora respuestas especificas y
  factuales. Para AriadGSM, toda memoria util debe poder explicar su fuente.
  Fuente: https://arxiv.org/abs/2005.11401
- NIST AI RMF enfatiza gobernanza, gestion de riesgos y comunicacion de
  limitaciones. Para AriadGSM, la memoria debe exponer incertidumbre,
  correcciones y necesidad de revision humana.
  Fuente: https://www.nist.gov/itl/ai-risk-management-framework

## 3. Capas de memoria

Living Memory guarda seis capas. Cada memoria tiene tipo, estado de verdad,
confianza, evidencia, fuente y trazabilidad.

1. Episodica
   Lo que ocurrio en una conversacion, caso o evento. Ejemplo: "wa-2 recibio 4
   mensajes de Cliente X preguntando por Xiaomi".

2. Semantica
   Hechos y patrones del negocio: cliente, pais, servicio, modelo, proveedor,
   precio observado, demanda frecuente.

3. Procedimental
   Como se hacen trabajos o como se corrigen fallos: pasos, excepciones,
   alternativas cuando una herramienta falla.

4. Contable
   Pagos, deudas, reembolsos, cotizaciones y evidencia. Si no hay comprobante o
   confirmacion, se guarda como hipotesis o borrador.

5. Estilo
   Jerga, idioma, tono y forma de hablar por pais o cliente. Sirve para que la
   IA entienda mejor el negocio real, no solo texto formal.

6. Correccion
   Lo que Bryams corrige, aprueba o rechaza. Esta capa puede degradar memorias
   anteriores para evitar repetir errores.

## 4. Estados de verdad

Cada memoria vive con uno de estos estados:

- fact: evidencia suficiente y sin revision pendiente.
- hypothesis: probablemente cierto, pero falta evidencia fuerte.
- uncertain: confianza baja o requiere revision humana.
- procedure: aprendizaje operativo de como actuar.
- correction: correccion humana o ajuste autorizado.
- deprecated: conocimiento degradado por correccion o conflicto.
- conflict: reservado para contradicciones entre fuentes.

Esto evita que la IA trate igual un pago confirmado, una captura dudosa y una
frase aprendida de un cliente.

## 5. Entradas

Living Memory consume:

- `conversation_event`: historia visible unificada por Timeline/Reader Core.
- `decision_event`: decisiones de Cognitive/Operating Core.
- `learning_event`: aprendizaje extraido por clasificadores o IA local.
- `accounting_event`: eventos contables evidence-first.
- `domain_event`: eventos de negocio normalizados.
- `human_feedback_event`: correcciones, aprobaciones, rechazos y notas humanas.

## 6. Salida

El contrato principal es:

- `desktop-agent/contracts/living-memory-state.schema.json`

El estado queda en:

- `desktop-agent/runtime/memory-state.json`

Campos importantes:

- `livingMemory.byLayer`: conteo por tipo de memoria.
- `livingMemory.byStatus`: hechos, hipotesis, correcciones y degradados.
- `latestLearned`: lo mas reciente que aprendio.
- `uncertainties`: lo que no debe usar sin cuidado.
- `corrections`: correcciones humanas recibidas.
- `humanReport`: resumen entendible para Bryams.

## 7. Politica de escritura

Living Memory no guarda todo como verdad. Usa estas reglas:

- confianza alta y sin revision: `fact`;
- confianza media: `hypothesis`;
- confianza baja o riesgo: `uncertain`;
- correccion humana: `correction`;
- conocimiento contradicho: `deprecated`.

Si una correccion apunta a una memoria o evento anterior, la confianza baja y
esa memoria queda marcada para revision.

## 8. Por que esto acerca el sistema a una IA

Un bot ejecuta reglas. Living Memory crea continuidad:

- recuerda experiencias pasadas;
- distingue hechos de dudas;
- aprende de correcciones;
- conserva procedimientos que cambiaron por experiencia;
- permite que el cerebro consulte evidencia antes de decidir.

La siguiente etapa, Business Brain, debe usar esta memoria para razonar sobre
clientes, precios, servicios, prioridades, riesgos, contabilidad y estilo
AriadGSM.

## 9. Definicion de terminado

Esta etapa queda cerrada cuando:

- existe este documento;
- existe contrato `living_memory_state`;
- Memory Core separa memoria episodica, semantica, procedimental, contable,
  estilo y correccion;
- ingiere Reader Core/Timeline, Domain Events, Learning, Accounting y Human
  Feedback;
- degrada conocimiento inseguro;
- explica que aprendio, que duda y que corrigio Bryams;
- existen pruebas repetibles sin WhatsApps reales ni acciones externas.
