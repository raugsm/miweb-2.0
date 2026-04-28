# AriadGSM Accounting Core Evidence-First Design

Fecha: 2026-04-28
Version objetivo: 0.8.5
Estado: implementacion completa del bloque `Accounting Core evidence-first`

## 1. Proposito

Accounting Core evidence-first convierte eventos del negocio en registros
contables trazables. No confirma pagos por intuicion ni por una frase suelta.

La regla del bloque es:

```text
Borrador rapido, confirmacion lenta y con evidencia.
```

## 2. Fuentes externas usadas

- IRS Publication 583: recomienda registrar transacciones cuando ocurren,
  identificar la fuente de recibos y reconciliar cuentas.
  https://www.irs.gov/publications/p583
- Microsoft Event Sourcing: un append-only event store ayuda a reconstruir
  estado, auditar historia y crear vistas materiales sin perder el evento
  original.
  https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing
- CloudEvents: los eventos deben tener contexto estable como tipo, fuente,
  sujeto e identificador para filtrado y trazabilidad.
  https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md
- OWASP Logging Cheat Sheet y OWASP Top 10 A09: las transacciones de alto
  valor necesitan una pista de auditoria verificable, con integridad y contexto.
  https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html
  https://owasp.org/Top10/2021/A09_2021-Security_Logging_and_Monitoring_Failures/
- AICPA AU-C 500: la auditoria distingue evidencia suficiente y apropiada;
  el sistema debe documentar por que un registro se acepta o queda pendiente.
  https://www.aicpa-cima.com/resources/download/aicpa-statements-on-auditing-standards-currently-effective

## 3. Decision para AriadGSM

El motor contable no reemplaza al contador ni a Bryams. Actua como asistente
local que ordena evidencia del negocio:

- crea borradores cuando detecta pago, deuda o reembolso;
- une cada registro a `caseId`, `customerId`, canal y conversacion;
- adjunta evidencia con nivel A-F;
- marca lo ambiguo como pendiente;
- confirma solo si existe evidencia fuerte o evento humano suficiente;
- emite eventos de dominio para que Case Manager, Memory y Supervisor vean la
  misma verdad.

## 4. Niveles de evidencia

```text
A = comprobante/banco/confirmacion fuerte.
B = comprobante visible o evento estructurado confiable.
C = texto del cliente con monto/metodo.
D = senal parcial sin monto o sin cliente.
E = inferencia debil.
F = ruido o inconsistente.
```

La IA puede guardar A-F, pero para confirmar necesita A. Si no hay A, deja el
registro como borrador o pendiente de Bryams.

## 5. Flujo

```text
domain-events.jsonl
  -> Case Manager SQLite
  -> Accounting Core evidence-first
  -> accounting-core-state.json
  -> accounting-core-report.json
  -> accounting-core.sqlite
  -> accounting-core-events.jsonl
  -> Domain Events
  -> Memory / Supervisor / Autonomous Cycle
```

## 6. Estados contables

### draft

Hay senal contable, pero todavia no hay evidencia fuerte.

### needs_evidence

Falta monto, moneda, cliente, caso o evidencia suficiente.

### needs_human

La evidencia o la ruta del caso es sensible y requiere Bryams.

### evidence_attached

Hay evidencia suficiente para auditar, pero no para confirmar caja.

### confirmed

Solo se alcanza con evidencia A o confirmacion humana fuerte ya registrada.

## 7. Eventos que maneja

Entrada:

- `PaymentDrafted`
- `PaymentEvidenceAttached`
- `PaymentConfirmed`
- `DebtDetected`
- `DebtUpdated`
- `RefundCandidate`
- `AccountingEvidenceAttached`
- `AccountingRecordConfirmed`
- `AccountingCorrectionReceived`

Salida:

- `AccountingEvidenceAttached`
- `AccountingRecordConfirmed`
- `HumanApprovalRequired` cuando falta Bryams.

## 8. Guardrails

- No enviar mensajes.
- No mover mouse.
- No cerrar deudas.
- No confirmar pagos sin evidencia A.
- No mezclar clientes sin `caseId` o `customerId`.
- No borrar eventos contables: se corrige con eventos posteriores.

## 9. Definicion de cerrado 0.8.5

Este bloque queda cerrado si:

- existe `ariadgsm_agent.accounting_evidence`;
- existe contrato `accounting-core-state`;
- existe prueba `accounting_core_evidence.py`;
- lee `domain-events.jsonl`;
- consulta Case Manager cuando necesita contexto de caso;
- separa borrador, evidencia, pendiente y confirmado;
- emite `AccountingEvidenceAttached`;
- emite `AccountingRecordConfirmed` solo con evidencia A;
- deja reporte humano de pendientes;
- se integra antes de Memory;
- version y paquete quedan publicados.
