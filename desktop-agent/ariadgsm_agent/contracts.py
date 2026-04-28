from __future__ import annotations

import json
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_DIR = ROOT / "contracts"

CONTRACT_FILES: dict[str, str] = {
    "stage_zero_readiness": "stage-zero-readiness.schema.json",
    "control_plane_state": "control-plane-state.schema.json",
    "runtime_kernel_state": "runtime-kernel-state.schema.json",
    "runtime_governor_state": "runtime-governor-state.schema.json",
    "support_telemetry_state": "support-telemetry-state.schema.json",
    "support_telemetry_event": "support-telemetry-event.schema.json",
    "domain_contracts_final_readiness": "domain-contracts-final-readiness.schema.json",
    "vision_event": "vision-event.schema.json",
    "perception_event": "perception-event.schema.json",
    "visible_message": "visible-message.schema.json",
    "reader_core_state": "reader-core-state.schema.json",
    "window_reality_state": "window-reality-state.schema.json",
    "conversation_event": "conversation-event.schema.json",
    "decision_event": "decision-event.schema.json",
    "action_event": "action-event.schema.json",
    "accounting_event": "accounting-event.schema.json",
    "learning_event": "learning-event.schema.json",
    "living_memory_state": "living-memory-state.schema.json",
    "business_brain_state": "business-brain-state.schema.json",
    "tool_registry_state": "tool-registry-state.schema.json",
    "cloud_sync_state": "cloud-sync-state.schema.json",
    "evaluation_release_state": "evaluation-release-state.schema.json",
    "autonomous_cycle_event": "autonomous-cycle-event.schema.json",
    "human_feedback_event": "human-feedback-event.schema.json",
    "safety_approval_event": "safety-approval-event.schema.json",
    "domain_event": "domain-event-envelope.schema.json",
    "case_manager_state": "case-manager-state.schema.json",
    "channel_routing_state": "channel-routing-state.schema.json",
    "accounting_core_state": "accounting-core-state.schema.json",
    "cabin_authority_state": "cabin-authority-state.schema.json",
    "input_arbiter_state": "input-arbiter-state.schema.json",
    "trust_safety_state": "trust-safety-state.schema.json",
    "hands_verification_state": "hands-verification-state.schema.json",
    "action_transaction_state": "action-transaction-state.schema.json",
    "action_transaction_event": "action-transaction-event.schema.json",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def load_schema(contract_name: str) -> dict[str, Any]:
    if contract_name not in CONTRACT_FILES:
        raise KeyError(f"Contrato desconocido: {contract_name}")
    path = CONTRACT_DIR / CONTRACT_FILES[contract_name]
    return json.loads(path.read_text(encoding="utf-8"))


def load_domain_event_registry() -> dict[str, Any]:
    path = CONTRACT_DIR / "domain-event-registry.json"
    return json.loads(path.read_text(encoding="utf-8"))


def _type_matches(value: Any, expected: str) -> bool:
    if expected == "object":
        return isinstance(value, dict)
    if expected == "array":
        return isinstance(value, list)
    if expected == "string":
        return isinstance(value, str)
    if expected == "number":
        return isinstance(value, (int, float)) and not isinstance(value, bool)
    if expected == "integer":
        return isinstance(value, int) and not isinstance(value, bool)
    if expected == "boolean":
        return isinstance(value, bool)
    if expected == "null":
        return value is None
    return True


def validate_json_schema_instance(value: Any, schema: dict[str, Any], path: str = "$") -> list[str]:
    errors: list[str] = []
    expected_type = schema.get("type")
    if isinstance(expected_type, list):
        if not any(_type_matches(value, item) for item in expected_type):
            errors.append(f"{path}: expected one of {expected_type}")
            return errors
    elif isinstance(expected_type, str) and not _type_matches(value, expected_type):
        errors.append(f"{path}: expected {expected_type}")
        return errors

    if "const" in schema and value != schema["const"]:
        errors.append(f"{path}: must be {schema['const']}")
    enum = schema.get("enum")
    if isinstance(enum, list) and value not in enum:
        errors.append(f"{path}: must be one of {enum}")

    if isinstance(value, (int, float)) and not isinstance(value, bool):
        if "minimum" in schema and value < schema["minimum"]:
            errors.append(f"{path}: must be >= {schema['minimum']}")
        if "maximum" in schema and value > schema["maximum"]:
            errors.append(f"{path}: must be <= {schema['maximum']}")

    if isinstance(value, str):
        if "minLength" in schema and len(value) < int(schema["minLength"]):
            errors.append(f"{path}: length must be >= {schema['minLength']}")
        if "maxLength" in schema and len(value) > int(schema["maxLength"]):
            errors.append(f"{path}: length must be <= {schema['maxLength']}")

    if isinstance(value, list):
        if "minItems" in schema and len(value) < int(schema["minItems"]):
            errors.append(f"{path}: item count must be >= {schema['minItems']}")
        if "maxItems" in schema and len(value) > int(schema["maxItems"]):
            errors.append(f"{path}: item count must be <= {schema['maxItems']}")
        item_schema = schema.get("items")
        if isinstance(item_schema, dict):
            for index, item in enumerate(value):
                errors.extend(validate_json_schema_instance(item, item_schema, f"{path}[{index}]"))

    if isinstance(value, dict):
        required = schema.get("required") or []
        for field in required:
            if field not in value:
                errors.append(f"{path}: missing required field: {field}")
        properties = schema.get("properties") or {}
        additional = schema.get("additionalProperties", True)
        if additional is False:
            for field in value:
                if field not in properties:
                    errors.append(f"{path}: unexpected field: {field}")
        for field, property_schema in properties.items():
            if field in value and isinstance(property_schema, dict):
                errors.extend(validate_json_schema_instance(value[field], property_schema, f"{path}.{field}"))

    return errors


def validate_contract(event: dict[str, Any], contract_name: str) -> list[str]:
    schema = load_schema(contract_name)
    if not isinstance(event, dict):
        return ["event must be an object"]
    errors = validate_json_schema_instance(event, schema)
    if contract_name == "domain_event":
        registry = load_domain_event_registry()
        event_type = event.get("eventType")
        entry = (registry.get("eventTypes") or {}).get(event_type)
        if entry is None:
            errors.append(f"$.eventType: unknown domain event type: {event_type}")
        else:
            allowed_domains = entry.get("allowedSourceDomains") or []
            if allowed_domains and event.get("sourceDomain") not in allowed_domains:
                errors.append(
                    "$.sourceDomain: "
                    f"{event.get('sourceDomain')} cannot emit {event_type}; allowed={allowed_domains}"
                )
        risk = event.get("risk") if isinstance(event.get("risk"), dict) else {}
        if risk.get("riskLevel") == "critical" and not event.get("requiresHumanReview"):
            errors.append("$.requiresHumanReview: critical events require human review")
        if event_type in {"PaymentConfirmed", "AccountingRecordConfirmed"}:
            levels = [
                evidence.get("evidenceLevel")
                for evidence in event.get("evidence", [])
                if isinstance(evidence, dict)
            ]
            if "A" not in levels:
                errors.append(f"$.evidence: {event_type} requires level A evidence")
    return errors


SAMPLE_EVENTS: dict[str, dict[str, Any]] = {
    "stage_zero_readiness": {
        "contractVersion": "0.8.1",
        "stageId": "stage_zero_product_foundation",
        "createdAt": utc_now(),
        "version": "0.8.1",
        "status": "ok",
        "summary": "Etapa 0 ok: base de producto validada.",
        "checks": [
            {
                "checkId": "source_documents",
                "name": "Documentos fuente",
                "status": "ok",
                "detail": "Documentos base presentes.",
                "evidence": ["docs/ARIADGSM_EXECUTION_LOCK.md"],
            }
        ],
        "humanReport": {
            "queQuedoListo": ["Base de producto validada."],
            "queFalta": [],
            "riesgos": ["Cerrar Etapa 1 antes de avanzar a casos."],
        },
        "nextStage": {
            "stageNumber": "0.5",
            "name": "Runtime Kernel",
            "status": "closed_runtime_kernel_final",
            "reason": "Etapa 0.5 gobierna vida de motores e incidentes antes de Cloud Sync.",
        },
    },
    "domain_contracts_final_readiness": {
        "contractVersion": "0.8.5",
        "stageId": "stage_one_domain_event_contracts",
        "createdAt": utc_now(),
        "version": "0.8.5",
        "status": "ok",
        "summary": "Etapa 1 ok: Domain Event Contracts cerrados.",
        "checks": [
            {
                "checkId": "registry",
                "name": "Registro de eventos",
                "status": "ok",
                "detail": "Registry versionado y completo.",
                "evidence": ["desktop-agent/contracts/domain-event-registry.json"],
            }
        ],
        "humanReport": {
            "queQuedoListo": ["Domain Event Contracts cerrados."],
            "queFalta": [],
            "riesgos": ["Siguiente riesgo: Case Manager debe usar estos eventos."],
        },
        "nextStage": {
            "stageNumber": 2,
            "name": "Autonomous Cycle Orchestrator",
            "status": "implemented_as_central_cycle",
            "reason": "Etapa 2 ya existe; despues corresponde Case Manager.",
        },
    },
    "control_plane_state": {
        "status": "running",
        "engine": "ariadgsm_control_plane",
        "contract": "control_plane_state",
        "architectureVersion": "final-ai-8-layers",
        "authorityLayer": "Capa 2: AI Runtime Control Plane",
        "phase": "readiness",
        "summary": "Motores encendidos bajo sesion trazable.",
        "source": "control_plane",
        "updatedAt": utc_now(),
        "version": "0.9.3",
        "runSessionId": "run-sample-20260428",
        "isRunning": True,
        "desiredRunning": True,
        "operationalStatus": "ready",
        "lastStopCause": {
            "reason": "none",
            "source": "constructor",
            "runSessionId": "run-sample-20260428",
            "at": utc_now(),
            "detail": "La IA aun no recibio orden de apagado.",
        },
        "lastCommand": {
            "commandId": "cmd-sample-start",
            "commandType": "start",
            "source": "ui.start_button_autonomous",
            "reason": "operator_authorized_autonomous_start",
            "runSessionId": "run-sample-20260428",
            "status": "running",
            "accepted": True,
            "result": "Motores encendidos bajo runSessionId.",
            "createdAt": utc_now(),
            "completedAt": utc_now(),
            "endsSession": False,
        },
        "commandLedger": [
            {
                "commandId": "cmd-sample-start",
                "commandType": "start",
                "source": "ui.start_button_autonomous",
                "reason": "operator_authorized_autonomous_start",
                "runSessionId": "run-sample-20260428",
                "status": "running",
                "accepted": True,
                "result": "Motores encendidos bajo runSessionId.",
                "createdAt": utc_now(),
                "completedAt": utc_now(),
                "endsSession": False,
            }
        ],
        "bootProtocol": {
            "protocolVersion": "ai-runtime-control-plane-v1",
            "currentPhase": "readiness",
            "phases": [
                {"phaseId": "operator_authorized", "status": "ok", "summary": "Bryams autorizo el inicio.", "updatedAt": utc_now()},
                {"phaseId": "update_check", "status": "ok", "summary": "Version al dia.", "updatedAt": utc_now()},
                {"phaseId": "workspace_bootstrap", "status": "ok", "summary": "Cabina lista.", "updatedAt": utc_now()},
                {"phaseId": "preflight", "status": "ok", "summary": "Diagnostico base aprobado.", "updatedAt": utc_now()},
                {"phaseId": "runtime_governor", "status": "ok", "summary": "Procesos propios bajo governor.", "updatedAt": utc_now()},
                {"phaseId": "workspace_guardian", "status": "ok", "summary": "Guardian activo.", "updatedAt": utc_now()},
                {"phaseId": "workers", "status": "ok", "summary": "Workers solicitados.", "updatedAt": utc_now()},
                {"phaseId": "python_core", "status": "ok", "summary": "Core mental solicitado.", "updatedAt": utc_now()},
                {"phaseId": "supervisor", "status": "ok", "summary": "Supervisor activo.", "updatedAt": utc_now()},
                {"phaseId": "readiness", "status": "ready", "summary": "Read/Think/Act separados.", "updatedAt": utc_now()},
            ],
        },
        "readiness": {
            "read": {"ready": True, "reason": "3/3 canales listos para leer."},
            "think": {"ready": True, "reason": "Memoria y cerebro pueden procesar lo leido."},
            "act": {"ready": False, "reason": "Manos esperan permiso/verificacion."},
            "sync": {"ready": True, "reason": "Cloud Sync puede subir resumen seguro."},
        },
        "owners": {
            "sessionOwner": "AI Runtime Control Plane",
            "runtimeGovernor": "Owns AriadGSM child processes only.",
            "ui": "Requests commands; does not own runtime session.",
        },
        "humanReport": {
            "headline": "IA encendida con sesion trazable",
            "queEstaPasando": ["Sesion: run-sample-20260428.", "Read/Think/Act separados."],
            "queHice": ["Unifique UI, Life Controller, Runtime Kernel, Runtime Governor, Workspace Guardian y Updater."],
            "queNecesitoDeBryams": [],
        },
    },
    "runtime_kernel_state": {
        "status": "attention",
        "engine": "ariadgsm_runtime_kernel",
        "version": "0.8.18",
        "updatedAt": utc_now(),
        "contract": "runtime_kernel_state",
        "authority": {
            "truthSource": "runtime-kernel-state.json",
            "desiredRunning": True,
            "isRunning": True,
            "canObserve": True,
            "canThink": True,
            "canAct": False,
            "canSync": False,
            "operatorHasPriority": False,
            "mainBlocker": "Vision se reinicio despues de un bloqueo de escritura.",
        },
        "summary": {
            "enginesTotal": 22,
            "enginesRunning": 20,
            "enginesDegraded": 1,
            "enginesBlocked": 0,
            "enginesDead": 0,
            "incidentsOpen": 1,
            "restartsRecent": 1,
        },
        "engines": [
            {
                "engineId": "vision",
                "name": "Vision",
                "kind": "worker",
                "lifecycle": "degraded",
                "status": "ok",
                "summary": "Vision siguio vivo tras reintento de escritura.",
            }
        ],
        "incidents": [
            {
                "incidentId": "vision-state-write-denied-sample",
                "severity": "warning",
                "source": "vision",
                "code": "state_write_denied",
                "summary": "Windows nego escritura a un estado local.",
                "detail": "Access to the path is denied.",
                "detectedAt": utc_now(),
                "recoveryAction": "retry_and_fallback_state",
                "requiresHuman": False,
            }
        ],
        "recovery": {
            "supervisorActive": True,
            "recentRestartCount": 1,
            "lastCheckpointAt": utc_now(),
            "lastRecoveryAction": "supervisor_restart",
        },
        "sourceFiles": {
            "vision": "vision-health.json",
            "agentSupervisor": "agent-supervisor-state.json",
            "cabinAuthority": "cabin-authority-state.json",
        },
        "outputFiles": {
            "state": "runtime-kernel-state.json",
            "report": "runtime-kernel-report.json",
            "diagnosticTimeline": "diagnostic-timeline.jsonl",
        },
        "humanReport": {
            "headline": "IA trabajando con incidente explicado",
            "queEstaPasando": ["Runtime Kernel unifico motores e incidentes."],
            "queHice": ["Reintente y marque recuperacion en vez de ocultar el fallo."],
            "queNecesitoDeBryams": [],
            "riesgos": ["Cloud Sync debe leer este estado antes de subir reportes."],
        },
    },
    "runtime_governor_state": {
        "status": "ok",
        "engine": "ariadgsm_runtime_governor",
        "version": "0.9.1",
        "updatedAt": utc_now(),
        "contract": "runtime_governor_state",
        "policy": {
            "windowsJobObject": True,
            "killBrowsers": False,
            "gracefulShutdownFirst": True,
            "forceKillOwnedOnly": True,
        },
        "ownedProcesses": [
            {
                "name": "Vision",
                "pid": 1200,
                "role": "worker",
                "owned": True,
                "running": True,
                "jobAssigned": True,
            }
        ],
        "summary": {
            "ownedTotal": 1,
            "runningOwned": 1,
            "orphanedOwned": 0,
            "forcedStops": 0,
            "verifiedStopped": False,
        },
        "humanReport": {
            "headline": "Runtime Governor controla los procesos AriadGSM.",
            "queEstaPasando": ["Los motores son propiedad de AriadGSM, no procesos sueltos."],
            "riesgos": [],
        },
    },
    "support_telemetry_event": {
        "eventType": "support_telemetry_event",
        "eventId": "support-sample-event",
        "createdAt": utc_now(),
        "traceId": "trace-sample-123456",
        "correlationId": "corr-sample-123456",
        "source": "runtime_kernel",
        "severity": "warning",
        "category": "runtime",
        "summary": "Runtime Kernel reporto un incidente explicable.",
        "detail": "Vision se reinicio y fue recuperado por el supervisor.",
        "redaction": {"applied": False, "redactions": 0},
        "evidence": {
            "sourceFiles": ["runtime-kernel-state.json", "windows-app.log"],
            "confidence": 0.86,
            "visibleOnly": False,
        },
        "recommendedAction": "Correlacionar con Runtime Kernel antes de actuar.",
        "privacy": {
            "cloudAllowed": True,
            "rawContentIncluded": False,
            "requiresConsent": True,
            "redactedBeforeStorage": True,
        },
    },
    "support_telemetry_state": {
        "status": "attention",
        "engine": "ariadgsm_support_telemetry_core",
        "version": "0.9.2",
        "updatedAt": utc_now(),
        "contract": "support_telemetry_state",
        "traceId": "trace-sample-123456",
        "correlationId": "corr-sample-123456",
        "policy": {
            "signals": ["logs", "metrics", "traces", "local_dumps_metadata", "runtime_state"],
            "localFirst": True,
            "redactionRequired": True,
            "supportBundleRequiresConsent": True,
            "rawScreenshotsUploaded": False,
            "fullChatsUploaded": False,
            "tokensLogged": False,
        },
        "sources": [
            {
                "sourceId": "runtime_kernel",
                "file": "runtime-kernel-state.json",
                "status": "attention",
                "category": "runtime",
            }
        ],
        "summary": {
            "sourcesRead": 8,
            "incidentsOpen": 1,
            "criticalIncidents": 0,
            "warningIncidents": 1,
            "traceEventsWritten": 1,
            "blackboxEventsRetained": 1,
            "bundleReady": True,
            "cloudSafeEventsPrepared": 1,
            "redactionsApplied": 0,
        },
        "incidents": [
            {
                "incidentId": "support-sample-event",
                "createdAt": utc_now(),
                "traceId": "trace-sample-123456",
                "correlationId": "corr-sample-123456",
                "source": "runtime_kernel",
                "severity": "warning",
                "category": "runtime",
                "summary": "Runtime Kernel reporto un incidente explicable.",
                "detail": "Vision se reinicio y fue recuperado por el supervisor.",
                "privacy": {"cloudAllowed": True, "rawContentIncluded": False, "requiresConsent": True},
            }
        ],
        "blackbox": {
            "path": "support-blackbox.jsonl",
            "retentionEvents": 500,
            "retainedEvents": 1,
            "maxBytes": 25165824,
            "localOnly": True,
        },
        "supportBundle": {
            "ready": True,
            "path": "runtime/support/support-bundle-latest.zip",
            "manifest": "runtime/support/support-bundle-manifest.json",
            "requiresConsent": True,
            "containsRawScreenshots": False,
            "containsFullChats": False,
            "containsSecrets": False,
        },
        "privacy": {
            "redactionApplied": False,
            "sensitiveUploadBlocked": True,
            "requiresExplicitUploadConsent": True,
        },
        "humanReport": {
            "headline": "Soporte local encontro incidentes explicables.",
            "queEstaPasando": ["traceId y correlationId conectan el ciclo completo."],
            "queQuedoListo": ["Caja negra local y bundle seguro listos."],
            "queNecesitoDeBryams": ["Revisar soporte si el incidente pide accion humana."],
            "riesgos": ["Los dumps de Windows no se suben sin permiso."],
        },
    },
    "cloud_sync_state": {
        "status": "ok",
        "engine": "ariadgsm_cloud_sync",
        "version": "0.8.18",
        "updatedAt": utc_now(),
        "contract": "cloud_sync_state",
        "endpoint": "https://ariadgsm.com/api/operativa-v2/cloud/sync",
        "enabled": True,
        "authenticated": True,
        "dryRun": False,
        "runtimeKernel": {
            "status": "ok",
            "canSync": True,
            "canAct": False,
            "operatorHasPriority": False,
            "mainBlocker": "",
        },
        "policy": {
            "idempotencyRequired": True,
            "retryAttemptsMax": 3,
            "rawFramesUploaded": False,
            "screenshotsUploaded": False,
            "secretsLogged": False,
            "tokenSource": "secret-file",
        },
        "summary": {
            "eventsPrepared": 12,
            "eventsSent": 9,
            "messagesPrepared": 7,
            "messagesRejected": 2,
            "conversationsSeen": 3,
            "reviewEvents": 1,
            "attempts": 1,
            "canSync": True,
        },
        "batch": {
            "id": "cloudsync-sample",
            "idempotencyKey": "cloudsync-sample",
            "payloadHash": "sample",
            "cloudAccepted": True,
            "responseStatusCode": 201,
        },
        "retry": {
            "attempts": [],
            "circuit": "closed",
            "lastError": "",
        },
        "sourceFiles": {
            "runtimeKernel": "runtime-kernel-state.json",
            "timeline": "timeline-events.jsonl",
            "domainEvents": "domain-events.jsonl",
        },
        "outputFiles": {
            "state": "cloud-sync-state.json",
            "report": "cloud-sync-report.json",
            "payload": "cloud-sync-payload.json",
            "ledger": "cloud-sync-ledger.json",
        },
        "humanReport": {
            "headline": "Nube sincronizada con ariadgsm.com.",
            "queQuedoListo": ["Subida por eventos entendidos, no por capturas."],
            "queNecesitoDeBryams": [],
            "riesgos": [],
        },
    },
    "evaluation_release_state": {
        "status": "ok",
        "engine": "ariadgsm_evaluation_release",
        "version": "0.9.1",
        "updatedAt": utc_now(),
        "contract": "evaluation_release_state",
        "stage": "15",
        "gates": [
            {
                "gateId": "15.1",
                "name": "Runtime Governor & Process Ownership",
                "status": "ok",
                "score": 1.0,
                "summary": "Procesos AriadGSM tienen ownership y apagado verificable.",
            },
            {
                "gateId": "15.2",
                "name": "Durable Execution / Checkpoints",
                "status": "ok",
                "score": 1.0,
                "summary": "Checkpoints durables listos para reanudar contexto.",
            },
            {
                "gateId": "15.3",
                "name": "Evaluation Harness",
                "status": "ok",
                "score": 1.0,
                "summary": "Evals locales validan contratos y escenarios de negocio.",
            },
            {
                "gateId": "15.4",
                "name": "Observability / Trace Grading",
                "status": "ok",
                "score": 1.0,
                "summary": "Trazas locales producen notas de salud y explicabilidad.",
            },
            {
                "gateId": "15.5",
                "name": "Installer / Updater / Rollback",
                "status": "ok",
                "score": 1.0,
                "summary": "Manifest, paquete y rollback quedan verificados.",
            },
            {
                "gateId": "15.6",
                "name": "Long-run Test",
                "status": "ok",
                "score": 1.0,
                "summary": "Prueba larga simulada no deja estados contradictorios.",
            },
            {
                "gateId": "15.7",
                "name": "Release Candidate",
                "status": "ok",
                "score": 1.0,
                "summary": "Version candidata queda empaquetada y lista para prueba real.",
            },
        ],
        "summary": {
            "gatesTotal": 7,
            "gatesOk": 7,
            "gatesAttention": 0,
            "gatesBlocked": 0,
            "releaseCandidate": True,
        },
        "artifacts": {
            "state": "evaluation-release-state.json",
            "releaseReport": "evaluation-release-report.json",
        },
        "humanReport": {
            "headline": "Etapa 15 cerrada como release candidate.",
            "queQuedoListo": ["Runtime, evals, trazas, updater y paquete quedaron verificados."],
            "queNoSePudoValidar": [],
            "siguientePaso": "Prueba real supervisada con la cabina de Bryams.",
        },
    },
    "cabin_authority_state": {
        "status": "ok",
        "engine": "ariadgsm_cabin_authority",
        "contract": "cabin_authority_state",
        "authorityVersion": "cabin-reality-authority-v2",
        "phase": "action_ready",
        "summary": "Cabin Reality: las 3 columnas estan visibles, frescas y accionables.",
        "reason": "manual_setup",
        "updatedAt": utc_now(),
        "exclusiveWindowControl": True,
        "handsMayFocus": True,
        "handsMayRecoverWindows": False,
        "handsMayArrangeWindows": False,
        "readiness": {
            "expectedChannels": 3,
            "structuralReadyChannels": 3,
            "actionReadyChannels": 3,
            "coveredChannels": 0,
            "missingChannels": 0,
        },
        "launchPolicy": {
            "mode": "explicit_browser_executable_only",
            "url": "https://web.whatsapp.com/",
            "profilePinningDefault": False,
            "shellUrlLaunchAllowed": False,
            "tabSelectionAllowedControlTypes": ["TabItem"],
        },
        "policy": [
            "Solo Cabin Authority puede acomodar o restaurar ventanas de navegador.",
            "El monitor en bucle solo observa; no minimiza ventanas del operador.",
            "Hands puede clicar solo en canales ready, visibles y sin bloqueadores.",
            "Si una ventana cubre WhatsApp, se reporta al operador en vez de cerrarla.",
        ],
        "channels": [
            {
                "channelId": "wa-1",
                "browserProcess": "msedge",
                "status": "action_ready",
                "structuralReady": True,
                "semanticFresh": True,
                "actionReady": True,
                "handsMayAct": True,
                "expectedBounds": {"left": 0, "top": 0, "width": 640, "height": 900},
                "remainingBlockers": 0,
            },
            {
                "channelId": "wa-2",
                "browserProcess": "chrome",
                "status": "action_ready",
                "structuralReady": True,
                "semanticFresh": True,
                "actionReady": True,
                "handsMayAct": True,
                "expectedBounds": {"left": 640, "top": 0, "width": 640, "height": 900},
                "remainingBlockers": 0,
            },
            {
                "channelId": "wa-3",
                "browserProcess": "firefox",
                "status": "action_ready",
                "structuralReady": True,
                "semanticFresh": True,
                "actionReady": True,
                "handsMayAct": True,
                "expectedBounds": {"left": 1280, "top": 0, "width": 640, "height": 900},
                "remainingBlockers": 0,
            },
        ],
        "blockers": [],
        "actions": [
            {
                "channelId": "wa-2",
                "type": "authority_place_whatsapp",
                "detail": "wa-2: Cabin Authority coloco chrome en columna 2.",
            }
        ],
    },
    "vision_event": {
        "eventType": "vision_event",
        "visionEventId": "vision-sample-1",
        "capturedAt": utc_now(),
        "source": "screen_capture",
        "visibleOnly": True,
        "channelHint": "wa-1",
        "frame": {
            "frameId": "frame-sample-1",
            "path": "D:/AriadGSM/vision-buffer/sample.jpg",
            "width": 1920,
            "height": 1080,
            "hash": "sample",
            "storedLocallyOnly": True,
        },
        "retention": {
            "rawFrameUploadedToCloud": False,
            "retentionHours": 1,
            "maxStorageGb": 40,
        },
    },
    "perception_event": {
        "eventType": "perception_event",
        "perceptionEventId": "perception-sample-1",
        "observedAt": utc_now(),
        "sourceVisionEventId": "vision-sample-1",
        "channelId": "wa-1",
        "objects": [
            {
                "objectType": "message_bubble",
                "confidence": 0.88,
                "text": "Cuanto vale liberar Samsung?",
                "role": "client",
            }
        ],
    },
    "visible_message": {
        "schemaVersion": "0.8.11",
        "messageId": "reader-msg-sample-1",
        "channelId": "wa-2",
        "browserProcess": "chrome",
        "conversationId": "wa-2-cliente-prueba",
        "conversationTitle": "Cliente prueba",
        "senderName": "Cliente prueba",
        "direction": "client",
        "text": "Cuanto vale liberar Samsung en Mexico?",
        "sentAt": utc_now(),
        "observedAt": utc_now(),
        "source": {
            "kind": "dom",
            "rank": 100,
            "sourceEventId": "reader-source-sample-1",
            "adapter": "chrome_cdp_dom",
            "selector": "[data-pre-plain-text]",
            "automationId": "",
        },
        "identity": {
            "isWhatsAppWeb": True,
            "identityConfidence": 0.98,
            "identitySource": "url:web.whatsapp.com",
            "url": "https://web.whatsapp.com/",
            "windowTitle": "WhatsApp - Google Chrome",
        },
        "confidence": 0.94,
        "evidence": {
            "localReference": "local://reader-core/dom/reader-source-sample-1/msg-1",
            "visibleOnly": True,
            "rawText": "Cuanto vale liberar Samsung en Mexico?",
            "bounds": {"left": 0, "top": 0, "width": 100, "height": 24},
        },
        "signals": [{"kind": "price_request", "value": "cuanto", "confidence": 0.9}],
        "sourcesCompared": [
            {"kind": "dom", "confidence": 0.94, "text": "Cuanto vale liberar Samsung en Mexico?"}
        ],
        "disagreements": [],
        "quality": {"accepted": True, "rejectionReasons": [], "learningWeight": "normal"},
    },
    "reader_core_state": {
        "status": "ok",
        "engine": "ariadgsm_reader_core",
        "version": "0.8.11",
        "updatedAt": utc_now(),
        "policy": {
            "sourcePriority": ["dom", "accessibility", "uia", "ocr"],
            "channelMap": {"msedge": "wa-1", "chrome": "wa-2", "firefox": "wa-3"},
            "ocrPolicy": "fallback_only_after_positive_whatsapp_identity",
        },
        "sourceFiles": ["desktop-agent/runtime/reader-source-events.jsonl"],
        "outputFiles": {
            "visibleMessages": "desktop-agent/runtime/reader-visible-messages.jsonl",
            "conversationEvents": "desktop-agent/runtime/conversation-events.jsonl",
            "report": "desktop-agent/runtime/reader-core-report.json",
            "db": "desktop-agent/runtime/reader-core.sqlite",
        },
        "ingested": {
            "sourceEvents": 1,
            "candidateMessages": 1,
            "acceptedCandidates": 1,
            "newMessages": 1,
            "conversationEvents": 1,
            "duplicates": 0,
            "rejected": 0,
            "invalidMessages": 0,
            "invalidConversations": 0,
            "bySource": {"dom": 1},
        },
        "summary": {
            "storedMessages": 1,
            "storedConversations": 1,
            "bySource": {"dom": 1},
            "byChannel": {"wa-2": 1},
            "latestRunMessages": 1,
            "latestRunDisagreements": 0,
            "structuredSourceMessages": 1,
            "ocrFallbackMessages": 0,
        },
        "latestMessages": [
            {
                "messageId": "reader-msg-sample-1",
                "channelId": "wa-2",
                "conversationTitle": "Cliente prueba",
                "sourceKind": "dom",
                "confidence": 0.94,
                "text": "Cuanto vale liberar Samsung en Mexico?",
            }
        ],
        "latestDisagreements": [],
        "humanReport": {
            "quePaso": "Reader Core comparo fuentes estructuradas y OCR.",
            "queLei": ["wa-2 Cliente prueba: Cuanto vale liberar Samsung en Mexico?"],
            "queRechace": [],
            "queNecesitoDeBryams": [],
            "riesgos": ["OCR es respaldo de menor confianza."],
        },
    },
    "window_reality_state": {
        "status": "attention",
        "engine": "ariadgsm_window_reality_resolver",
        "version": "0.9.1",
        "updatedAt": utc_now(),
        "contract": "window_reality_state",
        "policy": {
            "evidenceFusion": [
                "structural_windows",
                "visual_geometry",
                "semantic_reader_core",
                "freshness_ttl",
                "actionability_input_hands",
            ],
            "freshness": {
                "cabinReadinessMaxAgeMs": 45000,
                "readerCoreMaxAgeMs": 90000,
                "inputArbiterMaxAgeMs": 30000,
                "handsMaxAgeMs": 60000,
            },
            "actionability": {
                "operatorHasPriority": True,
                "doNotActOnCoveredWindow": True,
                "allowReadWhenSemanticFreshButVisualConflicted": True,
            },
        },
        "inputs": [
            {"file": "cabin-readiness.json", "freshness": {"status": "fresh", "ageMs": 200, "maxAgeMs": 45000, "fresh": True}},
            {"file": "reader-core-state.json", "freshness": {"status": "fresh", "ageMs": 300, "maxAgeMs": 90000, "fresh": True}},
            {"file": "input-arbiter-state.json", "freshness": {"status": "fresh", "ageMs": 150, "maxAgeMs": 30000, "fresh": True}},
            {"file": "hands-state.json", "freshness": {"status": "fresh", "ageMs": 250, "maxAgeMs": 60000, "fresh": True}},
        ],
        "summary": {
            "expectedChannels": 3,
            "operationalChannels": 2,
            "readyChannels": 2,
            "structuralReadyChannels": 2,
            "actionReadyChannels": 1,
            "conflictedChannels": 1,
            "requiresHumanChannels": 1,
            "staleInputs": 0,
            "handsMayActChannels": 1,
        },
        "channels": [
            {
                "channelId": "wa-1",
                "status": "READY",
                "confidence": 0.9,
                "isOperational": True,
                "structuralReady": True,
                "semanticFresh": True,
                "actionReady": True,
                "requiresHuman": False,
                "handsMayAct": True,
                "decision": {
                    "reason": "Ventana, pantalla y lectura no se contradicen.",
                    "accepted": True,
                    "actionPolicy": "read_and_act",
                },
                "signals": [
                    {"kind": "structural", "status": "ok", "confidence": 0.9, "detail": "Windows encontro el navegador asignado.", "evidence": ["msedge WhatsApp"]},
                    {"kind": "visual", "status": "ok", "confidence": 0.8, "detail": "La zona visual corresponde a WhatsApp.", "evidence": ["WhatsApp - Edge"]},
                    {"kind": "semantic", "status": "ok", "confidence": 0.88, "detail": "Reader Core vio mensajes.", "evidence": ["Hola"]},
                    {"kind": "actionability", "status": "ok", "confidence": 0.72, "detail": "Hands puede actuar con permiso.", "evidence": []},
                    {"kind": "freshness", "status": "ok", "confidence": 0.85, "detail": "La evidencia esta fresca.", "evidence": []},
                ],
                "evidence": {
                    "detail": "WhatsApp Web visible y utilizable.",
                    "rawStatus": "READY",
                    "sourceEvidence": ["msedge WhatsApp"],
                },
            }
        ],
        "humanReport": {
            "headline": "Cabina verificable",
            "queEstaPasando": ["Fusione ventana, pantalla, Reader Core, frescura e input."],
            "queAcepte": ["wa-1: READY (90%)"],
            "queDude": ["wa-2: COVERED_CONFIRMED - ventana cubierta"],
            "queNecesitoDeBryams": ["wa-2: liberar zona cubierta"],
            "riesgos": ["No permito manos si la ventana esta cubierta."],
        },
    },
    "conversation_event": {
        "eventType": "conversation_event",
        "conversationEventId": "conversation-sample-1",
        "conversationId": "wa-1-cliente-prueba",
        "channelId": "wa-1",
        "observedAt": utc_now(),
        "conversationTitle": "Cliente prueba",
        "source": "live",
        "messages": [
            {
                "messageId": "msg-sample-1",
                "text": "Cuanto vale liberar Samsung?",
                "direction": "client",
                "confidence": 0.9,
                "signals": [
                    {"kind": "price_request", "value": "cuanto", "confidence": 0.88},
                    {"kind": "service", "value": "samsung", "confidence": 0.8},
                ],
            }
        ],
        "timeline": {
            "historyLimitDays": 30,
            "complete": False,
            "dedupeStrategy": "channel_conversation_direction_time_text",
        },
    },
    "decision_event": {
        "eventType": "decision_event",
        "decisionId": "decision-sample-1",
        "createdAt": utc_now(),
        "goal": "attend_customer",
        "intent": "price_request",
        "confidence": 0.86,
        "autonomyLevel": 2,
        "proposedAction": "suggest_price_response",
        "requiresHumanConfirmation": True,
        "reasoningSummary": "Cliente pide precio y falta confirmar modelo exacto.",
        "evidence": ["msg-sample-1"],
    },
    "action_event": {
        "eventType": "action_event",
        "actionId": "action-sample-1",
        "createdAt": utc_now(),
        "actionType": "open_chat",
        "target": {"channelId": "wa-1", "conversationId": "wa-1-cliente-prueba"},
        "status": "planned",
        "verification": {"verified": False, "summary": "Pendiente de ejecutar.", "confidence": 0.0},
    },
    "hands_verification_state": {
        "contractVersion": "0.9.6",
        "status": "ok",
        "engine": "ariadgsm_hands_verification",
        "version": "0.9.6",
        "updatedAt": utc_now(),
        "executionMode": "execute",
        "policy": {
            "trustSafetyRequired": True,
            "inputArbiterRequired": True,
            "cabinAuthorityRequired": True,
            "postActionVerificationRequired": True,
            "actionTransactionsEnabled": True,
            "singlePhysicalAction": True,
            "freshPerceptionMaxAgeMs": 3500,
            "freshInteractionMaxAgeMs": 3500,
            "noDestructiveBrowserPolicy": True,
            "textDraftRequiresApproval": True,
            "sendRequiresApproval": True,
            "allowTextInput": False,
            "allowSendMessage": False,
            "physicalActions": ["focus_window", "open_chat", "scroll_history", "capture_conversation", "write_text", "send_message"],
            "historyScrollWheelSteps": 6,
        },
        "inputs": {
            "cognitiveDecisionEventsFile": "desktop-agent/runtime/cognitive-decision-events.jsonl",
            "operatingDecisionEventsFile": "desktop-agent/runtime/decision-events.jsonl",
            "businessDecisionEventsFile": "desktop-agent/runtime/business-decision-events.jsonl",
            "perceptionEventsFile": "desktop-agent/runtime/perception-events.jsonl",
            "interactionEventsFile": "desktop-agent/runtime/interaction-events.jsonl",
            "trustSafetyStateFile": "desktop-agent/runtime/trust-safety-state.json",
            "inputArbiterStateFile": "desktop-agent/runtime/input-arbiter-state.json",
            "actionEventsFile": "desktop-agent/runtime/action-events.jsonl",
            "actionTransactionStateFile": "desktop-agent/runtime/action-transaction-state.json",
            "actionJournalFile": "desktop-agent/runtime/action-journal.jsonl",
        },
        "verificationGate": {
            "verifiedBeforeContinueRequired": True,
            "unverifiedPhysicalActionsBecomeFailed": True,
            "openChatRequiresChannelTitleAndRow": True,
            "scrollRequiresVisibleChannel": True,
            "captureRequiresPerceptionConfirmation": True,
            "draftsNeverSendAutomatically": True,
            "singleActionBeforeNextRead": True,
            "freshPerceptionBeforePhysicalAction": True,
            "nonDestructiveBrowserPolicy": True,
        },
        "summary": {
            "decisionsRead": 1,
            "actionsPlanned": 2,
            "actionsWritten": 2,
            "actionsBlocked": 0,
            "actionsExecuted": 1,
            "actionsVerified": 1,
            "actionsSkipped": 0,
            "needsHuman": 0,
        },
        "lastAction": {
            "actionId": "action-sample-1",
            "summary": "open_chat: verified. Chat correcto confirmado.",
            "error": "",
        },
        "humanReport": {
            "headline": "Manos verificadas",
            "resumenDecision": "Abri el chat correcto y confirme antes de continuar.",
            "permitidas": ["1 accion verificada antes de continuar."],
            "necesitanBryams": [],
            "bloqueadas": [],
            "riesgos": [],
        },
    },
    "action_transaction_state": {
        "contractVersion": "0.9.6",
        "engine": "ariadgsm_action_transaction_gate",
        "status": "verified",
        "updatedAt": utc_now(),
        "transactionId": "action-transaction-sample-1",
        "traceId": "trace-action-sample-1",
        "channelId": "wa-1",
        "actionType": "open_chat",
        "expiresAt": utc_now(),
        "policy": {
            "singlePhysicalAction": True,
            "singleChannelLease": True,
            "freshPerceptionRequired": True,
            "postVerificationRequired": True,
            "destructiveBrowserActionsForbidden": True,
        },
        "lastActionId": "action-sample-1",
        "lastActionStatus": "verified",
        "summary": "Accion verificada con percepcion fresca y lease de canal.",
        "humanReport": {
            "headline": "Accion segura verificada",
            "resumenDecision": "La IA toco un solo chat despues de confirmar canal, fila y lectura fresca.",
            "permitidas": ["open_chat verificado en wa-1"],
            "necesitanBryams": [],
            "bloqueadas": [],
            "riesgos": [],
        },
    },
    "action_transaction_event": {
        "eventType": "action_transaction_event",
        "createdAt": utc_now(),
        "phase": "complete",
        "status": "verified",
        "transactionId": "action-transaction-sample-1",
        "traceId": "trace-action-sample-1",
        "actionId": "action-sample-1",
        "actionType": "open_chat",
        "channelId": "wa-1",
        "conversationTitle": "Cliente prueba",
        "sourceDecisionId": "decision-sample-1",
        "summary": "Action Transaction Gate completo y verificado.",
        "verification": {"verified": True, "summary": "Chat correcto confirmado.", "confidence": 0.92},
    },
    "accounting_event": {
        "eventType": "accounting_event",
        "accountingId": "accounting-sample-1",
        "createdAt": utc_now(),
        "status": "draft",
        "confidence": 0.72,
        "clientName": "Cliente prueba",
        "conversationId": "wa-1-cliente-prueba",
        "kind": "price_quote",
        "amount": 20,
        "currency": "USD",
        "evidence": ["msg-sample-1"],
    },
    "learning_event": {
        "eventType": "learning_event",
        "learningId": "learning-sample-1",
        "createdAt": utc_now(),
        "learningType": "slang",
        "source": "conversation",
        "summary": "La frase 'cuanto vale' indica pregunta de precio.",
        "confidence": 0.9,
        "appliesTo": ["price_request"],
    },
    "living_memory_state": {
        "status": "ok",
        "engine": "ariadgsm_memory_core",
        "capability": "ariadgsm_living_memory",
        "version": "0.8.12",
        "updatedAt": utc_now(),
        "contract": "living_memory_state",
        "policy": {
            "memoryLayers": ["episodic", "semantic", "procedural", "accounting", "style", "correction"],
            "truthStatuses": ["fact", "hypothesis", "uncertain", "procedure", "correction", "deprecated", "conflict"],
            "confidenceRules": {
                "fact": ">=0.75 y sin revision requerida",
                "hypothesis": "0.45-0.74 o evidencia parcial",
                "uncertain": "<0.45 o requiere revision",
                "deprecated": "conocimiento corregido o degradado por Bryams",
            },
            "evidenceFirst": True,
            "unsafeKnowledgeDegrades": True,
        },
        "sourceFiles": {
            "conversationEvents": "desktop-agent/runtime/timeline-events.jsonl",
            "learningEvents": "desktop-agent/runtime/learning-events.jsonl",
            "accountingEvents": "desktop-agent/runtime/accounting-events.jsonl",
            "domainEvents": "desktop-agent/runtime/domain-events.jsonl",
            "humanFeedbackEvents": "desktop-agent/runtime/human-feedback-events.jsonl",
        },
        "ingested": {"events": 1, "livingItems": 3, "uncertain": 1, "corrections": 1},
        "summary": {"livingMemoryItems": 3, "memoryCorrections": 1, "memoryMessages": 1},
        "livingMemory": {
            "totalItems": 3,
            "byLayer": {
                "episodic": 1,
                "semantic": 1,
                "procedural": 0,
                "accounting": 0,
                "style": 0,
                "correction": 1,
            },
            "byStatus": {"fact": 1, "hypothesis": 1, "correction": 1},
            "confidence": {"high": 2, "medium": 1, "low": 0},
            "uncertainties": 1,
            "degraded": 0,
            "corrections": 1,
        },
        "latestLearned": [
            {
                "memoryId": "mem-sample-1",
                "type": "semantic",
                "status": "fact",
                "summary": "La frase 'cuanto vale' indica pregunta de precio.",
                "confidence": 0.9,
                "sourceKey": "learning-sample-1",
                "updatedAt": utc_now(),
            }
        ],
        "uncertainties": [
            {
                "memoryId": "mem-sample-2",
                "type": "accounting",
                "status": "hypothesis",
                "summary": "Pago pendiente de comprobante.",
                "confidence": 0.62,
                "sourceKey": "accounting-sample-1",
            }
        ],
        "corrections": [
            {
                "correctionId": "human-feedback-sample-1",
                "targetEventId": "accounting-sample-1",
                "summary": "El pago aun no esta confirmado.",
                "correction": "Mantener como borrador hasta ver comprobante.",
                "actorId": "bryams",
                "createdAt": utc_now(),
            }
        ],
        "humanReport": {
            "headline": "Estoy convirtiendo lecturas en memoria viva",
            "queAprendi": ["semantic: 'cuanto vale' indica pregunta de precio."],
            "queDudo": ["accounting: pago pendiente de comprobante."],
            "queCorrigioBryams": ["El pago aun no esta confirmado -> Mantener como borrador."],
            "queNecesito": ["Confirmar memorias marcadas como duda antes de contabilidad."],
        },
    },
    "business_brain_state": {
        "status": "attention",
        "engine": "ariadgsm_business_brain",
        "version": "0.8.13",
        "updatedAt": utc_now(),
        "contract": "business_brain_state",
        "policy": {
            "objectives": [
                "protect_revenue",
                "answer_customer_fast",
                "preserve_accounting_evidence",
                "route_to_best_channel",
                "learn_business_patterns",
                "avoid_unsafe_autonomy",
            ],
            "decisionMode": "recommend_only_no_physical_action",
            "usesLivingMemory": True,
            "requiresTrustSafetyBeforeAction": True,
            "customerFacingDraftsRequireHuman": True,
        },
        "sourceFiles": {
            "caseManagerDb": "desktop-agent/runtime/case-manager.sqlite",
            "memoryDb": "desktop-agent/runtime/memory-core.sqlite",
            "routeDb": "desktop-agent/runtime/channel-routing.sqlite",
            "accountingDb": "desktop-agent/runtime/accounting-core.sqlite",
        },
        "outputFiles": {
            "decisionEvents": "desktop-agent/runtime/business-decision-events.jsonl",
            "recommendations": "desktop-agent/runtime/business-recommendations.jsonl",
            "db": "desktop-agent/runtime/business-brain.sqlite",
        },
        "ingested": {
            "casesRead": 1,
            "memoryItemsRead": 4,
            "routeDecisionsRead": 0,
            "accountingRecordsRead": 1,
            "recommendations": 1,
            "emittedDecisionEvents": 1,
        },
        "summary": {
            "activeCases": 1,
            "recommendations": 1,
            "requiresHuman": 1,
            "emittedDecisionEvents": 1,
            "memoryItemsRead": 4,
        },
        "mentalModel": {
            "activeCases": 1,
            "customers": [{"customerId": "customer-sample", "cases": 1}],
            "services": [{"service": "samsung", "cases": 1}],
            "markets": [{"country": "MX", "cases": 1}],
            "memoryItemsConsulted": 4,
            "uncertainties": [],
            "topPriorities": [
                {
                    "caseId": "case-sample-1",
                    "title": "Cliente prueba",
                    "intent": "quote_or_price",
                    "priority": "medium",
                    "requiresHumanConfirmation": True,
                }
            ],
        },
        "recommendations": [
            {
                "recommendationId": "business-sample-1",
                "caseId": "case-sample-1",
                "channelId": "wa-2",
                "conversationId": "wa-2-cliente-prueba",
                "customerId": "customer-sample",
                "title": "Cliente prueba",
                "intent": "quote_or_price",
                "priority": "medium",
                "riskLevel": "medium",
                "confidence": 0.82,
                "proposedAction": "prepare_quote_draft",
                "requiresHumanConfirmation": True,
                "rationale": "Intento=quote_or_price; accion=prepare_quote_draft; memorias_consultadas=4; faltantes=ninguno",
                "missingInformation": [],
                "replyDraft": "Tengo el contexto base para preparar una cotizacion; falta que Bryams confirme precio final antes de responder.",
                "evidence": ["case-sample-1", "mem-sample-1"],
            }
        ],
        "emittedDecisionEvents": [
            {
                "eventType": "decision_event",
                "decisionId": "business-decision-sample-1",
                "createdAt": utc_now(),
                "goal": "business_reasoning",
                "intent": "quote_or_price",
                "confidence": 0.82,
                "autonomyLevel": 3,
                "proposedAction": "prepare_quote_draft",
                "requiresHumanConfirmation": True,
                "reasoningSummary": "Business Brain preparo una propuesta, sin enviarla.",
                "evidence": ["case-sample-1", "mem-sample-1"],
            }
        ],
        "humanReport": {
            "headline": "Ya puedo razonar sobre casos de AriadGSM sin mover las manos",
            "queEntendi": ["Casos activos: 1.", "Memorias consultadas: 4."],
            "quePropongo": ["Cliente prueba: prepare_quote_draft."],
            "queDudo": [],
            "queNecesitoDeBryams": ["Confirmar precio final antes de responder."],
        },
    },
    "autonomous_cycle_event": {
        "eventType": "autonomous_cycle_event",
        "cycleId": "cycle-sample-1",
        "createdAt": utc_now(),
        "status": "ok",
        "phase": "observing",
        "summary": "Ciclo autonomo listo: ojos, memoria, supervisor y manos reportan estado.",
        "trigger": "checkpoint",
        "steps": [
            {
                "stepId": "observe",
                "name": "Observar",
                "status": "ok",
                "objective": "Saber que canales estan visibles.",
                "detail": "WhatsApp visible en los canales esperados.",
                "inputs": ["vision-health.json"],
                "outputs": ["canales observados"],
                "metrics": {"channelsReady": 3},
            },
            {
                "stepId": "understand",
                "name": "Entender",
                "status": "ok",
                "objective": "Unir mensajes en timeline.",
                "detail": "Mensajes unidos.",
                "inputs": ["timeline-state.json"],
                "outputs": ["historias"],
            },
            {
                "stepId": "plan",
                "name": "Planear",
                "status": "ok",
                "objective": "Elegir siguiente accion.",
                "detail": "Decision lista.",
                "inputs": ["cognitive-state.json"],
                "outputs": ["decision"],
            },
            {
                "stepId": "request_permission",
                "name": "Pedir permiso",
                "status": "ok",
                "objective": "Autorizar antes de actuar.",
                "detail": "Gate allow.",
                "inputs": ["supervisor-state.json"],
                "outputs": ["permissionGate"],
            },
            {
                "stepId": "act",
                "name": "Actuar",
                "status": "ok",
                "objective": "Ejecutar acciones autorizadas.",
                "detail": "Sin accion pendiente.",
                "inputs": ["hands-state.json"],
                "outputs": ["accion"],
            },
            {
                "stepId": "verify",
                "name": "Verificar",
                "status": "ok",
                "objective": "Confirmar resultado.",
                "detail": "Nada fisico que verificar.",
                "inputs": ["hands-state.json"],
                "outputs": ["verificacion"],
            },
            {
                "stepId": "learn",
                "name": "Aprender",
                "status": "ok",
                "objective": "Guardar memoria util.",
                "detail": "Memoria actualizada.",
                "inputs": ["memory-state.json"],
                "outputs": ["aprendizaje"],
            },
            {
                "stepId": "report",
                "name": "Reportar",
                "status": "ok",
                "objective": "Explicar a Bryams.",
                "detail": "Reporte generado.",
                "inputs": ["autonomous-cycle-state.json"],
                "outputs": ["autonomous-cycle-report.json"],
            },
        ],
        "permissionGate": {
            "decision": "ALLOW",
            "reason": "Permisos suficientes para continuar.",
            "canHandsRun": True,
        },
        "stages": [
            {
                "stageId": "eyes",
                "name": "Ojos",
                "status": "ok",
                "detail": "WhatsApp visible en los canales esperados.",
                "metrics": {"channelsReady": 3},
            }
        ],
        "directives": {"gateDecision": "ALLOW", "allowedEngines": {"hands": True}},
        "humanReport": {"headline": "Estoy trabajando contigo", "summary": "Ciclo listo."},
    },
    "human_feedback_event": {
        "eventType": "human_feedback_event",
        "feedbackId": "human-feedback-sample-1",
        "createdAt": utc_now(),
        "feedbackKind": "correction",
        "targetEventId": "domain-sample-1",
        "targetEventType": "PaymentDrafted",
        "channelId": "wa-2",
        "conversationId": "wa-2-cliente",
        "caseId": "case-sample-1",
        "customerId": "customer-sample-1",
        "summary": "El pago aun no esta confirmado.",
        "correction": "Mantener como borrador hasta ver comprobante.",
        "confidence": 1.0,
        "requiresFollowUp": True,
        "actor": {"type": "human", "id": "bryams"},
    },
    "safety_approval_event": {
        "eventType": "safety_approval_event",
        "approvalId": "approval-sample-1",
        "createdAt": utc_now(),
        "expiresAt": utc_now(),
        "targetSourceId": "business-decision-sample-1",
        "decision": "APPROVE",
        "approvedBy": "bryams",
        "scope": "single_decision",
        "reason": "Bryams aprobo este borrador especifico.",
        "constraints": {"maxUses": 1, "channelId": "wa-2", "actionKey": "text_draft"},
    },
    "domain_event": {
        "eventId": "evt-sample-1",
        "eventType": "ConversationObserved",
        "schemaVersion": "0.8.2",
        "createdAt": utc_now(),
        "sourceDomain": "TimelineEngine",
        "sourceSystem": "ariadgsm-local-agent",
        "actor": {"type": "system", "id": "ariadgsm-timeline-engine"},
        "subject": {"type": "conversation", "id": "wa-1-cliente-prueba"},
        "correlationId": "case-wa-1-cliente-prueba",
        "causationId": "conversation-sample-1",
        "idempotencyKey": "ConversationObserved:wa-1-cliente-prueba:conversation-sample-1",
        "traceId": "trace-sample-1",
        "channelId": "wa-1",
        "conversationId": "wa-1-cliente-prueba",
        "caseId": "case-wa-1-cliente-prueba",
        "customerId": "customer_pending",
        "confidence": 0.9,
        "evidence": [
            {
                "evidenceId": "ev-sample-1",
                "source": "conversation_event",
                "evidenceLevel": "B",
                "observedAt": utc_now(),
                "summary": "Timeline conversation sample.",
                "rawReference": "local://conversation-sample-1",
                "confidence": 0.9,
                "redactionState": "safe_summary",
                "limitations": [],
            }
        ],
        "privacy": {
            "classification": "internal",
            "cloudAllowed": True,
            "redactionRequired": False,
            "retentionPolicy": "case_lifetime",
            "contains": [],
            "reason": "Business conversation summary only.",
        },
        "risk": {
            "riskLevel": "low",
            "riskReasons": [],
            "autonomyLevel": 1,
            "allowedActions": ["reason"],
            "blockedActions": [],
        },
        "requiresHumanReview": False,
        "data": {"messageCount": 1},
    },
    "tool_registry_state": {
        "status": "attention",
        "engine": "ariadgsm_tool_registry",
        "version": "0.8.16",
        "updatedAt": utc_now(),
        "contract": "tool_registry_state",
        "policy": {
            "selectionMode": "capability_first_with_fallbacks",
            "executionMode": "plan_only_no_direct_execution",
            "trustSafetyRequired": True,
            "handsVerificationRequired": True,
            "leastPrivilege": True,
            "approvalRequiredForRisk": ["high", "critical"],
            "noSecretsInCatalog": True,
        },
        "sourceFiles": {
            "catalogFile": "desktop-agent/runtime/tool-registry-catalog.json",
            "businessRecommendationsFile": "desktop-agent/runtime/business-recommendations.jsonl",
            "businessDecisionEventsFile": "desktop-agent/runtime/business-decision-events.jsonl",
            "domainEventsFile": "desktop-agent/runtime/domain-events.jsonl",
        },
        "outputFiles": {
            "stateFile": "desktop-agent/runtime/tool-registry-state.json",
            "reportFile": "desktop-agent/runtime/tool-registry-report.json",
            "decisionEventsFile": "desktop-agent/runtime/business-decision-events.jsonl",
        },
        "summary": {
            "toolsRegistered": 2,
            "capabilitiesRegistered": 4,
            "readyTools": 1,
            "degradedTools": 1,
            "blockedTools": 0,
            "requestsRead": 1,
            "matchedRequests": 1,
            "unmatchedRequests": 0,
            "plansReady": 0,
            "plansNeedHuman": 1,
            "emittedDecisionEvents": 1,
        },
        "tools": [
            {
                "toolId": "usb-remote-session",
                "name": "Remote USB session coordinator",
                "status": "manual_required",
                "riskLevel": "high",
                "capabilities": ["remote_usb", "device_forwarding"],
                "requiresHumanApproval": True,
                "verifiers": ["device_seen", "operator_confirmation"],
                "valid": True,
                "validationErrors": [],
            },
            {
                "toolId": "manual-operator-review",
                "name": "Bryams manual review",
                "status": "ready",
                "riskLevel": "low",
                "capabilities": ["human_override", "risk_review"],
                "requiresHumanApproval": False,
                "verifiers": ["human_feedback_event"],
                "valid": True,
                "validationErrors": [],
            },
        ],
        "capabilities": [
            {
                "capability": "remote_usb",
                "tools": ["usb-remote-session"],
                "bestStatus": "manual_required",
                "highestRisk": "high",
                "requiresHumanApproval": True,
            }
        ],
        "requests": [
            {
                "requestId": "tool-request-sample-1",
                "source": "business_recommendation",
                "sourceId": "business-sample-1",
                "capabilities": ["remote_usb"],
                "primaryCapability": "remote_usb",
                "caseId": "case-sample-1",
                "summary": "Cliente necesita revisar conexion USB remota.",
            }
        ],
        "toolPlans": [
            {
                "planId": "tool-plan-sample-1",
                "requestId": "tool-request-sample-1",
                "status": "needs_human",
                "capability": "remote_usb",
                "selectedToolId": "usb-remote-session",
                "selectedToolName": "Remote USB session coordinator",
                "fallbackToolIds": ["manual-operator-review"],
                "riskLevel": "high",
                "requiresHumanApproval": True,
                "handsActionType": "prepare_tool_plan",
                "verificationRequired": True,
                "reason": "Remote USB session coordinator cubre remote_usb con aprobacion humana.",
            }
        ],
        "emittedDecisionEvents": [
            {
                "eventType": "decision_event",
                "decisionId": "tool-registry-sample-1",
                "createdAt": utc_now(),
                "goal": "select_authorized_tool_by_capability",
                "intent": "external_tool_plan",
                "confidence": 0.8,
                "autonomyLevel": 6,
                "proposedAction": "prepare_tool_plan",
                "requiresHumanConfirmation": True,
                "reasoningSummary": "Plan de herramienta externa requiere aprobacion.",
                "evidence": ["tool-plan-sample-1"],
            }
        ],
        "handsIntegration": {
            "decisionEventContract": "decision_event",
            "handsConsumesDecisionEvents": True,
            "externalExecutionAllowedByRegistry": False,
            "verificationRequiredBeforeDone": True,
            "writesToDecisionEvents": True,
        },
        "humanReport": {
            "headline": "Tool Registry selecciono herramienta por capacidad",
            "queQuedoListo": ["Inventario por capacidad listo."],
            "quePuedeHacer": ["remote_usb -> Remote USB session coordinator (needs_human)"],
            "queNecesitaBryams": ["Aprobar o completar datos antes de usar herramientas GSM."],
            "riesgos": ["El registro no ejecuta herramientas por si solo."],
        },
    },
    "case_manager_state": {
        "status": "ok",
        "engine": "ariadgsm_case_manager",
        "version": "0.8.3",
        "updatedAt": utc_now(),
        "domainEventsFile": "desktop-agent/runtime/domain-events.jsonl",
        "caseEventsFile": "desktop-agent/runtime/case-events.jsonl",
        "db": "desktop-agent/runtime/case-manager.sqlite",
        "ingested": {
            "events": 3,
            "duplicates": 0,
            "skipped": 1,
            "casesCreated": 1,
            "casesUpdated": 1,
            "caseEvents": 2,
        },
        "summary": {
            "processedEvents": 3,
            "eventsRead": 3,
            "cases": 1,
            "openCases": 1,
            "needsHuman": 1,
            "ignoredCases": 0,
            "linkedEvents": 2,
            "emittedCaseEvents": 2,
        },
        "humanReport": {
            "quePaso": "Case Manager agrupo eventos de dominio en 1 caso operativo.",
            "casosAbiertos": [{"caseId": "case-sample-1", "title": "Cliente prueba", "status": "needs_quote"}],
            "necesitanBryams": [{"caseId": "case-sample-1", "reason": "Precio o pago requiere validacion humana."}],
            "proximasAcciones": [{"caseId": "case-sample-1", "action": "Revisar evidencia y responder."}],
            "riesgos": ["No confirmar pagos sin evidencia fuerte."],
        },
    },
    "channel_routing_state": {
        "status": "attention",
        "engine": "ariadgsm_channel_routing_brain",
        "version": "0.8.4",
        "updatedAt": utc_now(),
        "caseManagerDb": "desktop-agent/runtime/case-manager.sqlite",
        "routeEventsFile": "desktop-agent/runtime/route-events.jsonl",
        "db": "desktop-agent/runtime/channel-routing.sqlite",
        "policy": {
            "version": "ariadgsm-channel-policy-0.8.4",
            "channels": {
                "wa-1": {"role": "general_intake"},
                "wa-2": {"role": "sales_accounting"},
                "wa-3": {"role": "technical_services"},
            },
        },
        "ingested": {
            "cases": 2,
            "duplicates": 0,
            "skipped": 0,
            "decisions": 2,
            "routeEvents": 2,
        },
        "summary": {
            "casesRead": 2,
            "routeDecisions": 2,
            "proposedRoutes": 1,
            "approvedRoutes": 1,
            "rejectedRoutes": 0,
            "needsHuman": 1,
            "duplicateGroups": 1,
            "crossChannelCandidates": 1,
            "currentChannelOk": 1,
            "emittedRouteEvents": 2,
        },
        "humanReport": {
            "quePaso": "Channel Routing analizo 2 casos y propuso 1 ruta entre WhatsApps.",
            "rutasPropuestas": [{"caseId": "case-sample-1", "targetChannelId": "wa-3"}],
            "rutasAprobadas": [{"caseId": "case-sample-2", "targetChannelId": "wa-2"}],
            "necesitanBryams": [{"caseId": "case-sample-1", "reason": "Cruza canales."}],
            "riesgos": ["No mover contexto entre WhatsApps sin confirmacion humana."],
        },
    },
    "accounting_core_state": {
        "status": "attention",
        "engine": "ariadgsm_accounting_core_evidence_first",
        "version": "0.8.5",
        "updatedAt": utc_now(),
        "domainEventsFile": "desktop-agent/runtime/domain-events.jsonl",
        "caseManagerDb": "desktop-agent/runtime/case-manager.sqlite",
        "accountingEventsFile": "desktop-agent/runtime/accounting-core-events.jsonl",
        "db": "desktop-agent/runtime/accounting-core.sqlite",
        "evidencePolicy": {
            "version": "ariadgsm-accounting-evidence-policy-0.8.5",
            "levels": ["A", "B", "C", "D", "E", "F"],
            "confirmationRequires": ["evidence_level_A"],
        },
        "ingested": {
            "domainEvents": 4,
            "accountingEvents": 2,
            "duplicates": 0,
            "invalid": 0,
            "records": 2,
            "emittedEvents": 2,
        },
        "summary": {
            "domainEventsRead": 4,
            "accountingRecords": 2,
            "drafts": 1,
            "needsEvidence": 0,
            "needsHuman": 1,
            "confirmedRecords": 1,
            "payments": 1,
            "debts": 1,
            "refunds": 0,
            "evidenceAttached": 2,
            "emittedAccountingEvents": 2,
            "ambiguousRecords": 0,
        },
        "humanReport": {
            "quePaso": "Accounting Core proceso eventos contables y separo borradores de confirmados.",
            "pendientes": [{"record_id": "acct-sample-1", "status": "draft"}],
            "confirmados": [{"record_id": "acct-sample-2", "status": "confirmed"}],
            "necesitanBryams": [{"record_id": "acct-sample-1", "reason": "falta evidencia A"}],
            "riesgos": ["No confirmar pagos sin evidencia A."],
        },
    },
    "input_arbiter_state": {
        "contractVersion": "0.8.14",
        "status": "attention",
        "engine": "ariadgsm_input_arbiter",
        "version": "0.8.14",
        "phase": "operator_control",
        "decision": "PAUSE_FOR_OPERATOR",
        "activeOwner": "operator",
        "updatedAt": utc_now(),
        "leaseId": "operator-control",
        "blockedActionId": "action-sample-1",
        "actionType": "open_chat",
        "channelId": "wa-1",
        "conversationTitle": "Cliente prueba",
        "operatorIdleMs": 120,
        "requiredIdleMs": 1200,
        "operatorHasPriority": True,
        "handsPausedOnly": True,
        "eyesContinue": True,
        "memoryContinue": True,
        "cognitiveContinue": True,
        "businessBrainContinue": True,
        "lease": {
            "leaseId": "operator-control",
            "granted": False,
            "requiresInput": True,
            "issuedAt": utc_now(),
            "expiresAt": utc_now(),
            "ttlMs": 0,
            "actionId": "action-sample-1",
            "actionType": "open_chat",
            "reason": "Bryams esta usando mouse o teclado.",
        },
        "operator": {
            "hasPriority": True,
            "idleMs": 120,
            "requiredIdleMs": 1200,
            "cooldownUntil": utc_now(),
            "cooldownMs": 1600,
        },
        "continuation": {
            "hands": False,
            "eyes": True,
            "memory": True,
            "cognitive": True,
            "businessBrain": True,
        },
        "summary": "Pauso manos; ojos, memoria y cerebro siguen.",
    },
    "trust_safety_state": {
        "status": "blocked",
        "engine": "ariadgsm_trust_safety_core",
        "version": "0.8.14",
        "updatedAt": utc_now(),
        "policy": {
            "version": "ariadgsm-trust-safety-0.8.14",
            "autonomyLevel": 3,
            "decisions": ["ALLOW", "ALLOW_WITH_LIMIT", "ASK_HUMAN", "PAUSE_FOR_OPERATOR", "BLOCK"],
            "principles": [
                "least_privilege",
                "human_approval_for_irreversible_actions",
                "verify_before_continue",
                "audit_every_permission_decision",
                "operator_has_mouse_priority",
                "critical_actions_need_per_action_approval",
                "high_risk_actions_need_evidence",
            ],
        },
        "contracts": {
            "decisionSources": ["cognitive", "operating", "business_brain"],
            "inputArbiterState": "input-arbiter-state.schema.json",
            "approvalEvent": "safety-approval-event.schema.json",
            "permissionState": "trust-safety-state.schema.json",
        },
        "permissions": {
            "allowLocalNavigation": True,
            "allowTextDraft": False,
            "allowMessageSend": False,
            "allowExternalToolExecution": False,
            "allowAccountingDraft": True,
            "allowAccountingConfirmation": False,
            "allowCrossChannelTransfer": False,
        },
        "riskMatrix": {
            "message_send": {
                "requiredAutonomyLevel": 6,
                "riskLevel": "critical",
                "reversible": False,
                "permission": "allowMessageSend",
            }
        },
        "approvalLedger": {
            "approvalsRead": 0,
            "approvalsApplied": 0,
            "ttlSeconds": 900,
            "applied": [],
        },
        "inputArbiter": {
            "status": "attention",
            "phase": "operator_control",
            "decision": "PAUSE_FOR_OPERATOR",
            "activeOwner": "operator",
            "operatorHasPriority": True,
            "handsPausedOnly": True,
            "operatorIdleMs": 120,
            "requiredIdleMs": 1200,
            "leaseId": "operator-control",
            "summary": "Bryams esta usando mouse o teclado.",
        },
        "permissionGate": {
            "decision": "BLOCK",
            "reason": "Accion irreversible sin permiso explicito.",
            "canHandsRun": False,
            "allowedEngines": {
                "vision": True,
                "perception": True,
                "memory": True,
                "cognitive": True,
                "businessBrain": True,
                "hands": False,
            },
        },
        "summary": {
            "decisionsRead": 1,
            "actionsRead": 1,
            "domainEventsRead": 1,
            "approvalsRead": 0,
            "approvalsApplied": 0,
            "findings": 3,
            "allowed": 1,
            "allowedWithLimit": 0,
            "paused": 1,
            "blocked": 1,
            "requiresHumanConfirmation": 1,
            "critical": 1,
            "safeNextActions": 1,
            "irreversibleBlocked": 1,
            "evidenceMissing": 0,
        },
        "latestFindings": [
            {
                "sourceId": "sample-send",
                "sourceType": "decision_event",
                "actionKey": "message_send",
                "decision": "BLOCK",
                "riskLevel": "critical",
                "allowed": False,
                "requiresHumanConfirmation": True,
                "evidenceCount": 1,
                "evidenceLevels": [],
                "approvalId": "",
                "humanSummary": "Bloquee enviar mensaje al cliente: permiso explicito faltante.",
            }
        ],
        "safeNextActions": [],
        "limitedActions": [],
        "pausedActions": [],
        "requiresHumanActions": [],
        "blockedActions": [
            {
                "sourceId": "sample-send",
                "decision": "BLOCK",
                "blockedActions": ["send_message"],
            }
        ],
        "humanReport": {
            "headline": "Bloquee una accion riesgosa",
            "resumenDecision": "Hay una accion irreversible, sin permiso explicito o sin evidencia suficiente.",
            "permitidas": [],
            "necesitanBryams": ["Aprobar o corregir accion riesgosa."],
            "bloqueadas": ["Bloquee enviar mensaje al cliente."],
            "riesgos": ["Permiso explicito faltante."],
        },
    },
}


def sample_event(contract_name: str) -> dict[str, Any]:
    if contract_name not in SAMPLE_EVENTS:
        raise KeyError(f"Contrato desconocido: {contract_name}")
    return deepcopy(SAMPLE_EVENTS[contract_name])
