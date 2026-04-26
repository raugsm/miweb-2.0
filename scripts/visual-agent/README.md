# Agente visual AriadGSM

Esta carpeta es el primer puente entre la PC que observa WhatsApp y la cabina en la nube.

En esta fase el agente no responde clientes ni toma decisiones finales de negocio. El modo vivo si puede decidir localmente que chat conviene abrir y envia eventos nuevos a la cabina:

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

## Modo vivo local

El modo vivo nuevo usa Python como cerebro del ciclo local:

```powershell
python .\scripts\visual-agent\agent-local.py --mode Live --max-cycles 1 --send
```

El launcher busca Python en este orden: `ARIADGSM_PYTHON`, runtime local de Codex y luego `python.exe` del sistema. PowerShell se mantiene como puente para OCR/mouse de Windows mientras esas piezas se migran.

## Visual debugger

Para ver exactamente que lee e ignora el agente:

```powershell
python .\scripts\visual-agent\visual-debugger.py --open
```

Genera `runtime\visual-debugger\latest.html` con capturas por canal, lineas OCR aceptadas/ignoradas y la decision local.

## Ojo vivo

Observador continuo por streaming visual:

```powershell
python .\scripts\visual-agent\eyes-stream.py --duration-seconds 8
```

Para dejarlo corriendo desde el launcher usa **Ojo vivo**. El launcher usa `--live`: captura cada 100ms por defecto, compara cambios por region, exige firma visual de WhatsApp antes de aceptar texto, mejora el recorte antes de OCR con escala 2x/contraste/nitidez, ejecuta OCR bajo demanda en segundo plano, guarda frames comprimidos en `D:\AriadGSM\vision-buffer` y escribe `runtime\eyes-stream.state.json` y `runtime\eyes-stream\latest.html`.

El buffer visual rota por defecto con 7 dias o 500GB. Se puede cambiar con `--retention-hours`, `--max-storage-gb`, `--vision-storage-dir` o la variable `ARIADGSM_VISION_STORAGE_DIR`.

El aprendizaje visible queda en `runtime\learning-ledger\latest.html` y tambien se abre desde el launcher con **Aprendizaje**.

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
