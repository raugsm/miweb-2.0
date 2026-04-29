# AriadGSM Product Runtime Packaging + Live Boot Chain Closure

Version: 0.9.12

## Fuentes base

- Microsoft .NET `File.Replace` / `File.Move`: estados de producto se escriben en archivo temporal y se publican con reemplazo atomico/reintentable.
- Microsoft .NET `Process.Kill(entireProcessTree: true)`: si un proceso propio no responde a apagado amable, el runtime debe detener su arbol y esperar salida.
- Microsoft Windows Job Objects: los procesos propios se agrupan bajo Job Object con `KILL_ON_JOB_CLOSE`; los navegadores quedan fuera de propiedad.
- Microsoft UI Automation / WinEvents / DWM: la realidad de ventana no puede depender solo del titulo; debe trackear visibilidad, cambios y disponibilidad.
- Playwright Actionability: antes de actuar se valida que el objetivo sea visible, estable y pueda recibir eventos; ese criterio se replica en Cabin Authority + Window Reality.
- OpenTelemetry/observability practice: cada fallo operativo debe dejar evidencia con causa, correlacion y reporte humano.

## Decision

Esta entrega no crea una capa nueva. Cierra el Paso 1 dentro de Capa 1, Capa 2, Capa 3, Capa 4, Capa 7 y Capa 8.

La causa viva detectada en logs recientes fue una cadena incompleta de arranque/apagado:

1. `Hands` se caia por `UnauthorizedAccessException` al escribir `hands-state.json`.
2. `Stop` dejaba workers vivos cuando el apagado coincidia con procesos en arranque o subprocess Python en curso.
3. `wa-3` podia desaparecer/cambiar de estado sin un evento de ciclo de vida claro.
4. `Trust & Safety` se volvia viejo y Hands bloqueaba aunque el Control Plane siguiera vivo.
5. La evaluacion del Paso 1 no validaba de punta a punta arranque -> lectura inicial -> pausa/cierre limpio.

La correccion de arquitectura es formalizar el boot chain:

- Safe State Writer evita que un lock temporal o antivirus tumbe Hands.
- Process Ownership Shutdown cancela loops, mata subprocess propios cancelados y limpia workers propios sin tocar Edge/Chrome/Firefox.
- Window Lifecycle Tracking publica `window-lifecycle-events.jsonl` para perdida/restauracion/cambio/tapado de wa-1/2/3.
- Trust & Safety Heartbeat refresca permisos cortos separado del ciclo Python pesado.
- End-to-End Boot/Shutdown Evaluation queda cubierta por pruebas automatizadas de contratos, codigo y paquete.

## Contrato operativo

- `cabin-readiness.json`: fuente estructural/visual de ventanas.
- `reader-core-state.json`: fuente semantica y mensajes frescos.
- `input-arbiter-state.json`: autoridad de operador; si Bryams usa mouse/teclado, manos ceden.
- `hands-state.json`: telemetria de actuacion, no evidencia primaria de realidad.
- `window-lifecycle-events.jsonl`: historial incremental de vida real de wa-1/2/3.
- `trust-safety-state.json`: permiso vivo; debe actualizarse por heartbeat mientras la IA este encendida.
- `runtime-governor-state.json`: ownership de procesos propios; navegadores no son propiedad de la IA.

## Empaquetado

El paquete de producto ahora debe incluir:

- ejecutable desktop;
- workers .NET;
- configs de workers;
- `desktop-agent/ariadgsm_agent`;
- `desktop-agent/contracts`;
- panel local Node/public;
- `ariadgsm-version.json`.

El self-test del ejecutable falla si el paquete no contiene estos archivos. Esto evita volver a publicar una version que solo funcione por estar al lado del repo.

## Definicion De Terminado

- Hands no se cae si Windows bloquea temporalmente la escritura de estado.
- Pausar/cerrar cancela loops y no deja workers propios vivos.
- El sistema emite eventos cuando una ventana WhatsApp se pierde, cambia, queda tapada o vuelve.
- Trust & Safety mantiene heartbeat fresco mientras la IA este encendida.
- Window Reality no queda bloqueado por Hands viejo o ausente.
- El paquete contiene nucleos Python y contratos.
- El self-test del paquete valida runtime minimo.
- Version y update manifest apuntan al paquete generado.
