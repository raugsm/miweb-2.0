# AriadGSM Operational Truth Matrix

Fecha: 2026-04-28  
Version auditada: 0.9.9  
Commit auditado: eb2d9a9  
Objetivo: separar lo que esta escrito, lo que compila, lo que pasa pruebas y lo que realmente funciona en la PC.

## Regla principal

Desde este punto, ninguna capa debe llamarse "100%" o "cerrada" solo porque existe codigo, contratos, pruebas o build.

Una capa solo puede llamarse cerrada para producto cuando cumple las cuatro verdades:

1. Codigo implementado.
2. Pruebas automatizadas ejecutadas.
3. Build y paquete generados.
4. Prueba real supervisada en la PC demuestra que funciona dentro del flujo completo.

Si falta cualquiera de esas cuatro, el estado correcto debe decir exactamente que falta.

## Estados permitidos

`FOUNDATION_ONLY`: existe base tecnica, pero todavia no demuestra la capacidad real.

`CODE_READY`: el codigo principal existe.

`TEST_READY`: las pruebas automatizadas y build pasan.

`LIVE_PARTIAL`: funciono parcialmente en la PC real, pero no cerro el flujo completo.

`LIVE_NOT_REACHED`: la capa no llego a ejecutarse en la prueba real.

`LIVE_INCOMPLETE`: la capa se ejecuto, pero dejo al operador sin claridad, se detuvo, o no llevo al siguiente estado.

`PRODUCT_CLOSED`: probado en vivo, entendible para el operador, durable, auditable y sin bloqueo oculto.

## Matriz real por capa

| Capa | Nombre | Estado real | Evidencia | Que falta para cerrarla |
| --- | --- | --- | --- | --- |
| 1 | Operator Product Shell | LIVE_INCOMPLETE | La interfaz existe, muestra version y estados, pero el usuario aun no puede entender con claridad por que la IA queda detenida o parece no hacer nada. El documento final tambien la marca como base funcional, no consolidada final. | Progreso humano por fases de arranque, causa visible de espera, causa visible de stop, y una pantalla que explique sin leer logs. |
| 2 | AI Runtime Control Plane | TEST_READY + LIVE_INCOMPLETE | En la ultima prueba se acepto `start`, se creo `runSessionId` y llego a `preflight`, pero no se alcanzo inicio real de workers ni `python_core` antes de que el usuario cerrara la app por parecer detenida. | Cerrar cadena viva de arranque: login/update -> cabina -> start -> preflight -> workers -> python_core -> reader -> timeline -> readiness. |
| 3 | Cabin Reality Authority | LIVE_PARTIAL | Alistar WhatsApps logro 3/3. Luego detecto correctamente que `wa-1` fue tapado por Administrador de tareas y degrado el estado. El usuario confirmo que esa cobertura fue manual. | Validacion prolongada con muchas apps abiertas, sin cerrar Edge/Chrome/Firefox y sin confundir WhatsApp Desktop. |
| 4 | Perception & Reader Core | TEST_READY + LIVE_NOT_REACHED | Existe estado anterior de Reader Core 0.9.8, pero en la prueba 0.9.9 no se genero estado nuevo. La capa no llego a demostrar lectura fresca dentro del ultimo arranque. | Reader fresco por canal dentro del arranque 0.9.9, con evidencia de mensajes reales y bloqueo explicito si la lectura esta vieja. |
| 5 | Event, Timeline & Durable State Backbone | TEST_READY + LIVE_NOT_REACHED | Codigo 0.9.9, pruebas y paquete quedaron hechos, pero `event-backbone-state.json` no existe en runtime. Eso significa que no se ejecuto en la prueba real. | Demostrar offsets, checkpoints, compactacion y timeline fresco durante una sesion real, no solo en test. |
| 6 | Living Memory & Business Brain | FOUNDATION_ONLY | La documentacion lo reconoce como base operativa, no IA de negocio completa. Existen estados de memoria y contabilidad, pero no un cerebro final que razone como operador AriadGSM. | Convertir lecturas reales en conocimiento: clientes, casos, precios, rutas, servicios GSM, contabilidad, dudas, correcciones y estilo de respuesta. |
| 7 | Action, Tools & Verification | TEST_READY + LIVE_NOT_REACHED | La capa de accion transaccional existe, pero en la ultima prueba no hubo lectura fresca ni canal listo para actuar. Por eso manos no tenia permiso real de moverse. | Probar una accion segura con lease de canal, percepcion fresca antes, verificacion despues y razon humana si se bloquea. |
| 8 | Trust, Telemetry, Evaluation & Cloud | LIVE_PARTIAL | La telemetria permitio reconstruir la causa: arranque llego a preflight y luego cierre manual. Pero la app no lo explico de forma suficientemente clara al operador. | Caja negra visible, bundle de soporte, evaluacion de sesion real y reporte humano que diga que paso sin inspeccion manual profunda. |

