# AriadGSM Web Hardening Annex

Estado: anexo operativo de endurecimiento para RC 0.9.2 sobre etapas cerradas 11, 14 y 15.
Version objetivo de laboratorio: 0.9.10.
Version desplegable del canal vivo: 0.9.16.

Este archivo no abre una etapa nueva. Declara controles web minimos para que la
prueba real supervisada de cabina completa no dependa solo de privacidad LLM,
idempotencia y evidencia A-F.

## Fuentes revisadas

- OWASP ASVS v4.0.3: V1 Architecture, V8 Data Protection, V9 Communications,
  V13 API and V14 Configuration.
- OWASP Web Top 10 mas reciente: OWASP Top 10 2025.
- MDN Web Docs: Content Security Policy, Strict-Transport-Security,
  Referrer-Policy, Permissions-Policy and SameSite cookies.
- web.dev: Core Web Vitals and performance budgets.
- IETF RFC 9110, RFC 9111 and RFC 7235.
- Node.js official docs: crypto HMAC/timing-safe comparison, zlib and HTTP
  headers.
- Python official docs: ssl TLSVersion TLSv1_3 and hmac.
- IETF RFC 9421: HTTP Message Signatures, considerado antes de mantener el
  contrato HMAC simple en el canal 0.9.15/0.9.16.
- Render docs: environment variables, deploy hooks and health checks.
- hstspreload.org / Cloudflare HSTS preload requirements.
- OWASP Secrets Management and Cryptographic Storage Cheat Sheets.
- OpenAI safety guidance for output handling: model output rendered into HTML
  must be treated as untrusted text unless explicitly sanitized/escaped.

## Decisiones

### Panel local Node

- La cabina `operativa-v2.html` recibe un token efimero generado al arranque y
  expuesto solo como meta tag del HTML servido.
- Las llamadas del panel a `/api/operativa-v2*` deben enviar
  `X-AriadGSM-Session` con ese token; una cookie de login por si sola no cuenta
  como autorizacion de cabina.
- Las mutaciones iniciadas por navegador usan doble token CSRF:
  cookie `ariadgsm_csrf` + header `X-AriadGSM-CSRF`.
- La entrada de agente local por token sigue existiendo para `/events`, pero no
  sustituye la firma HMAC de Cloud Sync.
- El CSP queda estricto:
  `default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:;
  connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'`.
- Los assets con query `?v=0.9.10` reciben
  `Cache-Control: public, max-age=31536000, immutable`.
- Los assets textuales se sirven con Brotli o gzip segun `Accept-Encoding`.
- La cabina deja de depender de polling periodico para Runtime Kernel y usa SSE
  en `/api/operativa-v2/runtime/stream`.
- Conversaciones, mensajes, senales y aprendizajes se entregan por pagina de
  cursor para evitar que la cabina cargue colecciones completas.

### Cloud Sync y ariadgsm.com

- `cloud_sync.py` firma el cuerpo exacto del batch con HMAC SHA-256 usando el
  `agentToken` leido desde `scripts/visual-agent/visual-agent.config.json`.
- El servidor verifica `X-AriadGSM-Signature: sha256=...` antes de procesar el
  batch. Sin firma o con firma invalida, responde `401`.
- El servidor aplica rate limit por agent token, base 60 req/min, configurable
  con `ARIADGSM_CLOUD_SYNC_RATE_LIMIT_PER_MINUTE`.
- El servidor emite HSTS `max-age=31536000; includeSubDomains; preload`.
- El cliente Python fuerza TLS 1.3 para destinos HTTPS.
- Cada lote genera audit log append-only en `cloud-sync-audit.jsonl` con
  `lote_id`, `agent_id`, `timestamp`, `hash` y `verdict`:
  `new`, `duplicate` o `rejected`.
- El esquema compartido del audit log queda versionado como
  `desktop-agent/contracts/audit-log-entry.schema.json` en el agente y como
  `server/contracts/audit-log-entry.schema.json` en cloud.

## Contrato de firma agente <-> cloud

Decision 0.9.16: se mantiene HMAC SHA-256 sobre el cuerpo exacto del batch para
no romper el canal vivo; el cambio de 0.9.16 consolida la fuente local del
secreto sin alterar el contrato wire. RFC 9421
queda registrado como siguiente estandar candidato porque cubre componentes
HTTP y canonicalizacion formal; no se adopta en 0.9.16 porque el contrato actual
solo necesita autenticar el body JSON exacto que ya controla el agente y porque
el servidor cloud debe aceptar el mismo header ya probado en laboratorio.

