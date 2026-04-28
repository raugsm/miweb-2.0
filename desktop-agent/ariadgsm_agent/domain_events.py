from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .contracts import load_domain_event_registry, validate_contract
from .text import clean_text, looks_like_browser_ui_title, normalize


AGENT_ROOT = Path(__file__).resolve().parents[1]
SCHEMA_VERSION = "0.8.2"
SOURCE_SYSTEM = "ariadgsm-local-agent"

IGNORED_GROUP_TITLES = (
    "pagos mexico",
    "pagos chile",
    "pagos colombia",
)

ENGINE_ID_FIELDS: dict[str, tuple[str, ...]] = {
    "domain_event": ("eventId",),
    "vision_event": ("visionEventId",),
    "perception_event": ("perceptionEventId",),
    "conversation_event": ("conversationEventId",),
    "decision_event": ("decisionId",),
    "action_event": ("actionId",),
    "accounting_event": ("accountingId",),
    "learning_event": ("learningId",),
    "autonomous_cycle_event": ("cycleId",),
    "human_feedback_event": ("feedbackId",),
}

ACCOUNTING_DOMAIN_EVENT: dict[str, str] = {
    "payment": "PaymentDrafted",
    "debt": "DebtDetected",
    "refund": "RefundCandidate",
    "price_quote": "QuoteRecorded",
}

SIGNAL_DOMAIN_EVENT: dict[str, str] = {
    "payment": "PaymentDrafted",
    "debt": "DebtDetected",
    "price_request": "QuoteRequested",
    "service": "ServiceDetected",
    "device": "DeviceDetected",
    "country": "MarketSignalDetected",
    "urgency": "CaseNeedsHumanContext",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 24) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def clamp(value: Any, default: float = 0.5) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        number = default
    return max(0.0, min(1.0, number))


def compact_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def source_event_id(event: dict[str, Any]) -> str:
    event_type = clean_text(event.get("eventType"))
    for field in ENGINE_ID_FIELDS.get(event_type, ()):
        value = clean_text(event.get(field))
        if value:
            return value
    return f"{event_type or 'event'}-{stable_hash(compact_json(event))}"


def observed_at(event: dict[str, Any]) -> str:
    for field in ("observedAt", "createdAt", "capturedAt", "updatedAt"):
        value = clean_text(event.get(field))
        if value:
            return value
    return utc_now()


def source_domain_for_event(event: dict[str, Any]) -> str:
    event_type = clean_text(event.get("eventType"))
    if event_type == "vision_event":
        return "VisionEngine"
    if event_type == "perception_event":
        return "PerceptionEngine"
    if event_type == "conversation_event":
        return "TimelineEngine"
    if event_type == "accounting_event":
        return "AccountingBrain"
    if event_type == "learning_event":
        return "CognitiveCore"
    if event_type == "action_event":
        return "HandsEngine"
    if event_type == "autonomous_cycle_event":
        return "AutonomousCycle"
    if event_type == "human_feedback_event":
        return "HumanCollaboration"
    if event_type == "decision_event":
        decision_id = clean_text(event.get("decisionId")).lower()
        if decision_id.startswith("operating-") or event.get("caseId"):
            return "OperatingCore"
        return "CognitiveCore"
    return "DesktopAgentCore"


def actor_for_domain(source_domain: str) -> dict[str, str]:
    if source_domain == "HumanCollaboration":
        return {"type": "human", "id": "bryams"}
    if source_domain in {"CognitiveCore", "BusinessBrain", "AccountingBrain", "ChannelRoutingBrain"}:
        return {"type": "ai", "id": "ariadgsm-business-brain"}
    if source_domain in {"VisionEngine", "PerceptionEngine", "HandsEngine", "AutonomousCycle"}:
        return {"type": "system", "id": f"ariadgsm-{source_domain.lower()}"}
    return {"type": "system", "id": "ariadgsm-local-agent"}


def conversation_id_for(event: dict[str, Any]) -> str | None:
    value = clean_text(event.get("conversationId"))
    if value:
        return value
    target = event.get("target")
    if isinstance(target, dict):
        value = clean_text(target.get("conversationId"))
        if value:
            return value
    return None


def channel_id_for(event: dict[str, Any]) -> str | None:
    value = clean_text(event.get("channelId") or event.get("channelHint"))
    if value:
        return value
    target = event.get("target")
    if isinstance(target, dict):
        value = clean_text(target.get("channelId"))
        if value:
            return value
    return None


def case_id_for(event: dict[str, Any], channel_id: str | None, conversation_id: str | None) -> str | None:
    value = clean_text(event.get("caseId"))
    if value:
        return value
    target = event.get("target")
    if isinstance(target, dict):
        value = clean_text(target.get("caseId"))
        if value:
            return value
    if conversation_id:
        return f"case-{stable_hash((channel_id or 'unknown') + '|' + conversation_id, 16)}"
    return None


