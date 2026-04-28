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
    "domain_contracts_final_readiness": "domain-contracts-final-readiness.schema.json",
    "vision_event": "vision-event.schema.json",
    "perception_event": "perception-event.schema.json",
    "visible_message": "visible-message.schema.json",
    "reader_core_state": "reader-core-state.schema.json",
    "conversation_event": "conversation-event.schema.json",
    "decision_event": "decision-event.schema.json",
    "action_event": "action-event.schema.json",
    "accounting_event": "accounting-event.schema.json",
    "learning_event": "learning-event.schema.json",
    "living_memory_state": "living-memory-state.schema.json",
    "business_brain_state": "business-brain-state.schema.json",
    "autonomous_cycle_event": "autonomous-cycle-event.schema.json",
    "human_feedback_event": "human-feedback-event.schema.json",
    "domain_event": "domain-event-envelope.schema.json",
    "case_manager_state": "case-manager-state.schema.json",
    "channel_routing_state": "channel-routing-state.schema.json",
    "accounting_core_state": "accounting-core-state.schema.json",
    "cabin_authority_state": "cabin-authority-state.schema.json",
    "trust_safety_state": "trust-safety-state.schema.json",
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
            "stageNumber": 1,
            "name": "Domain Event Contracts",
            "status": "base_implemented_needs_final_closure",
            "reason": "Etapa 1 debe cerrarse como producto final.",
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
    "cabin_authority_state": {
        "status": "ok",
        "engine": "ariadgsm_cabin_authority",
        "phase": "ready",
        "summary": "Cabin Authority: las 3 columnas WhatsApp estan visibles y libres.",
        "reason": "manual_setup",
        "updatedAt": utc_now(),
        "exclusiveWindowControl": True,
        "handsMayFocus": True,
        "handsMayRecoverWindows": False,
        "handsMayArrangeWindows": False,
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
                "status": "ready",
                "handsMayAct": True,
                "expectedBounds": {"left": 0, "top": 0, "width": 640, "height": 900},
                "remainingBlockers": 0,
            },
            {
                "channelId": "wa-2",
                "browserProcess": "chrome",
                "status": "ready",
                "handsMayAct": True,
                "expectedBounds": {"left": 640, "top": 0, "width": 640, "height": 900},
                "remainingBlockers": 0,
            },
            {
                "channelId": "wa-3",
                "browserProcess": "firefox",
                "status": "ready",
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
    "trust_safety_state": {
        "status": "blocked",
        "engine": "ariadgsm_trust_safety_core",
        "version": "0.8.8",
        "updatedAt": utc_now(),
        "policy": {
            "version": "ariadgsm-trust-safety-0.8.8",
            "autonomyLevel": 3,
            "decisions": ["ALLOW", "ALLOW_WITH_LIMIT", "ASK_HUMAN", "PAUSE_FOR_OPERATOR", "BLOCK"],
            "principles": [
                "least_privilege",
                "human_approval_for_irreversible_actions",
                "verify_before_continue",
            ],
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
        "permissionGate": {
            "decision": "BLOCK",
            "reason": "Accion irreversible sin permiso explicito.",
            "canHandsRun": False,
            "allowedEngines": {
                "vision": True,
                "perception": True,
                "memory": True,
                "cognitive": True,
                "hands": False,
            },
        },
        "summary": {
            "decisionsRead": 1,
            "actionsRead": 1,
            "domainEventsRead": 1,
            "findings": 3,
            "allowed": 1,
            "allowedWithLimit": 0,
            "blocked": 1,
            "requiresHumanConfirmation": 1,
            "critical": 1,
            "safeNextActions": 1,
            "irreversibleBlocked": 1,
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
                "humanSummary": "Bloquee enviar mensaje al cliente: permiso explicito faltante.",
            }
        ],
        "safeNextActions": [],
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
