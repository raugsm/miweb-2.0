# AriadGSM Web Hardening Annex

Estado: anexo operativo de endurecimiento para RC 0.9.2 sobre etapas cerradas 11, 14 y 15.
Version objetivo: 0.9.10.

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

- `cloud_sync.py` firma el cuerpo exacto del batch con HMAC SHA-256 usando
  `OPERATIVA_AGENT_KEY`.
- El servidor verifica `X-AriadGSM-Signature: sha256=...` antes de procesar el
  batch. Sin firma o con firma invalida, responde `401`.
- El servidor aplica rate limit por agent token, base 60 req/min, configurable
  con `ARIADGSM_CLOUD_SYNC_RATE_LIMIT_PER_MINUTE`.
- El servidor emite HSTS `max-age=31536000; includeSubDomains; preload`.
- El cliente Python fuerza TLS 1.3 para destinos HTTPS.
- Cada lote genera audit log append-only en `cloud-sync-audit.jsonl` con
  `lote_id`, `agent_id`, `timestamp`, `hash` y `verdict`:
  `new`, `duplicate` o `rejected`.

## Rotacion de OPERATIVA_AGENT_KEY

1. Generar una nueva clave de al menos 32 bytes aleatorios.
2. Configurar la nueva clave en el servidor cloud como `OPERATIVA_AGENT_KEY`.
3. Configurar la misma clave en la maquina local del agente.
4. Ejecutar `desktop-agent/tests/web_hardening_cloud_sync.py`.
5. Revisar que los lotes nuevos queden con verdict `new` y que una firma vieja
   reciba `401`.
6. Eliminar la clave anterior de variables de entorno, archivos secret y notas
   operativas.

## Gates de validacion

- `desktop-agent/tests/web_hardening_panel.py`: headers, token de arranque,
  CSRF, CSP, compresion, cache immutable, presupuesto < 800 ms, bundle < 100 KB,
  paginacion y escape HTML.
- `desktop-agent/tests/web_hardening_cloud_sync.py`: HMAC requerido, firma
  invalida rechazada, lote nuevo/duplicado aceptado y audit log append-only.
- `desktop-agent/ariadgsm_agent/release_evaluation.py`: gate 15.8 Web
  Hardening publica verdict dentro de Evaluation + Release.