def customer_id_for(event: dict[str, Any], conversation_id: str | None) -> str | None:
    value = clean_text(event.get("customerId"))
    if value:
        return value
    target = event.get("target")
    if isinstance(target, dict):
        value = clean_text(target.get("customerId"))
        if value:
            return value
    if conversation_id:
        return f"customer-candidate-{stable_hash(conversation_id, 16)}"
    return "customer_pending"


def evidence_level_for_source(event_type: str, confidence: float) -> str:
    if event_type in {"conversation_event", "perception_event"}:
        return "B" if confidence >= 0.55 else "D"
    if event_type == "vision_event":
        return "C"
    if event_type in {"decision_event", "learning_event", "autonomous_cycle_event"}:
        return "E"
    if event_type in {"accounting_event", "action_event"}:
        return "B" if confidence >= 0.7 else "C"
    return "E"


def make_evidence(source_event: dict[str, Any], summary: str, confidence: float, limitations: list[str] | None = None) -> dict[str, Any]:
    event_type = clean_text(source_event.get("eventType") or "unknown")
    source_id = source_event_id(source_event)
    level = evidence_level_for_source(event_type, confidence)
    return {
        "evidenceId": f"ev-{stable_hash(event_type + '|' + source_id + '|' + summary, 20)}",
        "source": event_type,
        "evidenceLevel": level,
        "observedAt": observed_at(source_event),
        "summary": summary[:280] or "Source event observed.",
        "rawReference": f"local://{event_type}/{source_id}",
        "confidence": clamp(confidence),
        "redactionState": "safe_summary",
        "limitations": limitations or [],
    }


def risk_profile(event_type: str, source_event: dict[str, Any], autonomy_level: int | None = None) -> dict[str, Any]:
    registry = load_domain_event_registry()
    entry = (registry.get("eventTypes") or {}).get(event_type) or {}
    level = clean_text(entry.get("defaultRiskLevel") or "medium")
    reasons: list[str] = []
    allowed = ["reason", "record_audit"]
    blocked: list[str] = []

    if level in {"high", "critical"}:
        reasons.append("sensitive_business_action")
        blocked.extend(["send_message", "confirm_payment", "execute_external_tool"])
    if event_type in {"PaymentDrafted", "DebtDetected", "RefundCandidate", "QuoteProposed", "QuoteRecorded", "AccountingEvidenceAttached"}:
        reasons.append("accounting_or_pricing")
        allowed = ["record_draft", "ask_human", "reason"]
        blocked.extend(["confirm_payment", "close_debt"])
    if event_type in {"ActionRequested", "ActionExecuted", "ToolActionRequested"}:
        reasons.append("local_hands")
        allowed = ["verify_action", "record_audit"]
        blocked.extend(["send_message_without_approval"])
    if event_type == "ActionVerified":
        level = "low"
        allowed = ["continue_cycle", "record_audit"]
        blocked = []
    if event_type == "HumanApprovalRequired":
        level = "high"
        allowed = ["ask_bryams"]
        blocked.extend(["continue_without_human"])
    if event_type in {"PaymentConfirmed", "AccountingRecordConfirmed"}:
        level = "critical"
        reasons.append("payment_confirmation")
        blocked.extend(["confirm_without_level_a_evidence"])

    return {
        "riskLevel": level,
        "riskReasons": sorted(set(reasons)),
        "autonomyLevel": max(1, min(6, int(autonomy_level or source_event.get("autonomyLevel") or 1))),
        "allowedActions": sorted(set(allowed)),
        "blockedActions": sorted(set(blocked)),
    }


def privacy_profile(event_type: str, source_event: dict[str, Any], data: dict[str, Any]) -> dict[str, Any]:
    haystack = normalize(
        " ".join(
            [
                compact_json(data),
                clean_text(source_event.get("conversationTitle")),
                clean_text(source_event.get("clientName")),
            ]
        )
    )
    contains: set[str] = set()
    classification = "internal"
    cloud_allowed = True
    redaction_required = False
    retention = "case_lifetime"
    reason = "Business event summary."

    if event_type in {"PaymentDrafted", "PaymentEvidenceAttached", "PaymentConfirmed", "DebtDetected", "DebtUpdated", "RefundCandidate", "AccountingEvidenceAttached", "AccountingRecordConfirmed"}:
        classification = "payment"
        contains.add("payment")
        redaction_required = True
        retention = "business_record"
        reason = "Accounting or payment related event."
    if any(token in haystack for token in ("password", "contrasena", "token", "cookie", "session", "api key", "apikey")):
        classification = "credential"
        contains.add("credential")
        cloud_allowed = False
        redaction_required = True
        retention = "local_until_review"
        reason = "Possible credential or session data."
    if any(char.isdigit() for char in haystack) and any(token in haystack for token in ("telefono", "phone", "whatsapp", "imei", "serial")):
        contains.add("pii")
        if classification == "internal":
            classification = "pii"
        redaction_required = True
    if event_type in {"ObservationCreated", "MessageObjectDetected"} and source_event.get("frame"):
        classification = "sensitive"
        cloud_allowed = False
        redaction_required = True
        retention = "local_short_lived"
        contains.add("screen_frame")
        reason = "Raw visual observation stays local by default."

    return {
        "classification": classification,
        "cloudAllowed": cloud_allowed,
        "redactionRequired": redaction_required,
        "retentionPolicy": retention,
        "contains": sorted(contains),
        "reason": reason,
    }


