# AriadGSM Cabin Authority final

Estado: cerrado para version `0.8.6`.

Nota 2026-04-28: este documento queda como antecedente. La version vigente es
`docs/ARIADGSM_CABIN_REALITY_AUTHORITY_FINAL.md`, que separa ventana visible,
lectura fresca y permiso real de manos.

Este bloque existe para resolver de raiz el fallo mas repetido de la cabina:
cuando Bryams pulsa `Alistar WhatsApps` o `Encender IA`, el sistema debe
preparar Edge, Chrome y Firefox como WhatsApp 1/2/3 sin cerrar pestañas,
sin abrir la app instalada de WhatsApp y sin pelear con la mano del operador.

## Investigacion usada

Fuentes base revisadas:

- Microsoft UI Automation: permite leer e interactuar con elementos de UI para
  accesibilidad y pruebas automatizadas. Esto justifica usar el arbol accesible,
  pero tambien obliga a seleccionar controles por tipo real, no por texto suelto.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/winauto/entry-uiauto-win32
- Win32 `SetWindowPos`: cambia posicion, tamano y orden Z de ventanas top-level.
  Debe usarse solo desde la autoridad de ventanas, no desde todos los motores.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
- Win32 `ShowWindow`: restaura o muestra ventanas por `HWND`; no debe usarse
  como reemplazo de cerrar/minimizar ventanas ajenas.
  Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow
- .NET `ProcessStartInfo`: permite lanzar un ejecutable concreto con argumentos.
  Esto evita abrir `https://web.whatsapp.com/` por shell y que Windows redirija
  a una app instalada o a un manejador equivocado.
  Fuente: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo
- Firefox command line docs: `-new-window URL` es el camino correcto para abrir
  una URL en una ventana nueva de Firefox.
  Fuente: https://firefox-source-docs.mozilla.org/browser/CommandLineParameters.html

## Fallos raiz encontrados

1. El localizador de sesiones buscaba cualquier nodo accesible con texto
   `WhatsApp`. Eso incluia botones, listas y controles custom. En navegadores,
   un boton de cerrar pestana puede tener nombre relacionado a WhatsApp; invocar
   ese boton explica los cierres vistos en Edge/Chrome.

2. El lanzamiento de Edge/Chrome tenia perfiles heredados `Profile 1` y
   `Profile 2` como configuracion por defecto. Eso puede abrir un perfil
   equivocado, provocar errores de perfil o no reutilizar la sesion real de
   Bryams.

3. La propia ventana `AriadGSM IA Local`, al mostrar progreso, podia quedar por
   encima del WhatsApp central y el diagnostico la marcaba como bloqueo. Eso
   hacia que la cabina pareciera degradada aunque el bloqueo fuera el propio
   panel de control.

4. El estado de autoridad no tenia contrato validado. Sin contrato, futuros
   cambios podian volver a mezclar manos, cabina, lanzador y monitor.

## Contrato definitivo

`Cabin Authority` es el unico modulo autorizado a:

- restaurar ventanas de Edge/Chrome/Firefox;
- acomodar las 3 columnas;
- traer al frente una pestaña de WhatsApp real;
- abrir `web.whatsapp.com` en el navegador asignado.

`Cabin Authority` no puede:

- cerrar Edge, Chrome, Firefox ni pestañas;
- minimizar ventanas del operador;
- usar botones accesibles como si fueran pestañas;
- abrir `web.whatsapp.com` por shell;
- fijar perfiles de navegador por defecto.

Estado publicado:

```text
desktop-agent/runtime/cabin-authority-state.json
```

Contrato validado:

```text
desktop-agent/contracts/cabin-authority-state.schema.json
```

## Flujo final de alistamiento

1. `Inventario`: leer ventanas visibles y procesos por navegador asignado.
2. `Rescate seguro`: si existe una pestaña WhatsApp, seleccionar solo
   `ControlType.TabItem`; nunca botones.
3. `Apertura controlada`: si falta canal, lanzar el ejecutable exacto del
   navegador asignado con `UseShellExecute=false`.
4. `Sin perfil fijo`: no usar `--profile-directory` salvo que una variable
   explicita lo active para soporte avanzado.
5. `Acomodo`: usar `SetWindowPos`/`ShowWindow` solo dentro de Cabin Authority.
6. `Verificacion`: publicar canales ready/covered/missing y blockers humanos.
7. `Gate para manos`: Hands solo actua si el canal esta ready y sin bloqueadores.

## Mapa operativo AriadGSM

- `wa-1`: Microsoft Edge.
- `wa-2`: Google Chrome.
- `wa-3`: Mozilla Firefox.

Este mapa ya no depende de la posicion inicial en pantalla. La posicion es una
consecuencia del alistamiento, no la identidad del canal.

## Garantias agregadas

- El localizador de pestañas solo acepta `ControlType.TabItem`.
- Se rechazan nombres que parezcan cerrar pestañas: `cerrar`, `close`,
  `cerrar pestana`, `close tab`.
- `LaunchWhatsAppWindow` usa ejecutable explicito y `UseShellExecute=false`.
- `ARIADGSM_ENABLE_BROWSER_PROFILE_PINNING` debe estar en `1/true/yes` para
  volver a pasar `--profile-directory`.
- `StopExternalWorkerProcesses` sigue limitado a workers AriadGSM; no incluye
  Edge, Chrome ni Firefox.
- `cabin-authority-state.json` declara `shellUrlLaunchAllowed=false`.
- `AriadGSM IA Local` se ignora como bloqueo durante el alistamiento visible,
  porque el panel se minimiza antes de encender motores.

## Pruebas de no regresion

Nueva prueba:

```text
desktop-agent/tests/cabin_authority_final.py
```

Comprueba:

- contrato `cabin_authority_state`;
- no uso de `UseShellExecute=true` en el lanzador de WhatsApp;
- sin perfiles `Profile 1/Profile 2` en config base;
- sin `Button/ListItem/Custom` en seleccion de pestañas;
- sin Edge/Chrome/Firefox en la lista de procesos que se pueden matar;
- monitor de bucle sin permiso de acomodar ventanas;
- politica de manos sin derecho a recuperar/arreglar ventanas.

## Definicion de terminado

Este bloque queda cerrado cuando:

- hay contrato de Cabin Authority;
- el alistamiento es Edge/Chrome/Firefox determinista;
- se abren canales faltantes solo por navegador asignado;
- no se cierran ventanas ni pestañas del usuario;
- el localizador no puede invocar botones por accidente;
- el panel informa ready/degraded/attention antes de Encender IA;
- el build y las pruebas pasan;
- la version y el updater quedan publicados.
