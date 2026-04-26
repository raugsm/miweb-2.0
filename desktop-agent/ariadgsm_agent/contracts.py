from __future__ import annotations

import json
from copy import deepcopy
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_DIR = ROOT / "contracts"

CONTRACT_FILES: dict[str, str] = {
    "vision_event": "vision-event.schema.json",
    "perception_event": "perception-event.schema.json",
    "conversation_event": "conversation-event.schema.json",
    "decision_event": "decision-event.schema.json",
    "action_event": "action-event.schema.json",
    "accounting_event": "accounting-event.schema.json",
    "learning_event": "learning-event.schema.json",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def load_schema(contract_name: str) -> dict[str, Any]:
    if contract_name not in CONTRACT_FILES:
        raise KeyError(f"Contrato desconocido: {contract_name}")
    path = CONTRACT_DIR / CONTRACT_FILES[contract_name]
    return json.loads(path.read_text(encoding="utf-8"))


def validate_contract(event: dict[str, Any], contract_name: str) -> list[str]:
    schema = load_schema(contract_name)
    errors: list[str] = []
    if not isinstance(event, dict):
        return ["event must be an object"]
    for field in schema.get("required") or []:
        if field not in event:
            errors.append(f"missing required field: {field}")
    event_type_schema = (schema.get("properties") or {}).get("eventType") or {}
    expected_const = event_type_schema.get("const")
    if expected_const and event.get("eventType") != expected_const:
        errors.append(f"eventType must be {expected_const}")
    expected_enum = event_type_schema.get("enum")
    if expected_enum and event.get("eventType") not in expected_enum:
        errors.append(f"eventType must be one of {expected_enum}")
    return errors


SAMPLE_EVENTS: dict[str, dict[str, Any]] = {
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
}


def sample_event(contract_name: str) -> dict[str, Any]:
    if contract_name not in SAMPLE_EVENTS:
        raise KeyError(f"Contrato desconocido: {contract_name}")
    return deepcopy(SAMPLE_EVENTS[contract_name])