def requires_human_review(event_type: str, risk: dict[str, Any], source_event: dict[str, Any]) -> bool:
    if bool(source_event.get("requiresHumanConfirmation")):
        return True
    if event_type in {"PaymentDrafted", "DebtDetected", "RefundCandidate", "AccountingEvidenceAttached", "AccountingRecordConfirmed", "ChannelRouteProposed", "HumanApprovalRequired"}:
        return True
    return risk.get("riskLevel") in {"high", "critical"}


def make_domain_event(
    event_type: str,
    source_event: dict[str, Any],
    *,
    subject_type: str,
    subject_id: str,
    data: dict[str, Any],
    confidence: float,
    summary: str,
    source_domain: str | None = None,
    autonomy_level: int | None = None,
    limitations: list[str] | None = None,
) -> dict[str, Any]:
    source_domain = source_domain or source_domain_for_event(source_event)
    source_id = source_event_id(source_event)
    source_type = clean_text(source_event.get("eventType") or "unknown")
    channel_id = channel_id_for(source_event)
    conversation_id = conversation_id_for(source_event)
    case_id = case_id_for(source_event, channel_id, conversation_id)
    customer_id = customer_id_for(source_event, conversation_id)
    confidence = clamp(confidence)
    evidence = [make_evidence(source_event, summary, confidence, limitations)]
    risk = risk_profile(event_type, source_event, autonomy_level=autonomy_level)
    privacy = privacy_profile(event_type, source_event, data)
    correlation_id = case_id or clean_text(source_event.get("correlationId")) or f"corr-{stable_hash(source_id, 16)}"
    idempotency_raw = compact_json(
        {
            "eventType": event_type,
            "source": source_type,
            "sourceId": source_id,
            "subjectType": subject_type,
            "subjectId": subject_id,
            "data": data,
        }
    )
    event_id = f"domain-{stable_hash(idempotency_raw, 24)}"
    domain_event = {
        "eventId": event_id,
        "eventType": event_type,
        "schemaVersion": SCHEMA_VERSION,
        "createdAt": utc_now(),
        "sourceDomain": source_domain,
        "sourceSystem": SOURCE_SYSTEM,
        "actor": actor_for_domain(source_domain),
        "subject": {"type": subject_type, "id": subject_id or source_id},
        "correlationId": correlation_id,
        "causationId": source_id,
        "idempotencyKey": f"{event_type}:{stable_hash(idempotency_raw, 32)}",
        "traceId": clean_text(source_event.get("traceId")) or f"trace-{stable_hash(correlation_id + '|' + source_id, 20)}",
        "channelId": channel_id,
        "conversationId": conversation_id,
        "caseId": case_id,
        "customerId": customer_id,
        "confidence": confidence,
        "evidence": evidence,
        "privacy": privacy,
        "risk": risk,
        "requiresHumanReview": requires_human_review(event_type, risk, source_event),
        "data": {
            "sourceEventType": source_type,
            "sourceEventId": source_id,
            **data,
        },
    }
    return domain_event


def message_count(event: dict[str, Any]) -> int:
    return len([item for item in event.get("messages") or [] if isinstance(item, dict)])


def collect_signals(conversation_event: dict[str, Any]) -> list[dict[str, Any]]:
    signals: list[dict[str, Any]] = []
    for message in conversation_event.get("messages") or []:
        if not isinstance(message, dict):
            continue
        for signal in message.get("signals") or []:
            if isinstance(signal, dict) and clean_text(signal.get("kind")):
                signals.append(
                    {
                        "kind": clean_text(signal.get("kind")),
                        "value": clean_text(signal.get("value")),
                        "confidence": clamp(signal.get("confidence"), 0.5),
                        "messageId": clean_text(message.get("messageId")),
                    }
                )
    return signals


