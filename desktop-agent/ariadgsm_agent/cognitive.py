from __future__ import annotations

import hashlib
from datetime import datetime, timezone
from typing import Any

from .classifier import classify_messages
from .supervisor import SupervisorPolicy, action_permission


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def decision_event_from_conversation(conversation_event: dict[str, Any], policy: SupervisorPolicy | None = None) -> dict[str, Any]:
    policy = policy or SupervisorPolicy()
    messages = conversation_event.get("messages") or []
    decision = classify_messages(messages)
    confidence = min(0.95, max(0.35, decision.score / 10.0))
    required_level = 2
    if decision.intent in {"accounting_payment", "accounting_debt"}:
        required_level = 4
    permission = action_permission(confidence, required_level, policy)
    raw_id = f"{conversation_event.get('conversationId')}|{decision.intent}|{decision.text}|{utc_now()}"
    return {
        "eventType": "decision_event",
        "decisionId": hashlib.sha1(raw_id.encode("utf-8")).hexdigest(),
        "createdAt": utc_now(),
        "goal": "operate_ariadgsm_business",
        "intent": decision.intent,
        "confidence": confidence,
        "autonomyLevel": policy.autonomy_level,
        "proposedAction": decision.label,
        "requiresHumanConfirmation": bool(permission["requiresHumanConfirmation"]),
        "reasoningSummary": "; ".join(decision.reasons) or decision.label,
        "evidence": [message.get("messageId") or message.get("text") for message in messages[-5:] if isinstance(message, dict)],
    }

