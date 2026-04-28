# AriadGSM Channel Routing Brain Design

Fecha: 2026-04-28
Version objetivo: 0.8.4
Estado: implementacion completa del bloque `Channel Routing Brain`

## 1. Proposito

Channel Routing Brain decide que hacer cuando un caso aparece en un WhatsApp
pero su contexto, cliente o tipo de servicio apunta a otro canal.

No mueve el mouse. No abre pestañas. No decide por posicion de pantalla.

Lee casos reales de Case Manager y emite eventos de dominio:

- `ChannelRouteProposed`
- `ChannelRouteApproved`
- `ChannelRouteRejected`

## 2. Fuentes externas usadas

La estructura sigue patrones conocidos y auditables:

- Microsoft Saga Pattern: un flujo de negocio entre componentes debe coordinar
  pasos, eventos, compensaciones y monitoreo.
  https://learn.microsoft.com/en-us/azure/architecture/patterns/saga
- Enterprise Integration Patterns, Content-Based Router: enrutar segun el
  contenido del mensaje/caso, no segun donde aparecio fisicamente.
  https://www.enterpriseintegrationpatterns.com/patterns/messaging/ContentBasedRouter.html
- Microsoft CQRS: usar una vista materializada para consultas rapidas mientras
  los eventos siguen siendo fuente de verdad.
  https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs
- CloudEvents: los eventos deben llevar contexto y sujeto para permitir
  filtrado/routing sin interpretar todo el payload.
  https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md
- OpenAI Agents handoffs/guardrails: una IA puede transferir trabajo a una
  capacidad especializada, pero los handoffs riesgosos deben estar trazados y
  protegidos.
  https://openai.github.io/openai-agents-js/guides/handoffs/
  https://openai.github.io/openai-agents-js/guides/guardrails/

## 3. Decision para AriadGSM

El routing no debe ser un if gigante tipo:

```text
si Xiaomi entonces wa-3
```

Debe ser una decision explicada:

```text
Caso + cliente + historial + senales + canal actual + politica -> ruta propuesta
```

La politica inicial es local y configurable:

- `wa-1`: entrada general / intake / continuidad del cliente;
- `wa-2`: precios, ventas, pagos, deudas, contabilidad ligera;
- `wa-3`: tecnico, modelos, servicios, procedimientos y herramientas.

Esa politica no es verdad absoluta. Es el primer mapa operativo. El cerebro
puede proponer cambios con evidencia y pedir confirmacion humana cuando cruza
canales.

## 4. Flujo

```text
Case Manager SQLite
  -> Channel Routing Brain
  -> channel-routing-state.json
  -> channel-routing-report.json
  -> route-events.jsonl
  -> Domain Events
  -> Memory / Supervisor / Autonomous Cycle
```

## 5. Decisiones posibles

### stay_current

El caso ya esta en un canal razonable. Se emite `ChannelRouteApproved` con
`approvalSource=policy_same_channel`.

### propose_transfer

El caso esta en un canal, pero la evidencia apunta a otro. Se emite
`ChannelRouteProposed`, requiere a Bryams y no ejecuta traslado automatico.

### propose_merge

Hay dos o mas casos que parecen el mismo cliente en canales distintos. Se emite
`ChannelRouteProposed` con `routeKind=merge_context`.

### reject_transfer

La evidencia no alcanza para moverlo. Se emite `ChannelRouteRejected` cuando hay
una ruta candidata peligrosa o inconsistente.

## 6. Que no debe hacer

- No cerrar Edge, Chrome ni Firefox.
- No ordenar ventanas.
- No abrir WhatsApp.
- No enviar mensajes.
- No confirmar pagos.
- No depender de coordenadas.
- No mezclar chats por texto parecido sin confianza suficiente.

## 7. Datos que usa

Desde `case-manager.sqlite`:

- `caseId`
- `customerId`
- `primaryChannelId`
- `channels`
- `conversations`
- `title`
- `country`
- `service`
- `device`
- `intent`
- `paymentState`
- `quoteState`
- `priority`
- `requiresHuman`
- `eventCount`
- `updatedAt`

## 8. Guardrails

Cruzar canales es riesgoso porque puede mezclar clientes o mandar contexto al
WhatsApp equivocado. Por eso:

- transferencias y fusiones requieren revision humana;
- la ruta explica evidencia, canal origen, canal destino y confianza;
- las rutas se guardan con idempotencia;
- si la confianza no alcanza, se rechaza o se deja el caso en su canal.

## 9. Definicion de cerrado 0.8.4

Este bloque queda cerrado si:

- existe `ariadgsm_agent.channel_routing`;
- existe contrato `channel-routing-state`;
- existe prueba `channel_routing.py`;
- lee casos desde Case Manager;
- emite `ChannelRouteProposed`;
- emite `ChannelRouteApproved` como aprobacion local de canal actual;
- puede emitir `ChannelRouteRejected`;
- explica motivo, confianza y evidencia;
- no depende de posicion de pantalla;
- Domain Events absorbe `route-events.jsonl`;
- Autonomous Cycle muestra routing como etapa propia;
- runtime ejecuta Channel Routing antes de Memory;
- version y paquete quedan publicados.
