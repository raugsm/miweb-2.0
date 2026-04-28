from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.case_manager import run_case_manager_once
from ariadgsm_agent.contracts import validate_contract
from ariadgsm_agent.domain_events import run_domain_events_once


def write_jsonl(path: Path, *events: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events), encoding="utf-8")


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def main() -> int:
    payment_conversation = {
        "eventType": "conversation_event",
        "conversationEventId": "conversation-case-payment-test",
        "conversationId": "wa-2-cliente-case",
        "channelId": "wa-2",
        "observedAt": "2026-04-28T10:00:00Z",
        "conversationTitle": "Cliente Case Manager",
        "source": "timeline",
        "messages": [
            {
                "messageId": "case-msg-1",
                "text": "Hola bro cuanto sale liberar Xiaomi y ya hice el pago de 100 usd",
                "direction": "client",
                "signals": [
                    {"kind": "price_request", "value": "cuanto sale", "confidence": 0.91},
                    {"kind": "service", "value": "liberar Xiaomi", "confidence": 0.84},
                    {"kind": "payment", "value": "pago 100 usd", "confidence": 0.9},
                ],
            }
        ],
        "timeline": {"historyLimitDays": 30, "complete": False},
    }
    decision_event = {
        "eventType": "decision_event",
        "decisionId": "decision-case-payment-test",
        "createdAt": "2026-04-28T10:01:00Z",
        "goal": "understand_and_operate_ariadgsm_business",
        "intent": "accounting_payment",
        "confidence": 0.88,
        "autonomyLevel": 3,
        "proposedAction": "review_payment_evidence",
        "requiresHumanConfirmation": True,
        "reasoningSummary": "Cliente mezcla precio y pago; abrir caso y pedir validacion contable.",
        "conversationId": "wa-2-cliente-case",
        "channelId": "wa-2",
    }
    action_event = {
        "eventType": "action_event",
        "actionId": "action-case-open-chat",
        "createdAt": "2026-04-28T10:02:00Z",
        "actionType": "open_chat",
        "target": {"channelId": "wa-2", "conversationId": "wa-2-cliente-case"},
        "status": "verified",
        "verification": {"verified": True, "summary": "Chat correcto abierto.", "confidence": 0.92},
    }
    group_conversation = {
        **payment_conversation,
        "conversationEventId": "conversation-case-group-test",
        "conversationId": "wa-1-pagos-mexico",
        "channelId": "wa-1",
        "conversationTitle": "Pagos Mexico",
    }

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        timeline_file = root / "timeline-events.jsonl"
        decision_file = root / "decision-events.jsonl"
        action_file = root / "action-events.jsonl"
        domain_file = root / "domain-events.jsonl"
        domain_state_file = root / "domain-events-state.json"
        domain_db = root / "domain-events.sqlite"
        case_events_file = root / "case-events.jsonl"
        case_state_file = root / "case-manager-state.json"
        case_report_file = root / "case-manager-report.json"
        case_db = root / "case-manager.sqlite"
        write_jsonl(timeline_file, payment_conversation, group_conversation)
        write_jsonl(decision_file, decision_event)
        write_jsonl(action_file, action_event)

        domain_state = run_domain_events_once(
            [timeline_file, decision_file, action_file],
            domain_file,
            domain_state_file,
            domain_db,
            limit=100,
        )
        assert domain_state["status"] == "ok", domain_state
        assert domain_state["ingested"]["domainEvents"] >= 8, domain_state

        state = run_case_manager_once(
            domain_file,
            case_events_file,
            case_state_file,
            case_db,
            report_file=case_report_file,
            limit=200,
        )
        assert state["status"] == "attention", state
        assert not validate_contract(state, "case_manager_state"), state
        assert state["summary"]["openCases"] >= 1, state
        assert state["summary"]["needsHuman"] >= 1, state
        assert state["summary"]["ignoredCases"] >= 1, state
        assert state["summary"]["accountingCases"] >= 1, state
        assert state["summary"]["priceCases"] >= 1, state
        assert state["summary"]["serviceCases"] >= 1, state
        assert case_report_file.exists()
        assert case_events_file.exists()

        case_events = read_jsonl(case_events_file)
        case_types = {event["eventType"] for event in case_events}
        assert "CaseOpened" in case_types, case_types
        assert "CaseUpdated" in case_types, case_types
        assert "CaseNeedsHumanContext" in case_types, case_types
        for event in case_events:
            assert not validate_contract(event, "domain_event"), event
            assert event["data"]["caseManagerEvent"] is True
            assert event["caseId"]

        repeated = run_case_manager_once(
            domain_file,
            case_events_file,
            case_state_file,
            case_db,
            report_file=case_report_file,
            limit=200,
        )
        assert repeated["ingested"]["casesCreated"] == 0, repeated
        assert repeated["ingested"]["casesUpdated"] == 0, repeated
        assert repeated["ingested"]["duplicates"] >= state["ingested"]["events"], repeated

        after_case_state = run_domain_events_once(
            [case_events_file],
            domain_file,
            domain_state_file,
            domain_db,
            limit=100,
        )
        assert after_case_state["ingested"]["invalid"] == 0, after_case_state
        assert "CaseOpened" in after_case_state["summary"]["byType"], after_case_state
        assert "CaseNeedsHumanContext" in after_case_state["summary"]["byType"], after_case_state

    print("case manager OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
