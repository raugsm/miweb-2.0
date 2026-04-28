from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.domain_events import adapt_engine_event, run_domain_events_once
from ariadgsm_agent.memory import run_memory_once


def write_jsonl(path: Path, *events: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        "".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events),
        encoding="utf-8",
    )


def main() -> int:
    domain_sample = sample_event("domain_event")
    assert not validate_contract(domain_sample, "domain_event")

    payment_conversation = {
        "eventType": "conversation_event",
        "conversationEventId": "conversation-payment-domain-test",
        "conversationId": "wa-2-cliente-pago",
        "channelId": "wa-2",
        "observedAt": "2026-04-27T19:00:00Z",
        "conversationTitle": "Cliente Pago",
        "source": "timeline",
        "messages": [
            {
                "messageId": "pay-domain-1",
                "text": "Ya hice el pago de 100 usd por Xiaomi",
                "direction": "client",
                "signals": [
                    {"kind": "payment", "value": "pago", "confidence": 0.93},
                    {"kind": "amount", "value": "100 USD", "confidence": 0.82},
                    {"kind": "service", "value": "xiaomi", "confidence": 0.79},
                ],
            }
        ],
        "timeline": {"historyLimitDays": 30, "complete": False},
    }
    payment_domain_events = adapt_engine_event(payment_conversation)
    payment_types = {event["eventType"] for event in payment_domain_events}
    assert "ConversationObserved" in payment_types
    assert "CustomerCandidateIdentified" in payment_types
    assert "PaymentDrafted" in payment_types
    assert "ServiceDetected" in payment_types
    assert "PaymentConfirmed" not in payment_types
    payment_event = next(event for event in payment_domain_events if event["eventType"] == "PaymentDrafted")
    assert payment_event["requiresHumanReview"] is True
    assert payment_event["privacy"]["classification"] == "payment"
    assert payment_event["risk"]["riskLevel"] == "medium"
    assert not validate_contract(payment_event, "domain_event")

    group_conversation = dict(payment_conversation)
    group_conversation["conversationEventId"] = "conversation-group-domain-test"
    group_conversation["conversationId"] = "wa-2-pagos-mexico"
    group_conversation["conversationTitle"] = "Pagos Mexico"
    group_events = adapt_engine_event(group_conversation)
    group_types = {event["eventType"] for event in group_events}
    assert "GroupDetected" in group_types
    assert "LowLearningValueDetected" in group_types
    assert "CustomerCandidateIdentified" not in group_types

    browser_conversation = dict(payment_conversation)
    browser_conversation["conversationEventId"] = "conversation-browser-ui-test"
    browser_conversation["conversationId"] = "wa-3-browser-ui"
    browser_conversation["conversationTitle"] = "Anadir esta pagina a marcadores (Ctrl+D)"
    browser_events = adapt_engine_event(browser_conversation)
    assert [event["eventType"] for event in browser_events] == ["ObservationRejected"]

    accounting_event = {
        "eventType": "accounting_event",
        "accountingId": "accounting-domain-test",
        "createdAt": "2026-04-27T19:01:00Z",
        "status": "draft",
        "confidence": 0.75,
        "clientName": "Cliente Pago",
        "conversationId": "wa-2-cliente-pago",
        "kind": "payment",
        "amount": 100,
        "currency": "USD",
        "evidence": ["pay-domain-1"],
    }
    action_event = {
        "eventType": "action_event",
        "actionId": "action-domain-test",
        "createdAt": "2026-04-27T19:02:00Z",
        "actionType": "open_chat",
        "target": {"channelId": "wa-2", "conversationId": "wa-2-cliente-pago"},
        "status": "verified",
        "verification": {"verified": True, "summary": "Chat correcto abierto.", "confidence": 0.91},
    }
    decision_event = {
        "eventType": "decision_event",
        "decisionId": "cognitive-domain-test",
        "createdAt": "2026-04-27T19:03:00Z",
        "goal": "understand_and_operate_ariadgsm_business",
        "intent": "accounting_payment",
        "confidence": 0.88,
        "autonomyLevel": 2,
        "proposedAction": "review_payment_evidence",
        "requiresHumanConfirmation": True,
        "reasoningSummary": "Possible payment found; needs evidence review.",
        "evidence": ["pay-domain-1"],
        "conversationId": "wa-2-cliente-pago",
        "channelId": "wa-2",
    }
    learning_event = {
        "eventType": "learning_event",
        "learningId": "learning-domain-test",
        "createdAt": "2026-04-27T19:04:00Z",
        "learningType": "accounting",
        "source": "cognitive_core",
        "summary": "Clientes dicen 'hice el pago' para indicar pago pendiente de validar.",
        "confidence": 0.9,
        "appliesTo": ["accounting_payment"],
    }
    human_feedback_event = {
        "eventType": "human_feedback_event",
        "feedbackId": "human-domain-test",
        "createdAt": "2026-04-27T19:05:00Z",
        "feedbackKind": "correction",
        "targetEventId": "domain-payment-test",
        "targetEventType": "PaymentDrafted",
        "channelId": "wa-2",
        "conversationId": "wa-2-cliente-pago",
        "caseId": "case-wa-2-cliente-pago",
        "customerId": "customer-wa-2-cliente-pago",
        "summary": "Ese pago debe seguir como borrador.",
        "correction": "Falta comprobante.",
        "confidence": 1.0,
        "requiresFollowUp": True,
        "actor": {"type": "human", "id": "bryams"},
    }

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        conversation_file = root / "timeline-events.jsonl"
        accounting_file = root / "accounting-events.jsonl"
        action_file = root / "action-events.jsonl"
        decision_file = root / "decision-events.jsonl"
        learning_file = root / "learning-events.jsonl"
        human_file = root / "human-feedback-events.jsonl"
        domain_file = root / "domain-events.jsonl"
        state_file = root / "domain-events-state.json"
        db_file = root / "domain-events.sqlite"
        memory_state_file = root / "memory-state.json"
        memory_db_file = root / "memory.sqlite"
        write_jsonl(conversation_file, payment_conversation, group_conversation, browser_conversation)
        write_jsonl(accounting_file, accounting_event)
        write_jsonl(action_file, action_event)
        write_jsonl(decision_file, decision_event)
        write_jsonl(learning_file, learning_event)
        write_jsonl(human_file, human_feedback_event)

        state = run_domain_events_once(
            [conversation_file, accounting_file, action_file, decision_file, learning_file, human_file],
            domain_file,
            state_file,
            db_file,
            limit=100,
        )
        assert state["status"] == "ok", state
        assert state["ingested"]["domainEvents"] >= 10, state
        assert state["ingested"]["invalid"] == 0, state
        assert "PaymentDrafted" in state["summary"]["byType"], state
        assert "ActionVerified" in state["summary"]["byType"], state
        assert "LearningCandidateCreated" in state["summary"]["byType"], state
        assert "HumanCorrectionReceived" in state["summary"]["byType"], state
        assert state["summary"]["requiresHumanReview"] >= 2, state
        assert state["humanReport"]["queNecesitoDeBryams"], state
        assert domain_file.exists()

        memory_state = run_memory_once(
            conversation_file,
            decision_file,
            decision_file,
            learning_file,
            accounting_file,
            memory_state_file,
            memory_db_file,
            limit=100,
            domain_events_file=domain_file,
        )
        assert memory_state["ingested"]["domainEvents"] >= state["ingested"]["domainEvents"], memory_state
        assert memory_state["summary"]["domainEvents"] >= state["ingested"]["domainEvents"], memory_state

        repeated = run_domain_events_once(
            [conversation_file, accounting_file, action_file, decision_file, learning_file, human_file],
            domain_file,
            state_file,
            db_file,
            limit=100,
        )
        assert repeated["ingested"]["domainEvents"] == 0, repeated
        assert repeated["ingested"]["duplicates"] >= state["ingested"]["domainEvents"], repeated

    print("domain event contracts OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
