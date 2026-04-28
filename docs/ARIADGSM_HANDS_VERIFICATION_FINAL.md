# AriadGSM Hands & Verification Final

Version: 0.8.15
Estado: cerrada como base final de Etapa 12
Fecha: 2026-04-28

## Objetivo

Hands & Verification deja de ser "mover mouse y esperar que haya salido bien".
Desde esta etapa, las manos de AriadGSM trabajan bajo un contrato verificable:

1. reciben decisiones del cerebro;
2. aceptan solo lo permitido por Trust & Safety;
3. piden lease al Input Arbiter antes de tocar mouse o teclado;
4. obedecen Cabin Authority para no cerrar ni reorganizar ventanas por su cuenta;
5. ejecutan la accion fisica minima;
6. verifican con Perception antes de continuar;
7. escriben un evento auditable y un estado humano.

Esto no convierte a AriadGSM en un bot de clicks. Convierte las manos en una
capacidad fisica supervisada por cerebro, seguridad, ojos y memoria.

## Fuentes externas usadas

- Microsoft `SendInput`: la entrada sintetica se inserta en el flujo de mouse y
  teclado, puede fallar por UIPI y debe tratarse como accion no garantizada.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput
- Microsoft `GetLastInputInfo`: sirve para detectar actividad humana reciente
  en la sesion actual, con cautela sobre tiempos no monotonicamente perfectos.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlastinputinfo
- Microsoft UI Automation Events: los eventos de foco, estructura, texto y
  cambios de propiedades ayudan a verificar que la UI realmente cambio.
  Fuente: https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-overview
- Microsoft UI Automation Control Patterns: Invoke, Scroll, Text, Value y Window
  son patrones para manipular o leer capacidades de controles sin depender solo
  de pixeles.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-controlpatternsoverview

## Contratos cerrados

### Entrada

Hands lee tres fuentes de decisiones:

- `CognitiveDecisionEventsFile`
- `OperatingDecisionEventsFile`
- `BusinessDecisionEventsFile`

Esto cierra una brecha anterior: Business Brain podia proponer accion, pero
Hands no siempre consumia esa cola directamente.

### Permiso

Antes de ejecutar en modo real, Hands exige:

- `trust-safety-state.json` fresco;
- `permissionGate.canHandsRun = true`;
- aprobacion especifica para borradores o envios cuando aplique;
- `input-arbiter-state.json` con lease de manos, salvo modo plan;
- Cabin Authority vigente para ventanas WhatsApp.

### Salida

Hands sigue emitiendo:

- `action_event`

Y ahora tambien publica:

- `hands-verification-state.schema.json`
- runtime: `desktop-agent/runtime/hands-verification-state.json`

Ese estado esta pensado para la interfaz: no obliga a Bryams a leer logs crudos.
Dice que hizo, que verifico, que bloqueo y por que necesita ayuda.

## Reglas de accion verificable

### Abrir chat

`open_chat` solo puede avanzar si:

- Interaction confirmo una fila visible de chat;
- hay coordenadas positivas `clickX` y `clickY`;
- Cabin Authority confirma que el canal pertenece al navegador correcto;
- despues del click, Perception confirma canal y titulo esperado.

Si Perception ve otro chat, la accion queda `failed` y se suspenden acciones
dependientes para ese canal.

### Scroll historico

`scroll_history` tiene limite por ciclo:

- `HistoryScrollWheelSteps`

La accion se considera valida solo si, despues del scroll, Perception confirma
que el canal WhatsApp sigue visible. Si el canal desaparece, se falla. Esto evita
que la IA siga leyendo o aprendiendo sobre una ventana equivocada.

### Captura de conversacion

`capture_conversation` no asume que por enfocar una ventana ya leyo bien.
Necesita confirmacion de Perception por conversacion o por canal visible. Si no
hay confirmacion, queda como no verificada.

### Texto y envio

`write_text` queda bloqueado por defecto.

Aunque se habilite `AllowTextInput`, necesita aprobacion de Trust & Safety por
accion o decision. `send_message` queda aun mas restringido: no se envia sin
permiso explicito y verificacion posterior.

Esta etapa prepara borradores seguros, no envio autonomo final. El envio real se
habilitara solo cuando Trust & Safety, Tool Registry y Evaluation + Release lo
validen.

## Estado humano

`hands-verification-state.json` contiene:

- version de contrato;
- modo `plan` o `execute`;
- politica activa;
- fuentes de decisiones;
- gate de verificacion;
- resumen de acciones;
- ultima accion;
- reporte humano.

El objetivo es que la app diga cosas tipo:

- "Manos verificadas";
- "Manos esperando permiso o verificacion";
- "No confirme el chat correcto";
- "No envio nada porque falta aprobacion".

## Que queda fuera de esta etapa

No se cierra Tool Registry todavia.

Eso significa que AriadGSM aun no opera herramientas GSM como USB Redirector,
programas de flasheo o paneles externos por capacidad abstracta. La etapa 12
solo deja las manos listas y verificables para que la etapa 13 pueda registrar
herramientas sin parches.

## Definicion de terminado

- Hands consume Cognitive, Operating y Business Brain decisions.
- Trust & Safety gobierna ejecucion fisica.
- Input Arbiter conserva prioridad humana.
- Cabin Authority conserva propiedad de ventanas WhatsApp.
- Acciones fisicas ejecutadas sin verificacion quedan `failed`.
- Abrir chat confirma fila, canal y titulo.
- Scroll historico tiene limite.
- Borradores y envios requieren aprobacion.
- Se publica `hands-verification-state.json`.
- Hay pruebas C# y Python para el contrato.

## Siguiente etapa

Etapa 13: Tool Registry.

Ahora que manos verifican antes de continuar, el siguiente bloque puede registrar
herramientas por capacidad: liberar, flashear, consultar servidor, revisar panel,
usar USB Redirector o elegir alternativa cuando una herramienta falle.
