# Prototipo MVP de IA para Soporte Remoto

## Que incluye

- panel web para casos
- formulario publico para clientes
- perfil editable de estilo de atencion
- modo entrenamiento con importacion manual de chats exportados
- respuestas automaticas por etapa del caso
- notificaciones por correo SMTP o registro local
- clasificacion basica de solicitudes
- asignacion automatica de procedimiento
- monitor de precios con historial y cambios recientes
- conexion preparada para Telegram con TDLib
- simulacion de mensajes de cliente e IA
- agente local basado en reporte real de `ADB`, `Fastboot` y revision USB por `pnputil`
- base de datos SQLite local
- metricas por responsable y tiempos del caso
- endpoint listo para canal de correo entrante

## Como ejecutarlo

1. Abre esta carpeta en una terminal.
2. Si quieres IA real, define `OPENAI_API_KEY` en la misma terminal.
3. Opcionalmente define `OPENAI_MODEL`. Por defecto se usa `gpt-5.4-nano`.
4. Si quieres notificaciones por correo, define variables SMTP.
5. Ejecuta `powershell -ExecutionPolicy Bypass -File .\scripts\collect-agent-diagnostics.ps1`
6. Ejecuta `node server.js`
7. Abre `http://localhost:3000`

Inicio rapido en Windows:

- haz doble clic en `iniciar-panel.bat`
- luego abre `abrir-panel.url`

Formulario publico:

- `http://localhost:3000/solicitar-soporte.html`
- `http://localhost:3000/consultar-ticket.html`

## Que puedes probar

- revisar casos de ejemplo
- crear un nuevo caso
- abrir casos desde el formulario publico
- consultar estado de un ticket desde la pagina publica
- ajustar el tono del asistente desde el panel
- revisar alertas de notificacion en el panel
- ver como la clasificacion asigna un procedimiento
- generar el reporte local del agente
- ejecutar la validacion del agente para avanzar o escalar el caso
- generar una respuesta sugerida para el cliente
- revisar el detalle tecnico que devuelve cada chequeo
- revisar metricas por responsable
- cerrar casos con motivo y tiempos de resolucion
- importar chats reales para entrenar la logica de atencion

## Siguiente evolucion recomendada

- reemplazar clasificacion simple por un modelo de IA real
- guardar datos en una base de datos
- conectar un canal real de entrada
- convertir el agente local en un servicio Windows controlado

## Variables de entorno

- `OPENAI_API_KEY`: activa clasificacion y respuestas con OpenAI
- `OPENAI_MODEL`: modelo para la API de Responses. Si no lo defines, se usa `gpt-5.4-nano`
- `SMTP_HOST`: servidor SMTP
- `SMTP_PORT`: puerto SMTP
- `SMTP_SECURE`: `true` para SMTPS directo, opcional
- `SMTP_USER`: usuario SMTP, opcional si tu servidor no lo requiere
- `SMTP_PASS`: clave SMTP, opcional si tu servidor no lo requiere
- `SMTP_FROM`: remitente de correo
- `NOTIFY_TO`: correo destino para las alertas
- `CUSTOMER_SMTP_FROM`: remitente para correos al cliente, opcional
- `INBOUND_EMAIL_TOKEN`: token compartido para aceptar correos entrantes via `POST /api/inbound/email`
- `SESSION_SECURE`: usa `true` cuando ya este detras de HTTPS
- `FORCE_HTTPS`: usa `true` si tu proxy o plataforma ya entrega HTTPS y quieres forzar redireccion

## Telegram con TDLib

Esta version ya deja preparada la integracion para leer tu cuenta de Telegram con TDLib y traer mensajes reales al panel.

Que necesitas poner en tu PC:

1. `api_id` y `api_hash` de Telegram
2. `tdjson.dll` de TDLib
3. .NET SDK para compilar el puente local si todavia no lo tienes

Archivos relacionados:

- `telegram.js`
- `scripts/TelegramTdlibBridge/Program.cs`
- `scripts/TelegramTdlibBridge/TelegramTdlibBridge.csproj`

