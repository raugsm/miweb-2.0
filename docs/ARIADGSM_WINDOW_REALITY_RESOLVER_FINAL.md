# AriadGSM Window Reality Resolver Final

Version: 0.9.1  
Estado: cerrado como capa transversal obligatoria para cabina, ojos, seguridad, manos y release.

## Objetivo

Window Reality Resolver resuelve una falla de raiz: antes el sistema podia creer que un WhatsApp estaba listo usando una sola senal, por ejemplo titulo de ventana, z-order o estado viejo. Eso producia errores como cerrar o tapar navegadores, creer que Chrome/Edge estaban listos cuando otra aplicacion cubria la zona, o habilitar manos sin saber si la IA podia leer.

Desde 0.9.1 ningun canal queda listo por una sola senal. El resolver fusiona:

1. Evidencia estructural de Windows: proceso, titulo, hwnd, rectangulo, z-order y canal asignado.
2. Evidencia visual/geometrica: ventana existente, zona cubierta, ventana encima y rectangulo visible.
3. Evidencia semantica: Reader Core vio mensajes reales del canal.
4. Frescura: cada archivo de estado debe estar dentro de TTL; estado viejo no cuenta como verdad.
5. Capacidad de accion: Input Arbiter y Hands deciden si se puede tocar mouse/teclado.

## Fuentes Externas Usadas

- [Microsoft UI Automation](https://learn.microsoft.com/en-us/windows/win32/winauto/ui-automation-specification): base para leer arboles UI, elementos, propiedades, patrones y eventos de aplicaciones de escritorio.
- [AutomationElement.IsOffscreenProperty](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.automationelement.isoffscreenproperty): confirma que una propiedad UIA puede decir si un elemento esta fuera de pantalla, pero no basta para saber si es operable.
- [DWM window attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute): base para separar identidad/atributos de ventana de lo que se ve realmente en escritorio.
- [SetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow): Windows restringe traer ventanas al frente; por eso la IA no debe asumir que pedir foco equivale a lograr foco.
- [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook): WinEvents permiten escuchar cambios de ventana, pero requieren loop y cuidado con reentrancia.
- [Windows Graphics Capture](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/screen-capture): confirma que la vision visual real debe tratarse como fuente aparte de UIA/Win32.
- [DPI awareness](https://learn.microsoft.com/en-us/windows/win32/hidpi/declaring-managed-apps-dpi-aware): la geometria de pantalla debe considerar DPI, porque coordenadas logicas y fisicas pueden diferir.
- [Playwright actionability](https://playwright.dev/docs/actionability): se adopta el patron de no actuar hasta que un objetivo sea visible, estable, habilitado y accionable.
- [Patrones RPA robustos de UiPath](https://docs.uipath.com/studio/standalone/latest/user-guide/ui-automation): se adopta la idea de selectores/fuentes robustas y diagnostico, sin depender de OCR o clicks ciegos.

## Contrato

Archivo:

- `desktop-agent/contracts/window-reality-state.schema.json`

Estado runtime:

- `desktop-agent/runtime/window-reality-state.json`

Campos clave:

- `status`: `ok`, `attention`, `blocked` o `idle`.
- `policy.evidenceFusion`: lista las cinco familias de evidencia.
- `inputs`: registra frescura de `cabin-readiness.json`, `reader-core-state.json`, `input-arbiter-state.json` y `hands-state.json`.
- `channels`: decision por `wa-1`, `wa-2`, `wa-3`.
- `summary`: operables, listos, conflictos, humanos requeridos, estados viejos y canales donde manos podrian actuar.
- `humanReport`: explicacion clara para Bryams.

## Decision Por Canal

Estados principales:

- `READY`: ventana, pantalla, semantica y accionabilidad no se contradicen.
- `READY_PENDING_SEMANTIC`: Windows y geometria se ven bien, pero Reader Core aun no confirmo mensajes.
- `READY_WITH_CONFLICT`: Reader Core lee mensajes, pero la geometria dice que hay conflicto. Se permite leer, no mover manos.
- `READY_OPERATOR_BUSY`: el canal es leible, pero Bryams esta usando mouse/teclado. Manos se pausan.
- `COVERED_CONFIRMED`: hay ventana WhatsApp, pero esta cubierta. No se actua.
- `HUMAN_REQUIRED`: WhatsApp pide QR, "Usar aqui", perfil roto o duplicados.
- `STALE_STATE`: la evidencia esta vieja. No se toma como listo.
- `MISSING_OR_WRONG_SESSION`: no hay consenso de que el canal este visible.

## Integracion Con Etapas

### Etapa 7: Cabin Authority

Cabin Authority sigue siendo dueno de alistar Edge/Chrome/Firefox, pero ahora publica una proyeccion inmediata de Window Reality. Esa proyeccion no habilita manos por si sola; solo dice "Windows cree esto". Luego el resolver Python reemplaza esa proyeccion con consenso real.

### Etapa 8: Safe Eyes / Reader Core

Reader Core ya no trabaja aislado. Sus mensajes visibles se vuelven senal semantica del resolver. Si Reader Core ve mensajes de `wa-2`, pero Windows dice que `wa-2` esta cubierto, el estado queda como conflicto leible, no accionable.

### Etapa 11: Trust & Safety + Input Arbiter

El permiso de manos queda condicionado por Window Reality. Si no hay canal operable con evidencia fresca, el ciclo autonomo bloquea manos aunque otros motores digan "ok".

### Etapa 12: Hands & Verification

Hands solo debe operar cuando el canal tiene `handsMayAct=true`. Si el operador mueve mouse o la ventana esta cubierta, el resolver mantiene lectura o pausa manos segun corresponda.

### Etapa 15: Evaluation + Release

Evaluation + Release ahora valida que exista el contrato, sample, documento final y que el release incluya Window Reality como parte de los gates.

## Politica Contra Parches

Esta capa evita seguir agregando reglas sueltas tipo "si se cierra Edge, haz X". La decision pasa a ser:

- Que canal es?
- Que navegador/proceso lo respalda?
- Que ventana y zona ocupan?
- Esta fresco el estado?
- Reader Core confirmo mensajes?
- Puede actuar sin pelear con Bryams?

Si alguna respuesta falta o contradice otra, el sistema no inventa certeza: reporta conflicto y decide leer, esperar, pedir ayuda o bloquear manos.

## Limites

0.9.1 deja la fusion y contrato cerrados. Lo que aun queda para prueba real es medir contra pantalla viva con tres WhatsApps y confirmar que:

- Edge no se cierra.
- Chrome no se abre duplicado.
- Firefox conserva su sesion.
- Los estados pasan de proyeccion estructural a consenso real al iniciar motores.
- Hands no actua en zonas cubiertas o viejas.
