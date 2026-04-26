# Agente visual AriadGSM

Esta carpeta es el primer puente entre la PC que observa WhatsApp y la cabina en la nube.

En esta fase el agente no responde clientes, no mueve procesos y no toma decisiones finales. Solo envia eventos nuevos a la cabina:

- mensajes detectados
- pagos o comprobantes detectados
- servicios vendidos
- clientes que saltan entre WhatsApps
- checkpoints para no releer todo

## Preparacion

1. Copia `visual-agent.config.example.json` como `visual-agent.config.json`.
2. En Railway agrega una variable llamada `OPERATIVA_AGENT_KEY`.
3. Usa el mismo valor como `agentToken` en `visual-agent.config.json`.
4. Coloca archivos `.json` dentro de `inbox`.
5. Ejecuta el agente con Node.

## Ejecutar una vez

```powershell
node .\scripts\visual-agent\visual-agent.js --once
```

## Ejecutar en observacion continua

```powershell
node .\scripts\visual-agent\visual-agent.js --watch
```

## Formato de evento

Puedes poner un archivo `.json` con un evento:

```json
{
  "type": "whatsapp_message",
  "actor": "visual_agent",
  "data": {
    "channelId": "wa-1",
    "contactName": "Cliente Peru",
    "messageKey": "wa1-cliente-001",
    "senderName": "Cliente Peru",
    "direction": "client",
    "text": "Ya pague por el otro WhatsApp",
    "sentAt": "2026-04-25T15:30:00.000Z"
  }
}
```

O una lista de eventos:

```json
[
  { "type": "whatsapp_message", "data": { "channelId": "wa-1", "text": "Hola" } },
  { "type": "checkpoint", "data": { "channelId": "wa-1", "lastMessageKey": "msg-1" } }
]
```