## Contradicciones encontradas

1. El roadmap llama Product Shell "cerrada como shell operativo", pero la arquitectura final dice que Capa 1 sigue sin consolidacion final.
2. El roadmap llama Safe Eyes / Reader Core cerrada, pero en la misma seccion mantiene lista de cosas por cerrar.
3. Capa 2 fue llamada consolidada, pero la prueba real quedo en `preflight` sin que la UI guiara al operador hasta la causa exacta.
4. Evaluation + Release fue tratada como release candidate, pero todavia exige prueba supervisada real con 3 WhatsApps y corrida larga.
5. Capa 5 quedo bien como codigo y pruebas, pero no como cierre vivo porque nunca aparecio `event-backbone-state.json` en runtime.

## Diagnostico honesto

El producto no esta fallando porque una sola capa este "mal". Esta fallando porque se declaro cierre tecnico antes de exigir cierre operativo.

El estado real hoy es:

- Hay plataforma seria.
- Hay motores separados.
- Hay versionado, updater, telemetria y contratos.
- Hay avances en cabina, lectura, timeline y manos.
- Pero la cadena viva de arranque no esta cerrada de punta a punta.

Mientras esa cadena no cierre, seguir avanzando a nuevas funciones de negocio va a parecer progreso, pero no va a dar autonomia real.

## Gate obligatorio de cierre operativo

Antes de volver a llamar una capa "100%", una prueba real debe demostrar esta secuencia:

1. La app abre y muestra version correcta.
2. Login/update termina sin estado ambiguo.
3. Alistar WhatsApps deja Edge = wa-1, Chrome = wa-2 y Firefox = wa-3 sin cerrar navegadores.
4. Encender IA crea `runSessionId`.
5. Control Plane avanza de `preflight` a workers activos.
6. Python core arranca y reporta estado.
7. Event Backbone escribe estado con la version actual.
8. Reader Core entrega lectura fresca por canal.
9. Timeline recibe eventos nuevos sin releer archivos gigantes.
10. Trust & Safety decide si manos pueden actuar.
11. Hands actua o se bloquea con una razon humana concreta.
12. La interfaz explica todo lo anterior sin que el usuario tenga que abrir logs.

Si cualquiera de esos puntos falla, la capa relacionada queda en `LIVE_INCOMPLETE` o `LIVE_NOT_REACHED`, no en `PRODUCT_CLOSED`.

## Siguiente cierre real

No hace falta inventar otra capa para este problema.

Lo siguiente correcto es cerrar la cadena viva de arranque:

`Login / Update -> Cabin Ready -> Start Session -> Preflight -> Workers -> Python Core -> Event Backbone -> Reader Fresh -> Timeline -> Trust -> Hands`

Este cierre debe tratarse como validacion operativa del producto, no como parche.

Nombre interno recomendado: `Live Boot Chain Closure`.

No es una etapa nueva de arquitectura. Es el criterio que faltaba para comprobar que las capas ya construidas viven juntas en la PC real.
