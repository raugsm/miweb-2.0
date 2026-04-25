# AriadGSM Operativa V2 - Eventos del agente visual

Este archivo define como el agente visual alimenta la cabina operativa.

La cabina no debe releer todo cada vez. El agente envia solo eventos nuevos y guarda checkpoints por WhatsApp, chat y dia.

## Endpoint

```http
POST /api/operativa-v2/events
Content-Type: application/json
```

## Evento: mensaje de WhatsApp

```json
{
  "type": "whatsapp_message",
  "actor": "visual_agent",
  "data": {
    "channelId": "wa-1",
    "contactName": "Cliente Peru",
    "conversationType": "direct",
    "messageKey": "wa1-chat123-msg456",
    "senderName": "Cliente Peru",
    "direction": "client",
    "text": "Ya pague por el otro WhatsApp",
    "sentAt": "2026-04-25T15:30:00.000Z"
  }
}
```

Uso:

- Alimenta la lectura diaria.
- Suma mensajes no leidos.
- Detecta frases de pago o salto entre WhatsApps.

## Evento: comprobante o pago por texto

```json
{
  "type": "payment_evidence",
  "actor": "visual_agent",
  "data": {
    "channelId": "wa-3",
    "customerName": "Luis Medina",
    "amount": 350,
    "currency": "MXN",
    "country": "Mexico",
    "paymentMethod": "Transferencia",
    "receiverId": "receiver-mx",
    "operationCode": "ABC123",
    "evidenceType": "image",
    "ocrText": "Pago recibido 350 MXN ABC123",
    "paymentDatetime": "2026-04-25T15:35:00.000Z",
    "confidence": 0.88
  }
}
```

Uso:

- Alimenta caja del dia.
- Detecta comprobantes repetidos.
- Si es solo texto, crea revision humana.
- Si es de otro dia, lo marca como posible saldo viejo.

## Evento: servicio vendido

```json
{
  "type": "service_order",
  "actor": "visual_agent",
  "data": {
    "channelId": "wa-3",
    "customerName": "Luis Medina",
    "serviceName": "Xiaomi FRP",
    "deviceModel": "Redmi Note 13",
    "chargedAmount": 350,
    "chargedCurrency": "MXN",
    "providerCost": 8,
    "providerCurrency": "USDT",
    "status": "in_process",
    "paymentEvidenceId": "pay-xxxxx"
  }
}
```

Uso:

- Alimenta servicios activos.
- Calcula ganancia proyectada o realizada cuando ya tengamos conversiones.

## Evento: pago cruzado entre WhatsApps

```json
{
  "type": "cross_channel_payment",
  "actor": "visual_agent",
  "data": {
    "sourceChannelId": "wa-1",
    "targetChannelId": "wa-3",
    "paymentEvidenceId": "pay-xxxxx",
    "amountApplied": 70,
    "currency": "PEN",
    "reason": "Cliente pide usar pago hecho en otro WhatsApp",
    "confidence": 0.78
  }
}
```

Uso:

- Nunca se aprueba solo.
- Crea revision humana.
- Mantiene trazabilidad del canal origen y destino.

## Evento: identidad posible entre WhatsApps

```json
{
  "type": "customer_identity",
  "actor": "visual_agent",
  "data": {
    "channelId": "wa-2",
    "customerName": "Juan Peru",
    "phoneOrAlias": "Juan Carlos",
    "confidence": 0.82,
    "status": "pendiente_revision"
  }
}
```

Uso:

- Ayuda a unir clientes que saltan entre los 3 WhatsApp.
- Si no hay confianza alta, queda en revision.

## Evento: cuenta semanal

```json
{
  "type": "weekly_account",
  "actor": "visual_agent",
  "data": {
    "customer": "Grupo Cliente Mexico",
    "completed": 8,
    "pending": 2,
    "failed": 1,
    "total": "112 USD",
    "status": "acumulando"
  }
}
```

Uso:

- Alimenta cuentas que se cobran sabado por la noche.
- Antes de enviar una cuenta semanal, debe pasar por revision humana.

## Evento: checkpoint

```json
{
  "type": "checkpoint",
  "actor": "visual_agent",
  "data": {
    "channelId": "wa-1",
    "conversationId": "chat-123",
    "dayKey": "2026-04-25",
    "lastMessageKey": "msg-456",
    "lastCaptureHash": "hash-de-captura"
  }
}
```

Uso:

- Evita releer conversaciones completas.
- Permite continuar desde el ultimo mensaje visto.

## Reglas

- No responder clientes en fase 1.
- No aprobar pagos cruzados sin revision humana.
- No cerrar cuentas semanales sin revision humana.
- No duplicar pagos si coincide operacion, monto, moneda, fecha o texto OCR.
- No releer todo el historial: siempre avanzar por checkpoints.
