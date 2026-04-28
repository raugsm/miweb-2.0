# AriadGSM Evaluation + Release Final

Fecha: 2026-04-28
Version objetivo: 0.9.2
Etapa Execution Lock: 15
Estado: release candidate

## 1. Proposito

Etapa 15 convierte la plataforma interna en un candidato de producto. No agrega
otro bot ni otro parche: cierra el cuerpo operativo de la IA para que pueda
observar, entender, decidir, recordar, actuar con permisos, verificar y aprender
sin dejar procesos vivos, estados contradictorios o actualizaciones fragiles.

## 2. Investigacion aplicada

La etapa se basa en patrones externos usados en sistemas reales:

- Windows Job Objects: agrupar procesos que pertenecen a una misma aplicacion y
  poder cerrarlos como unidad.
- .NET Generic Host / `IHostedService`: ciclo de vida explicito, apagado
  cooperativo y eventos de cierre.
- Kubernetes Controllers: reconciliar estado deseado contra estado observado.
- OpenTelemetry: logs, metricas y trazas como una sola historia observable.
- OWASP Logging: telemetria util sin exponer tokens, contrasenas, chats
  completos ni datos sensibles.
- Windows Error Reporting LocalDumps: metadatos locales de crash sin subir dumps
  automaticamente.
- OpenAI Evals: evaluar comportamiento de IA con casos y trazas, no solo
  compilar codigo.
- NIST AI RMF: controlar riesgos de IA durante todo el ciclo de vida.
- The Update Framework: proteger actualizaciones contra rollback no autorizado
  con metadatos, hashes y versiones.

## 3. Los 7 gates de Etapa 15

### 15.1 Runtime Governor & Process Ownership

La app ya no debe confiar solo en una lista `_processes`. Cada proceso AriadGSM
debe tener:

- nombre;
- PID;
- rol;
- propiedad;
- estado vivo;
- asignacion a Windows Job Object cuando Windows lo permita;
- verificacion al apagar.

Regla de propiedad:

```text
AriadGSM own: Vision, Perception, Interaction, Orchestrator, Hands, WebPanel Node.
AriadGSM does not own: Edge, Chrome, Firefox, WhatsApp sessions, Codex, WebView externo.
```

### 15.2 Durable Execution / Checkpoints

La IA no puede depender de "me acuerdo porque el proceso sigue vivo". Debe dejar
checkpoints durables que indiquen:

- etapa;
- ciclo;
- archivos de estado relevantes;
- desde donde reanudar;
- intencion del operador.

### 15.3 Evaluation Harness

La IA se valida con pruebas de contratos y escenarios de negocio:

- cliente pide precio;
- cliente envia pago;
- cliente entra por un WhatsApp y corresponde derivarlo;
- contabilidad evidence-first;
- acciones que requieren humano.

### 15.4 Observability / Trace Grading

No basta con logs. La cabina debe producir trazas calificables:

- que vio;
- que decidio;
- que bloqueo;
- que verifico;
- que fallo.

### 15.5 Installer / Updater / Rollback

El updater debe validar:

- version;
- SHA256;
- paquete completo;
- self-test;
- active version;
- previous version;
- rollback.

### 15.6 Long-run Test

La prueba larga confirma que la IA no degrada su estado tras varios ciclos:

- no quedan procesos propios vivos al detener;
- checkpoints siguen escribiendo;
- evals siguen pasando;
- trazas siguen calificando;
- Runtime Kernel y Runtime Governor no se contradicen.

### 15.7 Release Candidate

El release candidate queda listo cuando:

- las 15 etapas estan cerradas como base;
- el paquete existe;
- el manifest apunta al paquete correcto;
- el hash coincide;
- las pruebas pasan;
- GitHub tiene la version.

## 4. Entregables tecnicos

- `desktop-agent/contracts/runtime-governor-state.schema.json`
- `desktop-agent/contracts/evaluation-release-state.schema.json`
- `desktop-agent/contracts/window-reality-state.schema.json`
- `desktop-agent/contracts/support-telemetry-state.schema.json`
- `desktop-agent/contracts/support-telemetry-event.schema.json`
- `desktop-agent/ariadgsm_agent/runtime_governor.py`
- `desktop-agent/ariadgsm_agent/window_reality.py`
- `desktop-agent/ariadgsm_agent/support_telemetry.py`
- `desktop-agent/ariadgsm_agent/release_evaluation.py`
- `desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.ProcessGovernor.cs`
- `desktop-agent/tests/window_reality_resolver.py`
- `desktop-agent/tests/support_telemetry_core.py`
- `desktop-agent/tests/evaluation_release.py`
- paquete `AriadGSMAgent-0.9.2.zip`

## 5. Definicion de terminado

Etapa 15 queda cerrada cuando:

- Runtime Governor controla procesos propios;
- Job Object queda integrado como barrera de procesos huérfanos;
- cerrar la app no deja workers propiedad de AriadGSM sin gobierno;
- existen checkpoints durables;
- hay evaluation harness local;
- hay trace grading local;
- Support & Telemetry Core genera caja negra local, traceId/correlationId,
  incidentes redactados y Support Bundle seguro;
- updater/rollback/manifest quedan verificados;
- long-run simulado pasa;
- release candidate queda empaquetado;
- Execution Lock apunta a prueba real supervisada, no a otra etapa de
  construccion.

## 6. Fuentes

- Microsoft Windows Job Objects:
  `https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects`
- Microsoft .NET Generic Host:
  `https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host`
- Microsoft `Process.Kill`:
  `https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill`
- Kubernetes Controllers:
  `https://kubernetes.io/docs/concepts/architecture/controller/`
- OpenTelemetry:
  `https://opentelemetry.io/docs/`
- OpenAI Evals:
  `https://platform.openai.com/docs/guides/evals`
- NIST AI Risk Management Framework:
  `https://www.nist.gov/itl/ai-risk-management-framework`
- The Update Framework:
  `https://theupdateframework.io/`