Pasos recomendados:

1. compila el puente con `dotnet build scripts\TelegramTdlibBridge\TelegramTdlibBridge.csproj -c Release`
2. copia `tdjson.dll` a una ruta estable
3. abre la pestana `Telegram`
4. guarda `apiId`, `apiHash`, telefono y ruta a `tdjson.dll`
5. pulsa `Iniciar autorizacion`
6. si Telegram pide codigo o contrasena, envialos desde el panel
7. pulsa `Descubrir chats`
8. activa las fuentes que quieras monitorear
9. pulsa `Sincronizar ahora`

Cuando una fuente tiene `Importar ofertas` activado, el sistema intenta convertir mensajes en ofertas y las manda al monitor de precios.

Ejemplo en PowerShell:

```powershell
$env:OPENAI_API_KEY="tu_clave"
$env:OPENAI_MODEL="gpt-5.4-nano"
$env:SMTP_HOST="smtp.tudominio.com"
$env:SMTP_PORT="587"
$env:SMTP_USER="usuario"
$env:SMTP_PASS="clave"
$env:SMTP_FROM="alertas@tudominio.com"
$env:NOTIFY_TO="tu-correo@dominio.com"
$env:CUSTOMER_SMTP_FROM="soporte@tudominio.com"
$env:INBOUND_EMAIL_TOKEN="token-seguro"
node server.js
```

Ejemplo de ingreso por correo:

```powershell
Invoke-RestMethod `
  -Uri "http://localhost:3000/api/inbound/email" `
  -Method Post `
  -Headers @{ "x-inbound-token" = "token-seguro" } `
  -ContentType "application/json" `
  -Body '{
    "from":"cliente@correo.com",
    "fromName":"Cliente Mail",
    "subject":"Mi equipo no entra por ADB",
    "text":"El equipo carga pero no lo detecta la PC."
  }'
```

## Entrenamiento con chats actuales

Si todavia no quieres conectar WhatsApp, puedes ensenarle a la IA usando exportaciones reales:

1. exporta un chat de WhatsApp en texto
2. abre la pestana `Entrenamiento` en el panel
3. sube el archivo `txt` o `zip`, o pega el chat exportado
4. indica como apareces tu en esa conversacion dentro de `Tus nombres o aliases`
5. importa el chat

El sistema separa mensajes de `cliente` y `agente`, guarda el historial y te muestra un resumen.
Eso sirve para construir ejemplos reales de:

- como llega cada tipo de caso
- que datos pides
- como respondes
- como cierras o escalas

Nota:

- `txt`: soportado
- `zip`: soportado si dentro trae un `.txt`
- `rar`: todavia no soportado

Si no configuras SMTP, el sistema guarda alertas locales en `data/notifications-log.json`.

## Base de datos

La fuente principal ahora es `data/app.db`.
En el primer arranque, el sistema importa automaticamente la informacion heredada desde:

- `data/cases.json`
- `data/style-profile.json`
- `data/notifications-log.json`

Despues de eso, el panel y los endpoints trabajan sobre SQLite.

## Acceso al panel

El panel interno ahora esta protegido por usuarios. Si todavia no existe un usuario inicial:

1. abre `http://localhost:3000/login.html`
2. configura el usuario inicial
3. inicia sesion y entra al panel

Las contrasenas se guardan con hash dentro de SQLite.

## Preparacion antes de nube

Antes de subirlo, esta version ya incluye:

- usuarios separados para el equipo
- cookies listas para modo seguro con `SESSION_SECURE`
- redireccion opcional a HTTPS con `FORCE_HTTPS`
- respaldo manual de la base desde el panel
- limites basicos para importacion de chats
- anonimizado opcional al importar conversaciones

## Estilo de respuestas

El perfil de atencion se guarda en `data/style-profile.json`.
Puedes editarlo desde el panel o directamente en el archivo si quieres ajustar:

- nombre del negocio
- tono
- frases preferidas
- frases prohibidas
- notas operativas
