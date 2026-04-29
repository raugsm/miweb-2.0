# AriadGSM Product Runtime Packaging + Live Boot Chain Closure

Version: 0.9.11

## Fuentes base

- Microsoft .NET deployment: empaquetado debe declarar archivos requeridos y tener prueba de arranque del paquete.
- Python Windows embeddable/runtime guidance: el runtime Python debe estar separado de la app y el paquete debe resolver rutas de modulos de forma explicita.
- Microsoft Windows process/job ownership patterns: el proceso padre debe saber que arranca y que detiene.
- OpenTelemetry/observability practice: cada fallo operativo debe dejar evidencia con causa, no solo logs sueltos.

## Decision

Esta entrega no crea una capa nueva. Cierra una deuda operativa entre Capa 2, Capa 3, Capa 4, Capa 5, Capa 7 y Capa 8.

La causa viva detectada fue una dependencia circular:

1. Window Reality declaraba una ventana no accionable si `hands-state.json` estaba viejo.
2. Hands no podia arrancar acciones si Window Reality no declaraba la ventana accionable.
3. Resultado: la IA quedaba "iniciada" pero sin movimiento real.

La correccion de arquitectura es separar autoridad:

- Window Reality decide realidad de ventana usando cabina, lector, frescura e Input Arbiter.
- Hands consume esa realidad y verifica acciones despues.
- Hands puede estar viejo como telemetria, pero no bloquea la verdad de ventana.

## Contrato operativo

- `cabin-readiness.json`: fuente estructural/visual de ventanas.
- `reader-core-state.json`: fuente semantica y mensajes frescos.
- `input-arbiter-state.json`: autoridad de operador; si Bryams usa mouse/teclado, manos ceden.
- `hands-state.json`: telemetria de actuacion, no evidencia primaria de realidad.

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

- Window Reality no queda bloqueado por Hands viejo o ausente.
- Tests automatizados validan que Hands viejo sea telemetria.
- El paquete contiene nucleos Python y contratos.
- El self-test del paquete valida runtime minimo.
- Version y update manifest apuntan al paquete generado.
