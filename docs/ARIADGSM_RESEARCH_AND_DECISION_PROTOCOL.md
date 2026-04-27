# AriadGSM Research and Decision Protocol

Documento obligatorio para evitar parches y decisiones improvisadas durante el desarrollo de AriadGSM IA.

Fecha: 2026-04-27
Estado: protocolo de trabajo
Aplica a: arquitectura, IA, vision, manos, memoria, contabilidad, actualizador, cabina, nube e interfaz.

## 1. Motivo

AriadGSM no puede avanzar con decisiones tomadas solo por intuicion o por arreglos rapidos.

Antes de crear o cambiar una pieza importante, se debe separar:

```text
Datos reales de AriadGSM
Investigacion tecnica externa
Opciones posibles
Decision recomendada
Riesgos
Prueba de aceptacion
Implementacion
```

Esto evita repetir el ciclo de parchear un fallo, romper otro modulo y volver a empezar.

## 2. Regla principal

Ningun bloque grande se considera listo para programar si no responde estas preguntas:

1. Que problema real de AriadGSM resuelve?
2. Que evidencia local lo demuestra?
3. Que dice la investigacion tecnica seria?
4. Que opciones existen?
5. Por que se escoge una opcion y no otra?
6. Que puede fallar?
7. Como sabremos que quedo bien?
8. Como se corrige sin cambiar codigo cada vez?

## 3. Fuentes permitidas

Para cada decision importante se priorizan fuentes primarias:

- Documentacion oficial de OpenAI para agentes, herramientas, memoria y evaluaciones.
- Documentacion oficial de Microsoft para Windows, UI Automation, captura, servicios y seguridad.
- Documentacion oficial del framework o tecnologia usada.
- Estado real del runtime de AriadGSM.
- Logs propios de la aplicacion.
- Conversaciones y correcciones confirmadas por Bryams.

Fuentes secundarias como blogs, videos o foros solo se usan como apoyo, no como base principal.

## 4. Formato obligatorio de investigacion

Cada bloque debe tener una ficha de decision.

```text
Nombre del bloque:
Problema:
Evidencia AriadGSM:
Investigacion externa:
Opciones:
Decision:
Por que:
Riesgos:
Prueba de aceptacion:
Que no se hara:
```

Ejemplo:

```text
Nombre del bloque: Managed Cabin Host
Problema: la app cerro Edge/Chrome al preparar WhatsApp.
Evidencia AriadGSM: logs de cierre y perdida de ventana.
Investigacion externa: Windows UI Automation, window handles, process ownership.
Opciones: adoptar ventanas existentes, crear perfiles propios, extension local.
Decision: adoptar primero, crear solo si falta, nunca cerrar navegadores externos.
Prueba: wa-1 Edge, wa-2 Chrome, wa-3 Firefox quedan visibles sin cerrar ventanas.
```

## 5. Separacion entre negocio y tecnologia

Toda decision debe distinguir dos capas:

```text
Negocio = lo que AriadGSM necesita hacer.
Tecnologia = como lo vamos a ejecutar.
```

Ejemplo:

```text
Negocio: identificar pago, deuda, cliente, servicio y caso.
Tecnologia: OCR, accesibilidad, DOM, memoria, clasificador y base de datos.
```

Si la tecnologia cambia, el negocio no debe romperse.

## 6. Criterios para elegir soluciones

Una solucion se prefiere cuando:

1. Reduce parches futuros.
2. Tiene una autoridad clara.
3. Es observable y auditable.
4. Se puede probar.
5. Permite corregir con datos o configuracion, no solo codigo.
6. Respeta seguridad y permisos.
7. Mejora la autonomia real del negocio.
8. No depende de una sola fuente fragil.

## 7. Escalera de evidencia para la IA

Para que la IA crea algo, debe clasificar la fuente:

```text
Nivel A = dato estructurado confirmado.
Nivel B = lectura de accesibilidad/DOM.
Nivel C = OCR con alta confianza.
Nivel D = OCR dudoso.
Nivel E = inferencia.
Nivel F = dato rechazado o ruido.
```

Contabilidad, pagos, respuestas al cliente y acciones tecnicas no deben basarse solo en Nivel D o E.

## 8. Investigacion base ya considerada

Estas fuentes sostienen el enfoque actual y deben ampliarse por bloque:

- OpenAI describe agentes como sistemas que realizan tareas en nombre del usuario con modelo, herramientas e instrucciones, y recomienda herramientas bien definidas, guardrails y evaluaciones.
- Microsoft UI Automation expone elementos, patrones, propiedades y eventos de interfaces de Windows, por eso es mejor base que depender solo de OCR.
- Windows Graphics Capture permite capturar ventanas o pantallas como fuente visual cuando no hay datos estructurados.
- Observabilidad con logs, metricas y trazas es necesaria para diagnosticar sistemas distribuidos por motores.

Referencias iniciales:

- https://openai.com/business/guides-and-resources/a-practical-guide-to-building-ai-agents/
- https://platform.openai.com/docs/guides/agents
- https://platform.openai.com/docs/guides/agent-evals
- https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview
- https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture
- https://opentelemetry.io/docs/

## 9. Como se aplicara desde ahora

Antes de programar bloques grandes:

1. Leer datos reales del runtime.
2. Revisar documentacion tecnica confiable.
3. Crear ficha de decision.
4. Confirmar contigo cuando el impacto sea de negocio.
5. Programar.
6. Probar.
7. Decir que quedo al 100% y que no.

## 10. Bloques que requieren investigacion formal

Los proximos bloques no deben programarse sin ficha:

1. Managed Cabin Host.
2. Reader Core definitivo.
3. Business Brain.
4. Case Manager.
5. Pricing Brain.
6. Accounting Core.
7. Market Intelligence.
8. Conversation Brain.
9. Tool Registry / Process Brain.
10. Self-Improvement Loop.
11. Updater estable.
12. Interfaz humana de AriadGSM.

## 11. Conclusion

Este protocolo convierte el desarrollo en una secuencia seria:

```text
Investigar -> Disenar -> Validar -> Codificar -> Probar -> Aprender.
```

La meta no es avanzar rapido con parches. La meta es construir AriadGSM IA como una operacion inteligente, mantenible y cada vez mas autonoma.