def best_signals(signals: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    best: dict[str, dict[str, Any]] = {}
    for signal in signals:
        kind = clean_text(signal.get("kind"))
        if not kind:
            continue
        if kind not in best or clamp(signal.get("confidence")) > clamp(best[kind].get("confidence")):
            best[kind] = signal
    return best


def is_ignored_group(event: dict[str, Any]) -> bool:
    title = normalize(" ".join([clean_text(event.get("conversationTitle")), clean_text(event.get("conversationId"))]))
    return any(group in title for group in IGNORED_GROUP_TITLES)


def adapt_vision_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    frame = event.get("frame") if isinstance(event.get("frame"), dict) else {}
    subject_id = clean_text(frame.get("frameId")) or source_event_id(event)
    return [
        make_domain_event(
            "ObservationCreated",
            event,
            subject_type="screen_frame",
            subject_id=subject_id,
            data={
                "source": clean_text(event.get("source") or "screen_capture"),
                "visibleOnly": bool(event.get("visibleOnly")),
                "channelHint": clean_text(event.get("channelHint")),
                "width": frame.get("width"),
                "height": frame.get("height"),
                "storedLocallyOnly": bool(frame.get("storedLocallyOnly", True)),
            },
            confidence=0.65,
            summary="Local screen frame captured as visible evidence.",
            limitations=["Raw frame is local-only and not business truth by itself."],
        )
    ]


def adapt_perception_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    objects = [item for item in event.get("objects") or [] if isinstance(item, dict)]
    output = [
        make_domain_event(
            "ObservationCreated",
            event,
            subject_type="perception_snapshot",
            subject_id=source_event_id(event),
            data={"objectCount": len(objects), "sourceVisionEventId": clean_text(event.get("sourceVisionEventId"))},
            confidence=max([clamp(item.get("confidence"), 0.5) for item in objects] or [0.55]),
            summary=f"Perception produced {len(objects)} visible object(s).",
        )
    ]
    for index, item in enumerate(objects[:30]):
        object_type = clean_text(item.get("objectType") or "object")
        domain_type = "ChatRowDetected" if object_type == "chat_row" else "MessageObjectDetected" if object_type == "message_bubble" else ""
        if not domain_type:
            continue
        output.append(
            make_domain_event(
                domain_type,
                event,
                subject_type=object_type,
                subject_id=clean_text(item.get("objectId")) or f"{source_event_id(event)}:{index}",
                data={
                    "objectType": object_type,
                    "role": clean_text(item.get("role")),
                    "textPreview": clean_text(item.get("text"))[:160],
                    "bounds": item.get("bounds") if isinstance(item.get("bounds"), dict) else {},
                },
                confidence=clamp(item.get("confidence"), 0.5),
                summary=f"Visible {object_type} detected.",
            )
        )
    return output


def adapt_conversation_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    title = clean_text(event.get("conversationTitle") or event.get("conversationId"))
    conversation_id = conversation_id_for(event) or source_event_id(event)
    confidence = clamp(((event.get("quality") or {}).get("identityConfidence") if isinstance(event.get("quality"), dict) else None), 0.7)
    output: list[dict[str, Any]] = []

    if looks_like_browser_ui_title(title):
        return [
            make_domain_event(
                "ObservationRejected",
                event,
                subject_type="conversation",
                subject_id=conversation_id,
                data={"reason": "browser_or_generic_ui_title", "conversationTitle": title},
                confidence=0.95,
                summary=f"Rejected non-business conversation title: {title}.",
                limitations=["Not used for customer learning."],
            )
        ]

    output.append(
        make_domain_event(
            "ConversationObserved",
            event,
            subject_type="conversation",
            subject_id=conversation_id,
            data={
                "conversationTitle": title,
                "messageCount": message_count(event),
                "source": clean_text(event.get("source") or "timeline"),
                "timeline": event.get("timeline") if isinstance(event.get("timeline"), dict) else {},
            },
            confidence=confidence,
            summary=f"Conversation observed: {title}.",
        )
    )

    if is_ignored_group(event):
        output.append(
            make_domain_event(
                "GroupDetected",
                event,
                subject_type="conversation",
                subject_id=conversation_id,
                data={"conversationTitle": title, "groupPolicy": "low_learning_value"},
                confidence=0.92,
                summary=f"Group detected as low-learning channel: {title}.",
            )
        )
        output.append(
            make_domain_event(
                "LowLearningValueDetected",
                event,
                subject_type="conversation",
                subject_id=conversation_id,
                data={"conversationTitle": title, "reason": "payment_group_or_admin_group"},
                confidence=0.9,
                summary=f"Conversation should not train customer behavior directly: {title}.",
            )
        )
        return output

    output.append(
        make_domain_event(
            "CustomerCandidateIdentified",
            event,
            subject_type="customer_candidate",
            subject_id=customer_id_for(event, conversation_id) or conversation_id,
            data={"conversationTitle": title, "identitySource": "visible_conversation"},
            confidence=confidence,
            summary=f"Possible customer conversation identified: {title}.",
            limitations=["Candidate only until stronger identity evidence exists."],
        )
    )

    for kind, signal in best_signals(collect_signals(event)).items():
        domain_type = SIGNAL_DOMAIN_EVENT.get(kind)
        if not domain_type:
            continue
        data = {
            "signalKind": kind,
            "signalValue": clean_text(signal.get("value")),
            "messageId": clean_text(signal.get("messageId")),
            "conversationTitle": title,
        }
        output.append(
            make_domain_event(
                domain_type,
                event,
                subject_type="conversation_signal",
                subject_id=f"{conversation_id}:{kind}:{stable_hash(data['signalValue'], 8)}",
                data=data,
                confidence=clamp(signal.get("confidence"), 0.55),
                summary=f"Business signal detected: {kind}={data['signalValue'] or 'detected'}.",
                limitations=["Signal is not final truth without matching evidence level."],
            )
        )
    return output


def adapt_accounting_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    kind = clean_text(event.get("kind") or "unknown")
    domain_type = ACCOUNTING_DOMAIN_EVENT.get(kind)
    if not domain_type:
        return []
    data = {
        "accountingId": source_event_id(event),
        "kind": kind,
        "status": clean_text(event.get("status") or "draft"),
        "clientName": clean_text(event.get("clientName")),
        "amount": event.get("amount"),
        "currency": clean_text(event.get("currency")),
        "method": clean_text(event.get("method")),
    }
    return [
        make_domain_event(
            domain_type,
            event,
            subject_type="accounting_record",
            subject_id=source_event_id(event),
            data=data,
            confidence=clamp(event.get("confidence"), 0.5),
            summary=f"Accounting candidate created: {kind}.",
            source_domain="AccountingBrain",
            autonomy_level=2,
            limitations=["Accounting event remains draft unless confirmed with stronger evidence."],
        )
    ]


def adapt_decision_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    decision_id = source_event_id(event)
    source_domain = source_domain_for_event(event)
    confidence = clamp(event.get("confidence"), 0.5)
    data = {
        "decisionId": decision_id,
        "goal": clean_text(event.get("goal")),
        "intent": clean_text(event.get("intent")),
        "proposedAction": clean_text(event.get("proposedAction")),
        "reasoningSummary": clean_text(event.get("reasoningSummary"))[:500],
        "requiresHumanConfirmation": bool(event.get("requiresHumanConfirmation")),
    }
    output = [
        make_domain_event(
            "DecisionExplained",
            event,
            subject_type="decision",
            subject_id=decision_id,
            data=data,
            confidence=confidence,
            summary=data["reasoningSummary"] or "Decision explained by core.",
            source_domain=source_domain,
            autonomy_level=int(event.get("autonomyLevel") or 1),
        )
    ]
    if event.get("caseId"):
        output.append(
            make_domain_event(
                "CaseUpdated",
                event,
                subject_type="case",
                subject_id=clean_text(event.get("caseId")),
                data={
                    "caseId": clean_text(event.get("caseId")),
                    "intent": data["intent"],
                    "proposedAction": data["proposedAction"],
                },
                confidence=confidence,
                summary=f"Case updated from decision intent {data['intent']}.",
                source_domain="OperatingCore",
                autonomy_level=int(event.get("autonomyLevel") or 1),
            )
        )
    if bool(event.get("requiresHumanConfirmation")):
        output.append(
            make_domain_event(
                "HumanApprovalRequired",
                event,
                subject_type="decision",
                subject_id=decision_id,
                data={"reason": data["reasoningSummary"], "proposedAction": data["proposedAction"]},
                confidence=confidence,
                summary="Decision requires Bryams before action.",
                source_domain=source_domain,
                autonomy_level=int(event.get("autonomyLevel") or 1),
            )
        )
    return output


def adapt_action_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    status = clean_text(event.get("status") or "planned").lower()
    verification = event.get("verification") if isinstance(event.get("verification"), dict) else {}
    verified = bool(verification.get("verified")) or status == "verified"
    if verified:
        domain_type = "ActionVerified"
    elif status == "blocked":
        domain_type = "ActionBlocked"
    elif status == "failed":
        domain_type = "ActionFailed"
    elif status == "executed":
        domain_type = "ActionExecuted"
    else:
        domain_type = "ActionRequested"
    data = {
        "actionId": source_event_id(event),
        "actionType": clean_text(event.get("actionType")),
        "target": event.get("target") if isinstance(event.get("target"), dict) else {},
        "status": status,
        "verification": verification,
    }
    return [
        make_domain_event(
            domain_type,
            event,
            subject_type="action",
            subject_id=source_event_id(event),
            data=data,
            confidence=clamp(verification.get("confidence"), 0.5 if not verified else 0.85),
            summary=clean_text(verification.get("summary")) or f"Action status: {status}.",
            source_domain="HandsEngine",
            autonomy_level=int(((event.get("target") or {}).get("requiredAutonomyLevel") if isinstance(event.get("target"), dict) else 1) or 1),
        )
    ]


def adapt_learning_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        make_domain_event(
            "LearningCandidateCreated",
            event,
            subject_type="learning_candidate",
            subject_id=source_event_id(event),
            data={
                "learningId": source_event_id(event),
                "learningType": clean_text(event.get("learningType")),
                "summary": clean_text(event.get("summary"))[:500],
                "appliesTo": event.get("appliesTo") if isinstance(event.get("appliesTo"), list) else [],
            },
            confidence=clamp(event.get("confidence"), 0.5),
            summary=clean_text(event.get("summary")) or "Learning candidate created.",
            source_domain="CognitiveCore",
            autonomy_level=2,
            limitations=["Candidate only; not stable memory until accepted."],
        )
    ]


