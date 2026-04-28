# AriadGSM Product Shell Final Design

Version objetivo: 0.8.9
Bloque Execution Lock: Product Shell visual final
Estado: implementado como shell operativo humano

## Proposito

Product Shell es la cara diaria de AriadGSM IA Local. Su trabajo no es mostrar
motores ni rutas tecnicas; su trabajo es decirle a Bryams:

- si la IA esta apagada, lista, trabajando o bloqueada;
- si la cabina WhatsApp esta preparada;
- si memoria, contabilidad, seguridad y manos estan en condiciones de operar;
- que necesita la IA del operador para avanzar;
- que hizo recientemente, con lenguaje de negocio.

La aplicacion sigue siendo una cabina local de IA, no un dashboard tecnico.
Los detalles profundos quedan en Reporte e Historial.

## Investigacion aplicada

Fuentes revisadas:

- Microsoft Learn, Progress controls: recomienda progreso determinado cuando el
  avance es conocido y mensajes de estado cuando una tarea no debe interrumpir
  al usuario. Fuente: https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls
- Microsoft Learn, Windows app best practices: recomienda probar layout en
  distintos tamanos, mantener interacciones familiares de Windows y usar una
  experiencia visual calmada/coherente. Fuente: https://learn.microsoft.com/en-us/windows/apps/get-started/best-practices
- Microsoft Learn, Writing style: recomienda texto amistoso, conciso, sin culpar
  al usuario y con una solucion cuando algo falla. Fuente: https://learn.microsoft.com/en-us/windows/apps/design/style/writing-style
- W3C WCAG Error Identification: los errores deben explicarse en texto y dejar
  claro que elemento fallo. Fuente: https://www.w3.org/WAI/WCAG21/Understanding/error-identification
- Microsoft Research, Human-AI interaction guidelines: una IA debe aclarar que
  puede hacer, que tan bien lo hace, permitir correccion y explicar por que hizo
  algo cuando falla o duda. Fuente: https://www.microsoft.com/en-us/research/articles/guidelines-for-human-ai-interaction-eighteen-best-practices-for-human-centered-ai-design/
- Google PAIR Guidebook, Explainability + Trust: la confianza se calibra mostrando
  alcance, limites, explicaciones utiles y control humano cuando el riesgo sube.
  Fuente: https://pair.withgoogle.com/guidebook-v2/chapter/explainability-trust/

## Principios finales del shell

1. Login primero, IA apagada despues.
   Entrar a la cabina no debe iniciar ojos, memoria ni manos. El operador decide
   cuando pulsa Encender IA.

2. Alistar cabina es una accion visible.
   Cuando se preparan WhatsApps, la pantalla muestra progreso de 0 a 100 y no
   retrocede visualmente durante la misma pasada.

3. El estado principal se expresa como areas humanas.
   La tabla tecnica de motores se reemplaza por areas operativas: Cabina
   WhatsApp, Ojos y lectura, Cerebro y memoria, Contabilidad, Seguridad, Manos,
   Nube y panel.

4. Seguridad es parte visible del producto.
   Trust & Safety no queda escondido en logs. El operador ve si hay bloqueos,
   permisos pendientes o si las manos estan cedidas al humano.

5. Los errores deben ser accionables.
   La caja "Lo que necesito de ti" muestra el problema en lenguaje humano y
   remata con Reporte/Historial para soporte. No debe volcar rutas largas ni
   JSON completo.

6. La IA no se esconde si hay avisos.
   Al iniciar, la app solo se minimiza cuando no hay errores ni avisos visibles.
   Si falta algo, queda al frente para que Bryams vea que ocurre.

7. La bitacora es social, no cruda.
   "Lo que la IA hizo" resume cabina, memoria, contabilidad, seguridad, manos y
   motores encendidos. El detalle tecnico completo queda fuera de la vista normal.

## Pantalla final

### Login

- Marca AriadGSM visible.
- Usuario y contrasena.
- Recordar credenciales protegido por Windows DPAPI CurrentUser.
- Progreso de login y actualizacion.
- Mensaje claro: la IA queda apagada hasta autorizacion.

### Cabina operativa

- Encabezado con logo, version actual y resumen.
- Botones: Encender IA, Pausar IA, Leer ahora, Alistar WhatsApps, Panel web,
  Historial, Reporte.
- Panel de alistamiento solo visible cuando se alista la cabina o cuando hay un
  resultado reciente de cabina.
- Tarjetas de estado:
  - Cabina
  - Memoria
  - Contabilidad
  - Seguridad
  - Manos
- Caja "Lo que necesito de ti" para avisos humanos.
- Lista de areas de IA con estado simplificado.
- Bitacora "Lo que la IA hizo".

## Definicion de terminado

Este bloque queda completo cuando:

- existe este documento;
- la interfaz muestra version visible;
- el login conserva inicio manual;
- Alistar WhatsApps usa progreso determinado y monotono;
- el estado tecnico se agrupa en areas humanas;
- seguridad aparece en tarjetas y salud operativa;
- los errores visibles son accionables y no cajas tecnicas largas;
- la app no se minimiza si quedan errores o avisos;
- hay prueba automatizada sin mover sesiones reales;
- el paquete versionado se publica en el canal de actualizacion.
