from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.memory import MemoryStore, run_memory_once


def write_jsonl(path: Path, *events: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events), encoding="utf-8")


def main() -> int:
    assert not validate_contract(sample_event("living_memory_state"), "living_memory_state")

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        conversation_file = root / "timeline-events.jsonl"
        cognitive_file = root / "cognitive-decision-events.jsonl"
        operating_file = root / "decision-events.jsonl"
        learning_file = root / "learning-events.jsonl"
        accounting_file = root / "accounting-events.jsonl"
        domain_file = root / "domain-events.jsonl"
        feedback_file = root / "human-feedback-events.jsonl"
        state_file = root / "memory-state.json"
        db_file = root / "memory-core.sqlite"

        conversation = {
            "eventType": "conversation_event",
            "conversationEventId": "conversation-living-1",
            "conversationId": "wa-2-omar-mx",
            "channelId": "wa-2",
            "observedAt": "2026-04-28T12:00:00Z",
            "conversationTitle": "Omar Torres Mexico",
            "source": "live",
            "messages": [
                {
                    "messageId": "msg-living-1",
                    "text": "Bro cuanto vale liberar Samsung Mexico? pago por USDT",
                    "direction": "client",
                    "senderName": "Omar",
                    "sentAt": "2026-04-28T12:00:00Z",
                    "confidence": 0.94,
                    "signals": [
                        {"kind": "service", "value": "samsung", "confidence": 0.88},
                        {"kind": "country", "value": "MX", "confidence": 0.91},
                        {"kind": "price_request", "value": "cuanto vale", "confidence": 0.9},
                        {"kind": "slang", "value": "bro", "confidence": 0.82},
                        {"kind": "payment", "value": "USDT", "confidence": 0.68},
                    ],
                }
            ],
            "timeline": {"historyLimitDays": 30, "complete": False, "dedupeStrategy": "reader_core"},
            "quality": {"isReliable": True, "identityConfidence": 0.97, "identitySource": "reader_core"},
        }
        learning = {
            "eventType": "learning_event",
            "learningId": "learning-living-procedure-1",
            "createdAt": "2026-04-28T12:01:00Z",
            "learningType": "procedure",
            "source": "operator",
            "summary": "Para Samsung MX primero confirmar modelo exacto antes de cotizar.",
            "confidence": 0.86,
            "after": {"conversationId": "wa-2-omar-mx", "channelId": "wa-2", "conversationTitle": "Omar Torres Mexico"},
            "appliesTo": ["samsung", "MX", "price_quote"],
        }
        accounting = {
            "eventType": "accounting_event",
            "accountingId": "accounting-living-1",
            "createdAt": "2026-04-28T12:02:00Z",
            "status": "draft",
            "confidence": 0.62,
            "clientName": "Omar Torres Mexico",
            "conversationId": "wa-2-omar-mx",
            "kind": "payment",
            "amount": 20,
            "currency": "USDT",
            "method": "Binance",
            "evidence": ["msg-living-1"],
        }
        domain = {
            "eventId": "domain-living-case-1",
            "eventType": "CaseOpened",
            "schemaVersion": "0.8.2",
            "createdAt": "2026-04-28T12:03:00Z",
            "sourceDomain": "CaseManager",
            "sourceSystem": "ariadgsm-local-agent",
            "actor": {"type": "system", "id": "case-manager"},
            "subject": {"type": "case", "id": "case-wa-2-omar"},
            "correlationId": "case-wa-2-omar",
            "causationId": "conversation-living-1",
            "idempotencyKey": "CaseOpened:case-wa-2-omar",
            "traceId": "trace-living-1",
            "channelId": "wa-2",
            "conversationId": "wa-2-omar-mx",
            "caseId": "case-wa-2-omar",
            "customerId": "customer-omar",
            "confidence": 0.89,
            "evidence": [{"evidenceId": "ev-living-1", "source": "conversation_event", "evidenceLevel": "B", "summary": "Conversacion visible."}],
            "risk": {"riskLevel": "low", "reasons": []},
            "privacy": {"containsPersonalData": True, "redactionApplied": False, "allowedForCloudSync": True},
            "requiresHumanReview": False,
            "data": {"summary": "Caso abierto por pregunta de precio Samsung MX.", "conversationTitle": "Omar Torres Mexico"},
        }
        feedback = {
            "eventType": "human_feedback_event",
            "feedbackId": "feedback-living-1",
            "createdAt": "2026-04-28T12:04:00Z",
            "feedbackKind": "correction",
            "targetEventId": "accounting-living-1",
            "targetEventType": "accounting_event",
            "channelId": "wa-2",
            "conversationId": "wa-2-omar-mx",
            "caseId": "case-wa-2-omar",
            "customerId": "customer-omar",
            "summary": "El pago USDT aun no esta confirmado.",
            "correction": "Mantener pago como deuda hasta recibir comprobante.",
            "confidence": 1.0,
            "requiresFollowUp": True,
            "actor": {"type": "human", "id": "bryams"},
        }

        write_jsonl(conversation_file, conversation)
        write_jsonl(learning_file, learning)
        write_jsonl(accounting_file, accounting)
        write_jsonl(domain_file, domain)
        write_jsonl(feedback_file, feedback)

        state = run_memory_once(
            conversation_file,
            cognitive_file,
            operating_file,
            learning_file,
            accounting_file,
            state_file,
            db_file,
            domain_events_file=domain_file,
            human_feedback_events_file=feedback_file,
        )

        assert state["status"] == "ok"
        assert state["capability"] == "ariadgsm_living_memory"
        assert state["summary"]["memoryMessages"] == 1
        assert state["livingMemory"]["byLayer"]["episodic"] >= 1
        assert state["livingMemory"]["byLayer"]["semantic"] >= 1
        assert state["livingMemory"]["byLayer"]["procedural"] >= 1
        assert state["livingMemory"]["byLayer"]["accounting"] >= 1
        assert state["livingMemory"]["byLayer"]["style"] >= 1
        assert state["livingMemory"]["byLayer"]["correction"] >= 1
        assert state["livingMemory"]["uncertainties"] >= 1
        assert state["livingMemory"]["degraded"] >= 1
        assert state["livingMemory"]["corrections"] == 1
        assert state["humanReport"]["queAprendi"]
        assert state["humanReport"]["queDudo"]
        assert state["humanReport"]["queCorrigioBryams"]
        assert not validate_contract(state, "living_memory_state")

        repeated = run_memory_once(
            conversation_file,
            cognitive_file,
            operating_file,
            learning_file,
            accounting_file,
            state_file,
            db_file,
            domain_events_file=domain_file,
            human_feedback_events_file=feedback_file,
        )
        assert repeated["ingested"]["events"] == 0
        assert repeated["ingested"]["duplicates"] >= 5

        store = MemoryStore(db_file)
        try:
            profile = store.customer_profile("wa-2-omar-mx")
            assert profile is not None
            assert "MX" in profile["countries"]
            assert "samsung" in profile["services"]
        finally:
            store.close()

    print("living memory OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
