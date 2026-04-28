# AriadGSM Tool Registry Final

Version: 0.8.16  
Estado: Etapa 13 cerrada como base final  
Fecha: 2026-04-28

## 1. Proposito

Tool Registry es la capa que le dice al Business Brain:

```text
Para esta capacidad de negocio, estas son las herramientas posibles,
estos son sus riesgos, estos son los datos que necesitan, estas son sus
salidas esperadas, estos son sus verificadores y estas son sus alternativas.
```

No es un bot de herramientas. No ejecuta programas GSM, no guarda claves y no
abre sesiones privadas. Su trabajo es convertir una necesidad como
`remote_usb`, `price_lookup`, `driver_install` o `accounting_record` en un plan
auditable que pase por Trust & Safety y Hands & Verification.

## 2. Investigacion usada

La estructura queda alineada con fuentes externas:

- OpenAI Function Calling recomienda herramientas definidas con esquemas y modo
  estricto para que las llamadas se ajusten a contratos, no a texto libre:
  <https://platform.openai.com/docs/guides/function-calling>.
- Model Context Protocol define herramientas como primitivas con nombre,
  descripcion, esquema de entrada y resultado estructurado:
  <https://modelcontextprotocol.io/specification/2025-06-18/server/tools>.
- JSON Schema existe para describir estructura, tipos y restricciones de datos,
  justo lo que necesitamos para que cada herramienta sea validable:
  <https://json-schema.org/learn/getting-started-step-by-step>.
- OWASP Top 10 for LLM Applications marca el riesgo de `Excessive Agency`:
  dar autonomia sin control puede afectar confiabilidad, privacidad y confianza:
  <https://owasp.org/www-project-top-10-for-large-language-model-applications/>.
- NIST AI RMF recomienda incorporar gestion de riesgos en el diseno,
  desarrollo, uso y evaluacion de sistemas de IA:
  <https://www.nist.gov/itl/ai-risk-management-framework>.

Conclusion aplicada: AriadGSM no debe tener una IA con acceso libre a
programas. Debe tener capacidades registradas, permisos por riesgo, evidencia,
verificacion y alternativas.

## 3. Contrato de herramienta

Cada herramienta registrada debe declarar:

```json
{
  "toolId": "usb-remote-session",
  "name": "Remote USB session coordinator",
  "category": "device_connectivity",
  "status": "manual_required",
  "riskLevel": "high",
  "capabilities": ["remote_usb", "device_forwarding"],
  "inputsNeeded": ["customer consent", "remote session id", "device model"],
  "outputsProduced": ["connection status", "device visible status"],
  "verifiers": ["device_seen", "session_alive", "operator_confirmation"],
  "failureSignals": ["usb_dropped", "driver_missing"],
  "alternatives": ["driver-package-manager", "manual-operator-review"],
  "requiresHumanApproval": true,
  "executionOwner": "Bryams"
}
```

Campos prohibidos:

- contrasenas;
- tokens;
- cookies;
- claves de API;
- datos privados de cliente que no sean necesarios para seleccionar capacidad.

## 4. Regla principal

La IA no elige por nombre de programa. Elige por capacidad.

Ejemplo correcto:

```text
Necesito remote_usb para este caso.
Tool Registry encuentra usb-remote-session.
Si falla, prueba driver-package-manager o pide revision manual.
```

Ejemplo incorrecto:

```text
Abre siempre el programa X y haz estos clicks.
```

Ese segundo camino es el que genero parches. Tool Registry existe para que el
Business Brain razone por capacidad, no por botones especificos.

## 5. Flujo operativo

```text
Business Brain
  -> solicita capacidad
Tool Registry
  -> selecciona herramienta y alternativas
  -> produce plan de herramienta
Trust & Safety
  -> evalua riesgo, permisos, evidencia y aprobacion
Hands & Verification
  -> solo puede actuar si el gate lo permite
  -> verifica resultado antes de continuar
Living Memory
  -> guarda exito, fallo, alternativa y correccion humana
```

Tool Registry escribe planes como `decision_event` de tipo
`external_tool_plan`. Esto permite que Trust & Safety los bloquee o pida
confirmacion sin que Hands ejecute una herramienta externa directamente.

## 6. Inventario inicial

Capacidades base:

- `read_visible_chat`: leer WhatsApp visible con Reader Core.
- `capture_conversation`: capturar conversacion visible.
- `service_order`: preparar orden de servicio autorizada.
- `price_lookup`: buscar referencia de precio.
- `authorized_device_service`: razonar sobre servicio tecnico autorizado.
- `remote_usb`: coordinar conexion USB remota con permiso.
- `driver_install`: preparar soporte de controladores con permiso.
- `accounting_record`: crear registros contables con evidencia.
- `payment_evidence`: asociar comprobantes.
- `human_override`: derivar a Bryams cuando el riesgo o incertidumbre lo exige.

Herramientas base:

- `browser-whatsapp-reader`
- `gsm-service-panel`
- `usb-remote-session`
- `driver-package-manager`
- `pricing-reference-sheet`
- `accounting-ledger-local`
- `manual-operator-review`

El catalogo editable vive en:

```text
desktop-agent/runtime/tool-registry-catalog.json
```

Si no existe, el motor crea uno base. Un ejemplo versionado vive en:

```text
desktop-agent/tool-registry/catalog.example.json
```

## 7. Integracion con Hands

Tool Registry no mueve mouse. Su integracion con Hands es indirecta y segura:

1. Lee recomendaciones del Business Brain.
2. Detecta si una recomendacion necesita una capacidad de herramienta.
3. Crea un plan de herramienta.
4. Emite un `decision_event` con `intent=external_tool_plan`.
5. Trust & Safety lo clasifica como herramienta externa.
6. Hands solo recibe algo accionable si existe permiso, objetivo verificable y
   confirmacion.

En esta etapa, la ejecucion externa queda apagada por diseno:

```text
externalExecutionAllowedByRegistry = false
```

## 8. Integracion con memoria

Cada plan de herramienta debe alimentar Living Memory con:

- herramienta sugerida;
- capacidad solicitada;
- razon;
- entrada faltante;
- fallo observado;
- alternativa usada;
- verificador que confirmo o nego el resultado;
- correccion de Bryams.

Esto permite automejora operativa sin reprogramar cada caso: si una herramienta
falla mucho para cierta marca, pais o servicio, la memoria puede degradarla y
preferir otra alternativa.

## 9. Definicion de terminado

Etapa 13 queda cerrada cuando:

- existe contrato `tool_registry_state`;
- existe motor `ariadgsm_agent.tool_registry`;
- existe catalogo editable por capacidades;
- el motor valida herramientas, riesgos, entradas, salidas y verificadores;
- crea planes con alternativas;
- integra sus planes con `decision_event` para Trust & Safety y Hands;
- no ejecuta herramientas reales;
- no guarda secretos;
- tiene pruebas sin acciones reales peligrosas;
- queda versionado y empaquetado.

## 10. Siguiente etapa

Siguiente bloque bloqueado por Execution Lock:

```text
Etapa 14: Cloud Sync / ariadgsm.com
```

Esa etapa debe unificar panel, nube, respaldo, reportes y sincronizacion sin
subir evidencia sensible bruta por defecto.