def adapt_cycle_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    status = clean_text(event.get("status") or "ok").lower()
    phase = clean_text(event.get("phase") or "checkpoint").lower()
    gate = event.get("permissionGate") if isinstance(event.get("permissionGate"), dict) else {}
    if status == "blocked":
        domain_type = "CycleBlocked"
    elif phase in {"start", "starting"}:
        domain_type = "CycleStarted"
    elif phase == "recovery":
        domain_type = "CycleRecovered"
    else:
        domain_type = "CycleCheckpointCreated"
    return [
        make_domain_event(
            domain_type,
            event,
            subject_type="autonomous_cycle",
            subject_id=source_event_id(event),
            data={
                "cycleId": source_event_id(event),
                "status": status,
                "phase": phase,
                "summary": clean_text(event.get("summary")),
                "stageCount": len([item for item in event.get("stages") or [] if isinstance(item, dict)]),
                "stepCount": len([item for item in event.get("steps") or [] if isinstance(item, dict)]),
                "gateDecision": clean_text(gate.get("decision")),
                "gateReason": clean_text(gate.get("reason")),
            },
            confidence=0.8 if status == "ok" else 0.7,
            summary=clean_text(event.get("summary")) or f"Autonomous cycle {status}.",
            source_domain="AutonomousCycle",
        )
    ]


