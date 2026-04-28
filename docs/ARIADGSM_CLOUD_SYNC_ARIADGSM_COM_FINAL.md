# AriadGSM Cloud Sync / ariadgsm.com Final

Fecha: 2026-04-28
Version: 0.8.18
Etapa Execution Lock: 14
Estado: cerrada como base final

## 1. Proposito

Cloud Sync es la capa que conecta la cabina local con `ariadgsm.com`.

No es un capturador de pantalla hacia la nube. No sube fotos, frames, cookies,
tokens ni sesiones internas. Su trabajo es publicar eventos ya entendidos por la
IA local:

- estado humano del Runtime Kernel;
- checkpoints de los 3 WhatsApp;
- mensajes visibles convertidos en objetos de conversacion;
- dudas o revisiones de dominio que necesitan a Bryams;
- resumen de sincronizacion para panel, reportes, respaldos y auditoria.

Esto mantiene la arquitectura alineada con el producto final:

```text
ojos locales -> memoria local -> cerebro local -> eventos entendidos -> nube
```

La nube queda como panel, respaldo y espejo operativo. La decision sensible
sigue naciendo localmente y con permisos.

## 2. Investigacion aplicada

Esta etapa se diseno con fuentes externas para evitar otro parche:

- Microsoft Retry Pattern: reintentos solo para fallos transitorios, con limite y
  espera entre intentos.
- Microsoft Circuit Breaker Pattern: si la nube falla repetidamente, la cabina
  no debe quedarse golpeando el servidor; abre circuito, reporta alerta y sigue
  trabajando localmente.
- OWASP API Security Top 10 2023: autenticacion y autorizacion son riesgos
  principales en APIs, por eso el token no va en el codigo ni en logs.
- OpenTelemetry: la observabilidad futura debe correlacionar logs, metricas y
  trazas, no esconder errores en una caja tecnica.
- Railway Variables y Volumes: las credenciales deben venir de variables
  selladas/entorno, y la nube debe guardar estado en almacenamiento persistente.

## 3. Flujo final de sincronizacion

```text
Runtime Kernel
  -> cloud_sync.py
    -> cloud-sync-payload.json
    -> POST /api/operativa-v2/cloud/sync
      -> server-wrapper.js
        -> operativa-store.js
          -> operativa-v2 panel
          -> backups/reportes de ariadgsm.com
```

Cloud Sync lee solo archivos de estado locales aprobados:

- `runtime-kernel-state.json`
- `timeline-events.jsonl`
- `domain-events.jsonl`
- `cabin-readiness.json`

Produce:

- `cloud-sync-state.json`
- `cloud-sync-report.json`
- `cloud-sync-payload.json`
- `cloud-sync-ledger.json`

## 4. Contrato

El contrato oficial es:

```text
desktop-agent/contracts/cloud-sync-state.schema.json
```

El nombre interno es:

```text
cloud_sync_state
```

El estado debe explicar de forma legible:

- si la sincronizacion esta activa;
- si esta autenticada;
- cuantos eventos preparo;
- cuantos mensajes preparo;
- cuantos mensajes rechazo por ruido o duplicado;
- si la nube acepto el lote;
- si el circuito esta abierto;
- que necesita Bryams.

## 5. Seguridad y privacidad

Politica cerrada en 0.8.18:

- `rawFramesUploaded = false`
- `screenshotsUploaded = false`
- `secretsLogged = false`
- token por `ARIADGSM_CLOUD_TOKEN`, `OPERATIVA_AGENT_KEY`,
  `OPERATIVA_AGENT_TOKEN` o archivo secreto local ignorado por Git;
- `Authorization: Bearer <token>` hacia `ariadgsm.com`;
- `Idempotency-Key` obligatorio por lote;
- no se suben capturas, solo eventos estructurados y texto ya aceptado por la
  capa local.

Si falta token, Cloud Sync queda `blocked` con explicacion humana. Si esta
desactivado, queda `idle`. Si se prueba sin publicar, queda `dry_run`.

## 6. Idempotencia y no duplicacion

Cada lote se identifica con:

```text
cloudsync-<hash del payload>
```

El agente local guarda los `messageKey` enviados en `cloud-sync-ledger.json`.
El servidor tambien revisa `idempotencyKey` antes de ingerir eventos.

Resultado:

- si Windows, red o Railway hacen repetir el envio, el panel lo marca como
  duplicado y no crea registros dobles;
- si un mensaje ya fue enviado, el agente no lo prepara otra vez;
- si un evento individual falla, el lote no se pierde completo: se registra
  `eventsRejected` y queda visible para revisar.

## 7. Resiliencia

Cloud Sync aplica:

- maximo 3 intentos por lote;
- backoff corto entre intentos;
- reintentos solo ante codigos transitorios;
- estado `attention` si no se pudo publicar;
- circuito `open` cuando falla;
- el agente local sigue operando aunque la nube caiga.

Esto evita que una caida de ariadgsm.com tumbe ojos, memoria, manos o cerebro.

## 8. Panel ariadgsm.com

El endpoint final es:

```text
POST /api/operativa-v2/cloud/sync
```

El servidor:

- exige token o sesion autenticada;
- limita tamano de payload;
- procesa hasta 250 eventos por lote;
- ingiere eventos validos;
- acumula errores por evento;
- registra batch con runtime, mensajes, conversaciones, contabilidad y
  aprendizaje;
- expone duplicados y rechazos en el panel `operativa-v2`.

## 9. Definicion de terminado

Etapa 14 queda cerrada cuando:

- existe documento final;
- existe contrato `cloud_sync_state`;
- existe motor local `cloud_sync.py`;
- Runtime Kernel conoce Cloud Sync como motor;
- app Windows muestra Cloud Sync en salud y actividad;
- el ciclo Python ejecuta Cloud Sync al final;
- `ariadgsm.com` recibe lote con idempotencia;
- el panel distingue nuevo lote, duplicado y eventos rechazados;
- hay pruebas locales de contrato, dry-run, endpoint local e idempotencia;
- se versiona y empaqueta el ejecutable.

## 10. Limites honestos

Esta etapa no convierte la nube en cerebro. La nube no decide por el negocio.
La nube muestra, respalda, reporta y sincroniza.

La autonomia final depende de la Etapa 15:

- pruebas largas;
- metricas de lectura/decision/accion/aprendizaje;
- instalador y updater final;
- rollback;
- release estable.

## 11. Fuentes

- Microsoft Azure Architecture Center: Retry pattern:
  `https://learn.microsoft.com/en-us/azure/architecture/patterns/retry`
- Microsoft Azure Architecture Center: Circuit Breaker pattern:
  `https://learn.microsoft.com/en-us/azure/architecture/patterns/circuit-breaker`
- OWASP API Security Top 10 2023:
  `https://owasp.org/API-Security/editions/2023/en/0x11-t10/`
- OpenTelemetry Documentation:
  `https://opentelemetry.io/docs/`
- Railway Docs: Variables:
  `https://docs.railway.com/variables`
- Railway Docs: Volumes:
  `https://docs.railway.com/volumes/reference`
