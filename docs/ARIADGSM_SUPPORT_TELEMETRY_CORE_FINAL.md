# AriadGSM Support & Telemetry Core Final

Fecha: 2026-04-28
Version objetivo: 0.9.2
Estado: capa transversal integrada

## 1. Proposito

Support & Telemetry Core es la caja negra local de AriadGSM IA Local. Su trabajo
no es mover WhatsApp ni decidir por el negocio: su trabajo es explicar por que
la IA observa, decide, falla, se detiene, no actua o necesita ayuda.

Esta capa existe para que Bryams no tenga que adivinar si "no hizo nada", si un
motor cayo, si la cabina estaba tapada, si la nube rechazo un lote o si la IA se
pauso por seguridad.

## 2. Investigacion aplicada

La implementacion se basa en estas fuentes externas:

- OpenTelemetry .NET: señales de trazas, metricas y logs con `traceId` y
  correlacion entre componentes.
- Microsoft .NET Observability: observabilidad continua con bajo impacto,
  combinando logs, metricas y distributed tracing.
- Microsoft EventSource/EventPipe: eventos estructurados nativos de .NET para
  diagnostico sin depender de consolas visibles.
- Windows Error Reporting LocalDumps: dumps locales configurables para crashes,
  tratados aqui como metadatos locales y no como datos subibles automaticamente.
- OWASP Logging Cheat Sheet: no registrar tokens, passwords, datos personales,
  chats completos, rutas sensibles ni informacion que no sea necesaria para
  soporte.

## 3. Principio de privacidad

Regla fija:

```text
Nada de capturas, chats completos, contrasenas, tokens ni sesiones internas se
sube o empaqueta sin permiso explicito.
```

El Support Bundle incluye:

- estado de soporte redactado;
- eventos de telemetria resumidos;
- cola de caja negra local;
- cola de logs redactada;
- estados JSON redactados.

Queda excluido por diseno:

- screenshots;
- raw frames;
- transcripciones completas;
- dumps de memoria;
- archivos `.secret`;
- tokens y cookies.

## 4. Contratos

### `support_telemetry_state`

Archivo:

```text
desktop-agent/contracts/support-telemetry-state.schema.json
```

Describe:

- estado general de soporte;
- `traceId`;
- `correlationId`;
- fuentes revisadas;
- incidentes abiertos;
- caja negra local;
- Support Bundle;
- privacidad;
- reporte humano.

### `support_telemetry_event`

Archivo:

```text
desktop-agent/contracts/support-telemetry-event.schema.json
```

Cada incidente queda como evento con:

- fuente;
- severidad;
- categoria;
- resumen;
- detalle redactado;
- evidencia;
- accion recomendada;
- politica de privacidad.

## 5. Caja negra local

Archivo:

```text
desktop-agent/runtime/support-blackbox.jsonl
```

Retencion:

- maximo 500 ciclos;
- maximo logico 24 MB;
- local only;
- no se borra cada vez que un motor cae.

La caja negra permite reconstruir:

- ultimo `traceId`;
- ultimo `correlationId`;
- incidentes por ciclo;
- criticidad;
- si hubo bloqueo de privacidad.

## 6. Support Bundle

Ruta:

```text
desktop-agent/runtime/support/support-bundle-latest.zip
```

El bundle se genera localmente y queda listo para soporte, pero no se sube solo.
La nube solo recibe resumen seguro mediante Cloud Sync.

## 7. Integraciones

- Runtime Kernel: Support consume `runtime-kernel-state.json` y sus incidentes.
- Runtime Governor: detecta ownership, procesos vivos y apagado.
- Window Reality Resolver: explica si una ventana no era operable.
- Autonomous Cycle: correlaciona observacion, decision, permiso, accion y
  verificacion.
- Cabin Authority: explica fallos de alistamiento sin cerrar navegadores.
- Reader Core: explica lectura, ruido y fuentes.
- Trust & Safety + Input Arbiter: explica bloqueos por riesgo o control humano.
- Hands & Verification: explica acciones planeadas, bloqueadas o verificadas.
- Updater / Release: expone version, paquete, rollback y release gates.
- ariadgsm.com: recibe solo resumen de soporte seguro, no bundle ni datos
  sensibles.

## 8. Definicion de terminado

Queda cerrado cuando:

- existe motor `ariadgsm_agent.support_telemetry`;
- existen contratos JSON de estado e incidente;
- el ciclo principal ejecuta Support antes de Cloud Sync;
- la UI muestra estado humano de soporte;
- Cloud Sync prepara eventos seguros de soporte;
- Evaluation + Release valida documento, contratos y bundle;
- pruebas simulan crash, hang y fuga de privacidad;
- el paquete se versiona como 0.9.2.

## 9. Limites conscientes

- No configura automaticamente el registro de Windows para LocalDumps porque eso
  modifica el sistema y puede capturar memoria sensible. El Core detecta
  metadatos de dumps existentes y documenta que cualquier subida requiere
  permiso explicito.
- No sustituye logs tecnicos: los resume y correlaciona para que sean utiles.
- No hace soporte remoto en vivo todavia; deja preparada la estructura segura
  para que luego la app pueda compartir solo lo autorizado.
