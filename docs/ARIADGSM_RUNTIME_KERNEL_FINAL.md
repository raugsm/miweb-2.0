# AriadGSM Runtime Kernel

Version: 0.8.17
Estado: Etapa 0.5 obligatoria antes de Cloud Sync
Fecha: 2026-04-28

## 1. Por que existe

El fallo real visto en produccion local no fue "un boton malo". Vision cayo por
`Access denied`, el supervisor lo reinicio, pero la interfaz seguia leyendo
varios estados sueltos y pudo mostrar una conclusion incorrecta como "motor no
activo".

La raiz era arquitectonica:

- `Life Controller` decia si la IA queria estar encendida;
- `Agent Supervisor` decia si los procesos vivos existian;
- `Vision`, `Perception`, `Hands`, `Cabin Authority` y Python publicaban sus
  propios JSON;
- la UI hacia su propia mezcla;
- los errores recientes estaban en logs, no en una autoridad unica.

Runtime Kernel convierte todo eso en una sola verdad operacional.

## 2. Investigacion usada

La solucion sigue patrones de arquitectura agentica y sistemas durables:

- Microsoft AI Agent Orchestration recomienda separar la coordinacion de agentes
  y tratar la autonomia como componentes coordinados, no como funciones sueltas:
  https://learn.microsoft.com/en-us/azure/architecture/ai-ml/guide/ai-agent-design-patterns
- Temporal describe ejecucion durable con estado persistido, reintentos y
  reanudacion desde el ultimo punto confiable:
  https://temporal.io/home
- LangGraph documenta checkpoints durables para que cada paso guarde estado y
  pueda reanudarse:
  https://docs.langchain.com/oss/python/langgraph/durable-execution
- OpenTelemetry define observabilidad con logs, trazas y metricas uniformes:
  https://opentelemetry.io/docs/
- Microsoft UI Automation confirma que los sistemas que operan Windows deben
  trabajar sobre arboles/eventos de UI cuando sea posible:
  https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification
- OpenAI Agent Evals recomienda medir agentes con evaluaciones reproducibles y
  trazas para detectar errores de workflow:
  https://platform.openai.com/docs/guides/agent-evals

Conclusion aplicada: AriadGSM necesita un kernel local que normalice estado,
incidentes, permisos, procesos, reinicios y capacidad de actuar antes de que la
IA tome decisiones de negocio o sincronice con nube.

## 3. Responsabilidad

Runtime Kernel es la autoridad local sobre:

- si la IA desea estar encendida;
- que motores estan `running`, `starting`, `degraded`, `restarting`,
  `blocked`, `dead` o `stopped`;
- que incidentes ocurrieron y cual fue la recuperacion;
- si la IA puede observar, pensar, actuar o sincronizar;
- si la cabina esta lista, parcial, tapada o bloqueada;
- si una alerta requiere a Bryams.

No reemplaza Vision, Hands, Memory ni Business Brain. Los coordina como verdad
operacional.

## 4. Contrato

Archivo:

```text
desktop-agent/contracts/runtime-kernel-state.schema.json
```

Estado runtime:

```text
desktop-agent/runtime/runtime-kernel-state.json
```

Reporte humano:

```text
desktop-agent/runtime/runtime-kernel-report.json
```

Campos clave:

- `authority`: capacidades actuales: observar, pensar, actuar y sincronizar;
- `engines`: ciclo de vida por motor;
- `incidents`: fallos normalizados, no solo lineas de log;
- `recovery`: reinicios y ultimo checkpoint operacional;
- `humanReport`: mensaje entendible para Bryams.

## 5. Regla de verdad unica

La interfaz puede mostrar detalles de motores individuales, pero el estado
principal debe venir de:

```text
runtime-kernel-state.json
```

Eso evita mensajes falsos como:

```text
motor no activo
```

cuando la verdad era:

```text
Vision se reinicio por acceso denegado y ya volvio a correr.
```

## 6. Manejo de incidentes

Cada incidente debe tener:

- `incidentId`;
- `severity`;
- `source`;
- `code`;
- `summary`;
- `detail`;
- `detectedAt`;
- `recoveryAction`;
- `requiresHuman`.

Ejemplos:

- `engine_restart`: un motor cayo y el supervisor lo reinicio;
- `state_write_denied`: Windows nego escritura de estado;
- `workspace_covered`: una ventana cubre un WhatsApp;
- `operator_control`: Bryams esta usando mouse/teclado;
- `stale_state`: un motor esta vivo pero no actualiza estado.

## 7. Politica de actuacion

Runtime Kernel no ejecuta negocio. Solo decide la capacidad operacional:

- `canObserve`: ojos vivos o degradados pero recuperables;
- `canThink`: Python core y estados mentales disponibles;
- `canAct`: Hands permitido, cabina lista e Input Arbiter no cedio el mouse;
- `canSync`: solo si el estado local esta estable o degradado explicable.

Si hay incertidumbre fuerte:

```text
canAct = false
canObserve = true
canThink = true
```

La IA sigue aprendiendo, pero no pelea mouse ni toma acciones fisicas.

## 8. Cambios tecnicos de 0.8.17

- Se inserta Etapa 0.5 antes de Cloud Sync.
- Se agrega contrato `runtime_kernel_state`.
- Se agrega modulo Python `ariadgsm_agent.runtime_kernel` para validacion,
  pruebas y generacion del contrato.
- La app Windows escribe `runtime-kernel-state.json` desde el runtime real.
- La UI usa Runtime Kernel para titular y explicar estado humano.
- Vision ya no debe caerse por un fallo transitorio de escritura de estado:
  reintenta `IOException` y `UnauthorizedAccessException`, y si no puede escribir
  el estado principal usa un fallback local.

## 9. Definicion de terminado

Etapa 0.5 queda cerrada si:

- Execution Lock reconoce 0.5 antes de 14;
- Cloud Sync queda congelado hasta cerrar Runtime Kernel;
- existe documento final;
- existe contrato JSON;
- existe implementacion Python verificable;
- la app Windows publica Runtime Kernel;
- la UI lee ese estado para explicar salud real;
- Vision no cae por escritura denegada de estado;
- pruebas validan contrato, incidentes y roadmap;
- se compila y se empaqueta version nueva.

## 10. Siguiente etapa

Cuando 0.8.17 queda publicada, Execution Lock vuelve a:

```text
Etapa 14: Cloud Sync / ariadgsm.com
```

Cloud Sync no debe asumir estados sueltos. Debe consumir Runtime Kernel como
verdad local antes de subir reportes o memoria aprobada.