def adapt_human_feedback_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    feedback_kind = clean_text(event.get("feedbackKind") or "note").lower()
    domain_type = {
        "correction": "HumanCorrectionReceived",
        "approval_granted": "HumanApprovalGranted",
        "approval_rejected": "HumanApprovalRejected",
        "operator_override": "OperatorOverrideRecorded",
        "note": "OperatorNoteAdded",
    }.get(feedback_kind, "OperatorNoteAdded")
    summary = clean_text(event.get("summary") or event.get("correction") or "Human feedback received.")
    data = {
        "feedbackId": source_event_id(event),
        "feedbackKind": feedback_kind,
        "targetEventId": clean_text(event.get("targetEventId")),
        "targetEventType": clean_text(event.get("targetEventType")),
        "summary": summary,
        "correction": clean_text(event.get("correction")),
        "requiresFollowUp": bool(event.get("requiresFollowUp")),
    }
    limitations: list[str] = []
    if domain_type in {"HumanApprovalGranted", "HumanApprovalRejected", "OperatorOverrideRecorded"}:
        limitations.append("Human decision changes operational permission state; keep full audit trail.")
    return [
        make_domain_event(
            domain_type,
            event,
            subject_type="human_feedback",
            subject_id=source_event_id(event),
            data=data,
            confidence=clamp(event.get("confidence"), 1.0),
            summary=summary,
            source_domain="HumanCollaboration",
            autonomy_level=1,
            limitations=limitations,
        )
    ]


def adapt_engine_event(event: dict[str, Any]) -> list[dict[str, Any]]:
    event_type = clean_text(event.get("eventType"))
    if event.get("eventId") and event.get("schemaVersion") and event.get("sourceDomain"):
        return [event]
    if event_type == "vision_event":
        return adapt_vision_event(event)
    if event_type == "perception_event":
        return adapt_perception_event(event)
    if event_type == "conversation_event":
        return adapt_conversation_event(event)
    if event_type == "accounting_event":
        return adapt_accounting_event(event)
    if event_type == "decision_event":
        return adapt_decision_event(event)
    if event_type == "action_event":
        return adapt_action_event(event)
    if event_type == "learning_event":
        return adapt_learning_event(event)
    if event_type == "autonomous_cycle_event":
        return adapt_cycle_event(event)
    if event_type == "human_feedback_event":
        return adapt_human_feedback_event(event)
    return []