- Header: `X-AriadGSM-Signature`.
- Formato: `sha256=<hex-digest>`.
- Algoritmo: `HMAC-SHA-256`.
- Material firmado: bytes exactos del cuerpo HTTP enviado por el agente.
- Secreto: `agentToken` en `scripts/visual-agent/visual-agent.config.json`;
  el mismo valor debe estar en Render como `OPERATIVA_AGENT_KEY`.
- Comparacion servidor: constante en tiempo (`timingSafeEqual` en Node cuando
  aplica; comparacion segura equivalente en Python tests).
- Firma ausente: `401` con `{ "error": "signature_missing" }`.
- Firma invalida: `401` con `{ "error": "signature_invalid" }`.
- Rate limit cloud: por `agentToken`, base 60 req/min, configurable con
  `ARIADGSM_CLOUD_SYNC_RATE_LIMIT_PER_MINUTE`.
- Respuesta rate limit: `429` con header `Retry-After`.

## Single source of truth for agent secret

Decision 0.9.16: la fuente local autorizada del secreto del agente es solo
`scripts/visual-agent/visual-agent.config.json`, campo `agentToken`. El cloud
desplegado conserva el mismo valor en Render como `OPERATIVA_AGENT_KEY`; esa
variable es la copia operativa del servidor, no una segunda fuente local.

Justificacion:

- Es el menor cambio compatible con Python, PowerShell y Node: los scripts
  apuntan al mismo JSON local y no requieren `dotenv`, keyring ni dependencias
  nuevas en pleno RC.
- Mantiene el secreto fuera de Git mediante `.gitignore` y evita que la clave se
  duplique en `visual-agent.cloud.json`, archivos `.secret` o variables locales
  persistentes.
- Alinea el bloque con OWASP ASVS V8 y las guias OWASP de secrets management:
  no hardcodear claves, no versionarlas, documentar rotacion y limitar copias.

Reglas operativas:

- `cloud_sync.py` resuelve el token desde
  `scripts/visual-agent/visual-agent.config.json:agentToken`.
- `visual-agent.cloud.json` y `railway-operativa-agent-key.secret.txt` quedan
  como copias legacy no eliminadas en este bloque. La limpieza ocurre solo
  despues de validacion live verde.
- El fallback Railway queda apagado por defecto. Si Bryams confirma uso vivo de
  Railway, se habilita temporalmente con `ARIADGSM_USE_RAILWAY_FALLBACK=true`;
  sin esa variable, `cloud_sync.py` no lee el archivo legacy.

## Rotacion de OPERATIVA_AGENT_KEY

1. Generar una nueva clave de al menos 32 bytes aleatorios.
2. Escribirla solo en `scripts/visual-agent/visual-agent.config.json`, campo
   `agentToken`.
3. Bryams copia manualmente ese valor a Render como `OPERATIVA_AGENT_KEY`.
4. Manual Deploy en Render.
5. Ejecutar `desktop-agent/tests/web_hardening_cloud_sync.py`.
6. Revisar que los lotes nuevos queden con verdict `new` y que una firma vieja
   reciba `401`.
7. Eliminar la clave anterior de variables de entorno, archivos secret y notas
   operativas.

## Validacion end-to-end sin exponer secretos

No validar Cloud Sync live con `curl` manual usando la clave en variables de
entorno, argumentos de terminal, historial de shell o chats. Aunque el comando
no imprima el valor, ese flujo aumenta el riesgo de fuga por historial,
telemetria de herramientas o copia accidental.

Procedimiento seguro:

1. Mantener la clave solo en `scripts/visual-agent/visual-agent.config.json` y
   en `OPERATIVA_AGENT_KEY` del entorno Render.
2. Ejecutar el agente local 0.9.16 para que publique un lote de test real contra
   `ariadgsm.com`.
3. Validar en el servidor que el audit log append-only registro el lote con
   verdict `new`.
4. Repetir el lote desde el agente si se necesita validar idempotencia; el
   servidor debe registrar verdict `duplicate`.
5. No copiar la clave a terminales con historial persistente ni a herramientas
   externas de prueba.

## Gates de validacion

- `desktop-agent/tests/web_hardening_panel.py`: headers, token de arranque,
  CSRF, CSP, compresion, cache immutable, presupuesto < 800 ms, bundle < 100 KB,
  paginacion y escape HTML.
- `desktop-agent/tests/web_hardening_cloud_sync.py`: HMAC requerido, firma
  invalida rechazada, lote nuevo/duplicado aceptado y audit log append-only.
- `desktop-agent/ariadgsm_agent/release_evaluation.py`: gate 15.8 Web
  Hardening publica verdict dentro de Evaluation + Release.