class DomainEventStore:
    def __init__(self, db_path: Path, jsonl_path: Path):
        self.db_path = db_path
        self.jsonl_path = jsonl_path
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.jsonl_path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.executescript(
            """
            create table if not exists domain_events (
              event_id text primary key,
              idempotency_key text not null unique,
              event_type text not null,
              source_domain text not null,
              source_event_type text not null,
              source_event_id text not null,
              correlation_id text,
              causation_id text,
              trace_id text,
              channel_id text,
              conversation_id text,
              case_id text,
              customer_id text,
              confidence real not null,
              risk_level text not null,
              privacy_classification text not null,
              requires_human_review integer not null,
              cloud_allowed integer not null,
              event_json text not null,
              created_at text not null,
              stored_at text not null
            );
            create index if not exists idx_domain_events_type on domain_events(event_type);
            create index if not exists idx_domain_events_case on domain_events(case_id);
            create index if not exists idx_domain_events_conversation on domain_events(conversation_id);
            create index if not exists idx_domain_events_trace on domain_events(trace_id);
            """
        )
        self.conn.commit()

    def save(self, event: dict[str, Any]) -> tuple[bool, str]:
        errors = validate_contract(event, "domain_event")
        if errors:
            return False, "; ".join(errors)

        row = self.conn.execute(
            "select event_id from domain_events where idempotency_key = ? limit 1",
            (event["idempotencyKey"],),
        ).fetchone()
        if row is not None:
            return False, "duplicate"

        privacy = event.get("privacy") if isinstance(event.get("privacy"), dict) else {}
        risk = event.get("risk") if isinstance(event.get("risk"), dict) else {}
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        payload = json.dumps(event, ensure_ascii=False, separators=(",", ":"))
        with self.conn:
            self.conn.execute(
                """
                insert into domain_events (
                  event_id, idempotency_key, event_type, source_domain, source_event_type,
                  source_event_id, correlation_id, causation_id, trace_id, channel_id,
                  conversation_id, case_id, customer_id, confidence, risk_level,
                  privacy_classification, requires_human_review, cloud_allowed,
                  event_json, created_at, stored_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    event["eventId"],
                    event["idempotencyKey"],
                    event["eventType"],
                    event["sourceDomain"],
                    clean_text(data.get("sourceEventType")),
                    clean_text(data.get("sourceEventId")),
                    clean_text(event.get("correlationId")),
                    clean_text(event.get("causationId")),
                    clean_text(event.get("traceId")),
                    clean_text(event.get("channelId")),
                    clean_text(event.get("conversationId")),
                    clean_text(event.get("caseId")),
                    clean_text(event.get("customerId")),
                    float(event.get("confidence") or 0.0),
                    clean_text(risk.get("riskLevel")),
                    clean_text(privacy.get("classification")),
                    1 if event.get("requiresHumanReview") else 0,
                    1 if privacy.get("cloudAllowed") else 0,
                    payload,
                    clean_text(event.get("createdAt")),
                    utc_now(),
                ),
            )
            with self.jsonl_path.open("a", encoding="utf-8") as handle:
                handle.write(payload + "\n")
        return True, "stored"

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        def counter(sql: str) -> dict[str, int]:
            return {str(row[0]): int(row[1]) for row in self.conn.execute(sql).fetchall()}

        latest = self.conn.execute(
            """
            select event_type, source_domain, conversation_id, case_id, confidence,
                   risk_level, requires_human_review, created_at
            from domain_events
            order by stored_at desc
            limit 8
            """
        ).fetchall()
        return {
            "events": scalar("select count(*) from domain_events"),
            "requiresHumanReview": scalar("select count(*) from domain_events where requires_human_review = 1"),
            "cloudBlocked": scalar("select count(*) from domain_events where cloud_allowed = 0"),
            "byType": counter("select event_type, count(*) from domain_events group by event_type order by event_type"),
            "byRisk": counter("select risk_level, count(*) from domain_events group by risk_level order by risk_level"),
            "latest": [dict(row) for row in latest],
            "db": str(self.db_path),
            "jsonl": str(self.jsonl_path),
        }


def read_jsonl_events(path: Path, limit: int = 500) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    events: list[dict[str, Any]] = []
    lines = [line for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()][-limit:]
    for line in lines:
        try:
            item = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(item, dict):
            events.append(item)
    return events


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def build_human_report(new_events: list[dict[str, Any]], summary: dict[str, Any], invalid: list[dict[str, Any]]) -> dict[str, Any]:
    counts = Counter(event["eventType"] for event in new_events)
    needs = [
        f"{event['eventType']}: {event['evidence'][0]['summary']}"
        for event in new_events
        if event.get("requiresHumanReview")
    ][:8]
    blocked = [
        f"{event['eventType']}: {event['privacy']['reason']}"
        for event in new_events
        if isinstance(event.get("privacy"), dict) and not event["privacy"].get("cloudAllowed")
    ][:8]
    return {
        "quePaso": f"Converti {len(new_events)} eventos de motor en eventos de negocio validados.",
        "queEntendi": dict(counts),
        "queNecesitoDeBryams": needs,
        "privacidad": blocked,
        "errores": invalid[:8],
        "memoriaDeEventos": {
            "total": summary.get("events", 0),
            "requierenRevision": summary.get("requiresHumanReview", 0),
            "cloudBloqueado": summary.get("cloudBlocked", 0),
        },
    }


def run_domain_events_once(
    source_files: list[Path],
    output_file: Path,
    state_file: Path,
    db_path: Path,
    limit: int = 500,
) -> dict[str, Any]:
    store = DomainEventStore(db_path, output_file)
    ingested = {"sourceFiles": 0, "engineEvents": 0, "domainEvents": 0, "duplicates": 0, "invalid": 0}
    new_events: list[dict[str, Any]] = []
    invalid: list[dict[str, Any]] = []
    try:
        for path in source_files:
            engine_events = read_jsonl_events(path, limit=limit)
            if engine_events:
                ingested["sourceFiles"] += 1
            ingested["engineEvents"] += len(engine_events)
            for engine_event in engine_events:
                for domain_event in adapt_engine_event(engine_event):
                    stored, reason = store.save(domain_event)
                    if stored:
                        ingested["domainEvents"] += 1
                        new_events.append(domain_event)
                    elif reason == "duplicate":
                        ingested["duplicates"] += 1
                    else:
                        ingested["invalid"] += 1
                        invalid.append(
                            {
                                "sourceEventType": engine_event.get("eventType"),
                                "sourceEventId": source_event_id(engine_event),
                                "domainEventType": domain_event.get("eventType"),
                                "reason": reason,
                            }
                        )

        summary = store.summary()
        status = "attention" if ingested["invalid"] else "ok"
        if ingested["engineEvents"] == 0:
            status = "idle"
        state = {
            "status": status,
            "engine": "ariadgsm_domain_event_contracts",
            "updatedAt": utc_now(),
            "schemaVersion": SCHEMA_VERSION,
            "sourceFiles": [str(path) for path in source_files],
            "outputFile": str(output_file),
            "db": str(db_path),
            "ingested": ingested,
            "summary": summary,
            "humanReport": build_human_report(new_events, summary, invalid),
        }
        write_json(state_file, state)
        return state
    finally:
        store.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Domain Event Contracts")
    parser.add_argument("--vision-events", default="runtime/vision-events.jsonl")
    parser.add_argument("--perception-events", default="runtime/perception-events.jsonl")
    parser.add_argument("--conversation-events", default="runtime/timeline-events.jsonl")
    parser.add_argument("--raw-conversation-events", default="runtime/conversation-events.jsonl")
    parser.add_argument("--decision-events", default="runtime/decision-events.jsonl")
    parser.add_argument("--cognitive-decision-events", default="runtime/cognitive-decision-events.jsonl")
    parser.add_argument("--accounting-events", default="runtime/accounting-events.jsonl")
    parser.add_argument("--accounting-core-events", default="runtime/accounting-core-events.jsonl")
    parser.add_argument("--learning-events", default="runtime/learning-events.jsonl")
    parser.add_argument("--action-events", default="runtime/action-events.jsonl")
    parser.add_argument("--autonomous-cycle-events", default="runtime/autonomous-cycle-events.jsonl")
    parser.add_argument("--human-feedback-events", default="runtime/human-feedback-events.jsonl")
    parser.add_argument("--case-events", default="runtime/case-events.jsonl")
    parser.add_argument("--route-events", default="runtime/route-events.jsonl")
    parser.add_argument("--domain-events", default="runtime/domain-events.jsonl")
    parser.add_argument("--state-file", default="runtime/domain-events-state.json")
    parser.add_argument("--db", default="runtime/domain-events.sqlite")
    parser.add_argument("--limit", type=int, default=500)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args()
    source_files = [
        resolve_runtime_path(args.vision_events),
        resolve_runtime_path(args.perception_events),
        resolve_runtime_path(args.conversation_events),
        resolve_runtime_path(args.raw_conversation_events),
        resolve_runtime_path(args.decision_events),
        resolve_runtime_path(args.cognitive_decision_events),
        resolve_runtime_path(args.accounting_events),
        resolve_runtime_path(args.accounting_core_events),
        resolve_runtime_path(args.learning_events),
        resolve_runtime_path(args.action_events),
        resolve_runtime_path(args.autonomous_cycle_events),
        resolve_runtime_path(args.human_feedback_events),
        resolve_runtime_path(args.case_events),
        resolve_runtime_path(args.route_events),
    ]
    state = run_domain_events_once(
        source_files,
        resolve_runtime_path(args.domain_events),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.db),
        limit=max(1, args.limit),
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        ingested = state["ingested"]
        summary = state["summary"]
        print(
            "AriadGSM Domain Events: "
            f"engine_events={ingested['engineEvents']} "
            f"domain_events={ingested['domainEvents']} "
            f"duplicates={ingested['duplicates']} "
            f"invalid={ingested['invalid']} "
            f"total={summary['events']} "
            f"needs_human={summary['requiresHumanReview']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
